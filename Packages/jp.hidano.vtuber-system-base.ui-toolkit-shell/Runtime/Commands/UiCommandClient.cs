#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using LogLevel = VTuberSystemBase.UiToolkitShell.Diagnostics.LogLevel;
using IpcMessageKind = VTuberSystemBase.CoreIpc.Abstractions.MessageKind;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Default <see cref="IUiCommandClient"/> implementation. Acts as a thin Facade over the
    /// injected <see cref="ICoreIpcBus"/> abstraction: validates topic syntax, short-circuits
    /// sends to <see cref="SendErrorCode.NotConnected"/> when the bus diagnostics report a
    /// non-<c>Connected</c> state, maps <see cref="CoreIpcError"/> values returned by the bus
    /// to the UI-facing <see cref="SendErrorCode"/> / <see cref="RequestErrorCode"/> taxonomy,
    /// and emits a <c>SendStarted</c> / <c>SendResult</c> log pair per send via the injected
    /// <see cref="IDiagnosticsLogger"/> (<see cref="LogCategory.Ipc"/>).
    /// The class never references a concrete transport (e.g. WebSocket) — by Runtime asmdef
    /// constraint the only dependency on core-ipc is the abstraction package
    /// (Requirement 5.10).
    /// See design.md §Commands §UiCommandClient.
    /// </summary>
    public sealed class UiCommandClient : IUiCommandClient
    {
        private static readonly Regex TopicPattern = new Regex(
            "^[A-Za-z0-9/_-]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ICoreIpcBus _bus;
        private readonly IConnectionStatus _status;
        private readonly IDiagnosticsLogger _logger;
        private long _correlationCounter;

        public UiCommandClient(ICoreIpcBus bus, IConnectionStatus status, IDiagnosticsLogger logger)
        {
            if (bus is null) throw new ArgumentNullException(nameof(bus));
            if (status is null) throw new ArgumentNullException(nameof(status));
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            _bus = bus;
            _status = status;
            _logger = logger;
        }

        /// <summary>
        /// Exposes the bound <see cref="IConnectionStatus"/> so callers (e.g. notification bar /
        /// reconnect-aware tab spec code) can subscribe to <c>OnStatusChanged</c> without holding
        /// a separate reference. The send path itself queries the underlying bus diagnostics
        /// directly (see <c>IsBusConnected</c>) to avoid coupling to the status's startup
        /// <c>Initializing</c> grace state.
        /// </summary>
        public IConnectionStatus ConnectionStatus => _status;

        public SendResult PublishState<TPayload>(string topic, TPayload payload)
        {
            return SendInternal(IpcMessageKind.State, topic, () => _bus.PublishState(topic, payload));
        }

        public SendResult PublishEvent<TPayload>(string topic, TPayload payload)
        {
            return SendInternal(IpcMessageKind.Event, topic, () => _bus.PublishEvent(topic, payload));
        }

        public async Task<RequestResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var correlationId = Interlocked.Increment(ref _correlationCounter).ToString();
            LogStarted(IpcMessageKind.Request, topic, correlationId);

            RequestResult<TResponse> result;
            if (!IsTopicValid(topic))
            {
                result = RequestResult<TResponse>.Fail(new RequestError(
                    RequestErrorCode.TopicInvalid,
                    correlationId,
                    $"Topic '{DisplayTopic(topic)}' is null, empty, or contains characters outside [A-Za-z0-9/_-]"));
            }
            else if (!IsBusConnected)
            {
                result = RequestResult<TResponse>.Fail(new RequestError(
                    RequestErrorCode.NotConnected,
                    correlationId,
                    "IPC connection is not established."));
            }
            else
            {
                try
                {
                    RequestOptions? options = timeout.HasValue ? new RequestOptions(timeout.Value) : (RequestOptions?)null;
                    var ipcResult = await _bus.RequestAsync<TRequest, TResponse>(topic, payload, options, cancellationToken).ConfigureAwait(false);
                    if (ipcResult.Success)
                    {
                        result = RequestResult<TResponse>.Ok(ipcResult.Value!);
                    }
                    else
                    {
                        result = RequestResult<TResponse>.Fail(MapRequestError(ipcResult.Error!, correlationId));
                    }
                }
                catch (OperationCanceledException)
                {
                    result = RequestResult<TResponse>.Fail(new RequestError(
                        RequestErrorCode.Cancelled,
                        correlationId,
                        "Request was cancelled."));
                }
                catch (Exception ex)
                {
                    result = RequestResult<TResponse>.Fail(new RequestError(
                        RequestErrorCode.SerializationFailed,
                        correlationId,
                        ex.Message));
                }
            }

            LogResult(IpcMessageKind.Request, topic, correlationId, result.Success, result.Error?.Code.ToString());
            return result;
        }

        private SendResult SendInternal(IpcMessageKind kind, string topic, Func<IpcResult> publish)
        {
            LogStarted(kind, topic, correlationId: null);

            SendResult result;
            if (!IsTopicValid(topic))
            {
                result = SendResult.Fail(new SendError(
                    SendErrorCode.TopicInvalid,
                    $"Topic '{DisplayTopic(topic)}' is null, empty, or contains characters outside [A-Za-z0-9/_-]"));
            }
            else if (!IsBusConnected)
            {
                result = SendResult.Fail(new SendError(
                    SendErrorCode.NotConnected,
                    "IPC connection is not established."));
            }
            else
            {
                try
                {
                    var ipcResult = publish();
                    result = ipcResult.Success
                        ? SendResult.Ok()
                        : SendResult.Fail(MapSendError(ipcResult.Error!));
                }
                catch (Exception ex)
                {
                    result = SendResult.Fail(new SendError(SendErrorCode.SerializationFailed, ex.Message));
                }
            }

            LogResult(kind, topic, correlationId: null, result.Success, result.Error?.Code.ToString());
            return result;
        }

        private void LogStarted(IpcMessageKind kind, string topic, string? correlationId)
        {
            var message = correlationId is null
                ? $"SendStarted topic={DisplayTopic(topic)} kind={kind}"
                : $"SendStarted topic={DisplayTopic(topic)} kind={kind} correlationId={correlationId}";
            _logger.Log(LogLevel.Info, LogCategory.Ipc, message);
        }

        private void LogResult(IpcMessageKind kind, string topic, string? correlationId, bool success, string? errorCode)
        {
            var status = success ? "ok" : ("fail=" + (errorCode ?? "Unknown"));
            var message = correlationId is null
                ? $"SendResult topic={DisplayTopic(topic)} kind={kind} {status}"
                : $"SendResult topic={DisplayTopic(topic)} kind={kind} correlationId={correlationId} {status}";
            var level = success ? LogLevel.Info : LogLevel.Warning;
            _logger.Log(level, LogCategory.Ipc, message);
        }

        private bool IsBusConnected => _bus.Diagnostics.CurrentState == ConnectionState.Connected;

        private static bool IsTopicValid(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            return TopicPattern.IsMatch(topic);
        }

        private static string DisplayTopic(string topic) => string.IsNullOrEmpty(topic) ? "<empty>" : topic;

        private static SendError MapSendError(CoreIpcError error)
        {
            return error switch
            {
                CoreIpcError.NotConnected => new SendError(SendErrorCode.NotConnected, error.Message),
                CoreIpcError.SizeLimitExceeded => new SendError(SendErrorCode.PayloadTooLarge, error.Message),
                CoreIpcError.InvalidTopic => new SendError(SendErrorCode.TopicInvalid, error.Message),
                _ => new SendError(SendErrorCode.SerializationFailed, error.Message),
            };
        }

        private static RequestError MapRequestError(CoreIpcError error, string? correlationId)
        {
            return error switch
            {
                CoreIpcError.NotConnected => new RequestError(RequestErrorCode.NotConnected, correlationId, error.Message),
                CoreIpcError.RequestTimeout => new RequestError(RequestErrorCode.Timeout, correlationId, error.Message),
                CoreIpcError.SizeLimitExceeded => new RequestError(RequestErrorCode.PayloadTooLarge, correlationId, error.Message),
                CoreIpcError.InvalidTopic => new RequestError(RequestErrorCode.TopicInvalid, correlationId, error.Message),
                CoreIpcError.HandlerException he when string.Equals(he.Details, "Cancelled", StringComparison.Ordinal)
                    => new RequestError(RequestErrorCode.Cancelled, correlationId, error.Message),
                _ => new RequestError(RequestErrorCode.SerializationFailed, correlationId, error.Message),
            };
        }
    }
}
