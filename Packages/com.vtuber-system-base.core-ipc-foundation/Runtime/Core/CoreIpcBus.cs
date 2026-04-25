#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Correlation;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Subscription;

namespace VTuberSystemBase.CoreIpc.Core
{
    public sealed class CoreIpcBus : ICoreIpcBus
    {
        public const string DefaultProtocolVersion = "1.0";

        private readonly IMessageCodec _codec;
        private readonly IIpcOutboundChannel _outbound;
        private readonly RequestCorrelationRegistry _correlation;
        private readonly TopicSubscriptionRegistry _subscriptions;
        private readonly IConnectionDiagnostics _diagnostics;
        private readonly long _maxMessageSizeBytes;
        private readonly Func<long> _timestampProvider;
        private readonly string _protocolVersion;
        private readonly JsonSerializerOptions _payloadSerializerOptions;
        private readonly Action<string, Exception>? _logError;

        public CoreIpcBus(
            CoreIpcOptions options,
            IMessageCodec codec,
            IIpcOutboundChannel outboundChannel,
            RequestCorrelationRegistry correlationRegistry,
            TopicSubscriptionRegistry subscriptionRegistry,
            IConnectionDiagnostics diagnostics,
            Func<long>? timestampProvider = null,
            JsonSerializerOptions? payloadSerializerOptions = null,
            Action<string, Exception>? logError = null)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (options.MaxMessageSizeBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "CoreIpcOptions.MaxMessageSizeBytes must be a positive value.");
            }

            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _outbound = outboundChannel ?? throw new ArgumentNullException(nameof(outboundChannel));
            _correlation = correlationRegistry ?? throw new ArgumentNullException(nameof(correlationRegistry));
            _subscriptions = subscriptionRegistry ?? throw new ArgumentNullException(nameof(subscriptionRegistry));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _maxMessageSizeBytes = options.MaxMessageSizeBytes;
            _timestampProvider = timestampProvider ?? DefaultTimestampProvider;
            _protocolVersion = DefaultProtocolVersion;
            _payloadSerializerOptions = payloadSerializerOptions ?? CreatePayloadSerializerOptions();
            _logError = logError;
        }

        public IConnectionDiagnostics Diagnostics => _diagnostics;

        public IpcResult PublishState<TPayload>(string topic, TPayload payload)
            => Publish(MessageKind.State, topic, payload, correlationId: null);

        public IpcResult PublishEvent<TPayload>(string topic, TPayload payload)
            => Publish(MessageKind.Event, topic, payload, correlationId: null);

        public async Task<IpcResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            RequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(topic))
            {
                return IpcResult<TResponse>.Fail(new CoreIpcError.InvalidTopic(topic ?? string.Empty));
            }

            if (!_outbound.IsConnected)
            {
                return IpcResult<TResponse>.Fail(new CoreIpcError.NotConnected());
            }

            var correlationId = _correlation.AllocateCorrelationId();
            var encoded = BuildAndEncode(MessageKind.Request, topic, payload, correlationId);
            if (!encoded.Success)
            {
                return IpcResult<TResponse>.Fail(encoded.Error!);
            }

            var timeout = options.HasValue ? options.Value.Timeout : _correlation.DefaultTimeout;

            Task<IpcResult<JsonElement>> pendingTask;
            try
            {
                pendingTask = _correlation.RegisterPending(correlationId, timeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return IpcResult<TResponse>.Fail(new CoreIpcError.TransportFailure(ex.Message));
            }

            try
            {
                await _outbound.SendAsync(encoded.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _correlation.FailPending(
                    correlationId,
                    new CoreIpcError.TransportFailure("Request send was canceled."));
                throw;
            }
            catch (Exception ex)
            {
                _correlation.FailPending(
                    correlationId,
                    new CoreIpcError.TransportFailure(ex.Message));
                _logError?.Invoke("CoreIpcBus.RequestAsync send failed.", ex);
            }

            var responseResult = await pendingTask.ConfigureAwait(false);
            if (!responseResult.Success)
            {
                return IpcResult<TResponse>.Fail(responseResult.Error!);
            }

            try
            {
                var deserialized = DeserializePayload<TResponse>(responseResult.Value);
                return IpcResult<TResponse>.Ok(deserialized!);
            }
            catch (JsonException ex)
            {
                return IpcResult<TResponse>.Fail(new CoreIpcError.InvalidEnvelope(ex.Message));
            }
        }

        public ISubscriptionToken SubscribeState<TPayload>(string topic, Action<TPayload> handler)
            => RegisterTypedSubscription(topic, MessageKind.State, handler);

        public ISubscriptionToken SubscribeEvent<TPayload>(string topic, Action<TPayload> handler)
            => RegisterTypedSubscription(topic, MessageKind.Event, handler);

        public ISubscriptionToken RegisterRequestHandler<TRequest, TResponse>(
            string topic,
            Func<TRequest, CancellationToken, Task<TResponse>> handler)
        {
            if (topic is null) throw new ArgumentNullException(nameof(topic));
            if (topic.Length == 0)
            {
                throw new ArgumentException("topic must be a non-empty string.", nameof(topic));
            }
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            var capturedTopic = topic;
            DispatchHandler dispatch = envelope =>
            {
                var correlationId = envelope.CorrelationId;
                if (string.IsNullOrEmpty(correlationId))
                {
                    _logError?.Invoke(
                        "CoreIpcBus dropped Request envelope without correlationId on topic '" +
                        envelope.Topic + "'.",
                        new InvalidOperationException("Request envelope missing correlationId."));
                    return;
                }

                var payloadElement = envelope.Payload;
                _ = InvokeRequestHandlerAsync(
                    capturedTopic,
                    correlationId!,
                    payloadElement,
                    handler);
            };

            return _subscriptions.Register(topic, MessageKind.Request, dispatch, typeof(TRequest));
        }

        private async Task InvokeRequestHandlerAsync<TRequest, TResponse>(
            string topic,
            string correlationId,
            JsonElement requestPayload,
            Func<TRequest, CancellationToken, Task<TResponse>> handler)
        {
            try
            {
                var deserialized = DeserializePayload<TRequest>(requestPayload);
                var response = await handler(deserialized!, CancellationToken.None).ConfigureAwait(false);

                var encoded = BuildAndEncode(MessageKind.Response, topic, response, correlationId);
                if (!encoded.Success)
                {
                    _logError?.Invoke(
                        "CoreIpcBus failed to encode response for topic '" + topic +
                        "' (correlationId=" + correlationId + "): " + encoded.Error!.Message,
                        new InvalidOperationException(encoded.Error.Message));
                    return;
                }

                if (!_outbound.IsConnected)
                {
                    return;
                }

                await _outbound.SendAsync(encoded.Value, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logError?.Invoke(
                    "CoreIpcBus request handler for topic '" + topic +
                    "' (correlationId=" + correlationId + ") threw: " + ex.Message,
                    ex);
            }
        }

        private ISubscriptionToken RegisterTypedSubscription<TPayload>(
            string topic, MessageKind kind, Action<TPayload> handler)
        {
            if (topic is null) throw new ArgumentNullException(nameof(topic));
            if (topic.Length == 0)
            {
                throw new ArgumentException("topic must be a non-empty string.", nameof(topic));
            }
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            DispatchHandler dispatch = envelope =>
            {
                TPayload? deserialized;
                try
                {
                    deserialized = DeserializePayload<TPayload>(envelope.Payload);
                }
                catch (JsonException ex)
                {
                    _logError?.Invoke(
                        "CoreIpcBus failed to deserialize payload for topic '" +
                        envelope.Topic + "' (kind=" + kind + "): " + ex.Message,
                        ex);
                    return;
                }
                handler(deserialized!);
            };

            return _subscriptions.Register(topic, kind, dispatch, typeof(TPayload));
        }

        private IpcResult Publish<TPayload>(
            MessageKind kind, string topic, TPayload payload, string? correlationId)
        {
            if (string.IsNullOrEmpty(topic))
            {
                return IpcResult.Fail(new CoreIpcError.InvalidTopic(topic ?? string.Empty));
            }

            if (!_outbound.IsConnected)
            {
                return IpcResult.Fail(new CoreIpcError.NotConnected());
            }

            var encoded = BuildAndEncode(kind, topic, payload, correlationId);
            if (!encoded.Success)
            {
                return IpcResult.Fail(encoded.Error!);
            }

            try
            {
                var sendTask = _outbound.SendAsync(encoded.Value, CancellationToken.None);
                if (sendTask.IsCompleted)
                {
                    sendTask.GetAwaiter().GetResult();
                }
                else
                {
                    var task = sendTask.AsTask();
                    task.ContinueWith(static (t, state) =>
                    {
                        var logger = (Action<string, Exception>?)state;
                        if (t.Exception is not null && logger is not null)
                        {
                            logger("CoreIpcBus.Publish background send faulted.", t.Exception);
                        }
                    }, _logError, TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                return IpcResult.Fail(new CoreIpcError.TransportFailure(ex.Message));
            }

            return IpcResult.Ok();
        }

        private IpcResult<ReadOnlyMemory<byte>> BuildAndEncode<TPayload>(
            MessageKind kind, string topic, TPayload payload, string? correlationId)
        {
            JsonElement payloadElement;
            try
            {
                payloadElement = SerializePayload(payload);
            }
            catch (JsonException ex)
            {
                return IpcResult<ReadOnlyMemory<byte>>.Fail(new CoreIpcError.InvalidEnvelope(ex.Message));
            }

            var envelope = new MessageEnvelope(
                ProtocolVersion: _protocolVersion,
                Kind: kind,
                Topic: topic,
                CorrelationId: correlationId,
                TimestampUnixMs: _timestampProvider(),
                Payload: payloadElement);

            var encoded = _codec.Encode(envelope);
            if (!encoded.Success) return encoded;

            if (encoded.Value.Length > _maxMessageSizeBytes)
            {
                return IpcResult<ReadOnlyMemory<byte>>.Fail(
                    new CoreIpcError.SizeLimitExceeded(encoded.Value.Length, _maxMessageSizeBytes));
            }
            return encoded;
        }

        private JsonElement SerializePayload<TPayload>(TPayload payload)
        {
            if (payload is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.Undefined
                    ? JsonDocument.Parse("null").RootElement.Clone()
                    : element;
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _payloadSerializerOptions);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }

        private TPayload? DeserializePayload<TPayload>(JsonElement element)
        {
            if (typeof(TPayload) == typeof(JsonElement))
            {
                return (TPayload)(object)element;
            }

            byte[] bytes;
            if (element.ValueKind == JsonValueKind.Undefined)
            {
                bytes = NullLiteralBytes;
            }
            else
            {
                bytes = JsonSerializer.SerializeToUtf8Bytes(element, _payloadSerializerOptions);
            }

            return JsonSerializer.Deserialize<TPayload>(bytes, _payloadSerializerOptions);
        }

        private static readonly byte[] NullLiteralBytes = System.Text.Encoding.UTF8.GetBytes("null");

        private static long DefaultTimestampProvider() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static JsonSerializerOptions CreatePayloadSerializerOptions()
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            };
            opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
            return opts;
        }
    }
}
