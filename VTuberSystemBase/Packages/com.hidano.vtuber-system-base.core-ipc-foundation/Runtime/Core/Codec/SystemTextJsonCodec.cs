#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Codec
{
    public sealed class SystemTextJsonCodec : IMessageCodec
    {
        public const string SupportedProtocolVersion = "1.0";

        private const int ExpectedMajorVersion = 1;

        private static readonly JsonElement NullPayload = JsonDocument.Parse("null").RootElement.Clone();

        private readonly long _maxMessageSizeBytes;
        private readonly JsonSerializerOptions _serializerOptions;

        public SystemTextJsonCodec()
            : this(new CoreIpcOptions())
        {
        }

        public SystemTextJsonCodec(CoreIpcOptions options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (options.MaxMessageSizeBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "CoreIpcOptions.MaxMessageSizeBytes must be a positive value.");
            }

            _maxMessageSizeBytes = options.MaxMessageSizeBytes;
            _serializerOptions = CreateSerializerOptions();
        }

        public IpcResult<ReadOnlyMemory<byte>> Encode(in MessageEnvelope envelope)
        {
            if (envelope.ProtocolVersion is null)
            {
                return IpcResult<ReadOnlyMemory<byte>>.Fail(
                    new CoreIpcError.InvalidEnvelope("protocolVersion is null."));
            }

            if (envelope.Topic is null)
            {
                return IpcResult<ReadOnlyMemory<byte>>.Fail(
                    new CoreIpcError.InvalidEnvelope("topic is null."));
            }

            var payload = envelope.Payload.ValueKind == JsonValueKind.Undefined
                ? NullPayload
                : envelope.Payload;

            var dto = new EnvelopeDto
            {
                ProtocolVersion = envelope.ProtocolVersion,
                Kind = envelope.Kind,
                Topic = envelope.Topic,
                CorrelationId = envelope.CorrelationId,
                TimestampUnixMs = envelope.TimestampUnixMs,
                Payload = payload,
            };

            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, _serializerOptions);
                return IpcResult<ReadOnlyMemory<byte>>.Ok(bytes);
            }
            catch (JsonException ex)
            {
                return IpcResult<ReadOnlyMemory<byte>>.Fail(new CoreIpcError.InvalidEnvelope(ex.Message));
            }
        }

        public IpcResult<MessageEnvelope> Decode(ReadOnlyMemory<byte> bytes)
        {
            if (bytes.Length > _maxMessageSizeBytes)
            {
                return IpcResult<MessageEnvelope>.Fail(
                    new CoreIpcError.SizeLimitExceeded(bytes.Length, _maxMessageSizeBytes));
            }

            EnvelopeDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<EnvelopeDto>(bytes.Span, _serializerOptions);
            }
            catch (JsonException ex)
            {
                return IpcResult<MessageEnvelope>.Fail(new CoreIpcError.InvalidEnvelope(ex.Message));
            }

            if (dto is null)
            {
                return IpcResult<MessageEnvelope>.Fail(
                    new CoreIpcError.InvalidEnvelope("Decoded envelope is null."));
            }

            if (string.IsNullOrEmpty(dto.ProtocolVersion))
            {
                return IpcResult<MessageEnvelope>.Fail(
                    new CoreIpcError.InvalidEnvelope("protocolVersion is missing."));
            }

            if (!TryParseMajorVersion(dto.ProtocolVersion!, out var receivedMajor))
            {
                return IpcResult<MessageEnvelope>.Fail(new CoreIpcError.InvalidEnvelope(
                    $"protocolVersion '{dto.ProtocolVersion}' is not a valid semantic version."));
            }

            if (receivedMajor != ExpectedMajorVersion)
            {
                return IpcResult<MessageEnvelope>.Fail(
                    new CoreIpcError.ProtocolVersionMismatch(dto.ProtocolVersion!, SupportedProtocolVersion));
            }

            if (string.IsNullOrEmpty(dto.Topic))
            {
                return IpcResult<MessageEnvelope>.Fail(
                    new CoreIpcError.InvalidEnvelope("topic is missing."));
            }

            var envelope = new MessageEnvelope(
                ProtocolVersion: dto.ProtocolVersion!,
                Kind: dto.Kind,
                Topic: dto.Topic!,
                CorrelationId: dto.CorrelationId,
                TimestampUnixMs: dto.TimestampUnixMs,
                Payload: dto.Payload.ValueKind == JsonValueKind.Undefined ? NullPayload : dto.Payload);

            return IpcResult<MessageEnvelope>.Ok(envelope);
        }

        private static JsonSerializerOptions CreateSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            return options;
        }

        private static bool TryParseMajorVersion(string version, out int major)
        {
            major = 0;
            if (string.IsNullOrEmpty(version)) return false;

            var dotIndex = version.IndexOf('.');
            var head = dotIndex < 0 ? version : version.Substring(0, dotIndex);
            return int.TryParse(head, out major) && major >= 0;
        }

        private sealed class EnvelopeDto
        {
            [JsonPropertyName("protocolVersion")]
            public string? ProtocolVersion { get; set; }

            [JsonPropertyName("kind")]
            public MessageKind Kind { get; set; }

            [JsonPropertyName("topic")]
            public string? Topic { get; set; }

            [JsonPropertyName("correlationId")]
            public string? CorrelationId { get; set; }

            [JsonPropertyName("timestampUnixMs")]
            public long TimestampUnixMs { get; set; }

            [JsonPropertyName("payload")]
            public JsonElement Payload { get; set; }
        }
    }
}
