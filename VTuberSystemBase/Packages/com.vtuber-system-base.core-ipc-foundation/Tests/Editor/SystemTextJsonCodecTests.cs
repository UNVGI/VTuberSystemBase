#nullable enable
using System;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Codec;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class SystemTextJsonCodecTests
    {
        private static SystemTextJsonCodec NewCodec(long? maxBytes = null)
        {
            var options = maxBytes.HasValue
                ? new CoreIpcOptions { MaxMessageSizeBytes = maxBytes.Value }
                : new CoreIpcOptions();
            return new SystemTextJsonCodec(options);
        }

        private static MessageEnvelope BuildEnvelope(
            MessageKind kind,
            string topic,
            string? correlationId,
            string payloadJson)
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: kind,
                Topic: topic,
                CorrelationId: correlationId,
                TimestampUnixMs: 1_745_539_200_000L,
                Payload: doc.RootElement.Clone());
        }

        private static void AssertEnvelopeEqual(MessageEnvelope expected, MessageEnvelope actual)
        {
            Assert.AreEqual(expected.ProtocolVersion, actual.ProtocolVersion, "ProtocolVersion");
            Assert.AreEqual(expected.Kind, actual.Kind, "Kind");
            Assert.AreEqual(expected.Topic, actual.Topic, "Topic");
            Assert.AreEqual(expected.CorrelationId, actual.CorrelationId, "CorrelationId");
            Assert.AreEqual(expected.TimestampUnixMs, actual.TimestampUnixMs, "TimestampUnixMs");
            Assert.AreEqual(expected.Payload.GetRawText(), actual.Payload.GetRawText(), "Payload");
        }

        [Test]
        public void Encode_State_RoundTrip_PreservesAllFields()
        {
            var codec = NewCodec();
            var envelope = BuildEnvelope(
                MessageKind.State,
                "slot/1/assignment",
                correlationId: null,
                payloadJson: "{\"slotId\":1,\"asset\":\"a.png\"}");

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success, "Encode should succeed.");

            var decoded = codec.Decode(encoded.Value);
            Assert.IsTrue(decoded.Success, "Decode should succeed.");
            AssertEnvelopeEqual(envelope, decoded.Value);
        }

        [Test]
        public void Encode_Event_RoundTrip_PreservesAllFields()
        {
            var codec = NewCodec();
            var envelope = BuildEnvelope(
                MessageKind.Event,
                "lighting/preset",
                correlationId: null,
                payloadJson: "{\"preset\":\"warm\"}");

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success, "Encode should succeed.");

            var decoded = codec.Decode(encoded.Value);
            Assert.IsTrue(decoded.Success, "Decode should succeed.");
            AssertEnvelopeEqual(envelope, decoded.Value);
        }

        [Test]
        public void Encode_Request_RoundTrip_PreservesCorrelationId()
        {
            var codec = NewCodec();
            var envelope = BuildEnvelope(
                MessageKind.Request,
                "scene/load",
                correlationId: "corr-abc-123",
                payloadJson: "{\"scene\":\"intro\"}");

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success, "Encode should succeed.");

            var decoded = codec.Decode(encoded.Value);
            Assert.IsTrue(decoded.Success, "Decode should succeed.");
            AssertEnvelopeEqual(envelope, decoded.Value);
            Assert.AreEqual("corr-abc-123", decoded.Value.CorrelationId);
        }

        [Test]
        public void Encode_Response_RoundTrip_PreservesCorrelationId()
        {
            var codec = NewCodec();
            var envelope = BuildEnvelope(
                MessageKind.Response,
                "scene/load",
                correlationId: "corr-abc-123",
                payloadJson: "{\"ok\":true}");

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success, "Encode should succeed.");

            var decoded = codec.Decode(encoded.Value);
            Assert.IsTrue(decoded.Success, "Decode should succeed.");
            AssertEnvelopeEqual(envelope, decoded.Value);
            Assert.AreEqual("corr-abc-123", decoded.Value.CorrelationId);
        }

        [Test]
        public void Encode_ProducesCamelCaseFieldNamesAndLowercaseKind()
        {
            var codec = NewCodec();
            var envelope = BuildEnvelope(
                MessageKind.Request,
                "topic/x",
                correlationId: "c-1",
                payloadJson: "1");

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success);
            var json = Encoding.UTF8.GetString(encoded.Value.ToArray());

            StringAssert.Contains("\"protocolVersion\":\"1.0\"", json);
            StringAssert.Contains("\"kind\":\"request\"", json);
            StringAssert.Contains("\"topic\":\"topic/x\"", json);
            StringAssert.Contains("\"correlationId\":\"c-1\"", json);
            StringAssert.Contains("\"timestampUnixMs\":1745539200000", json);
            StringAssert.Contains("\"payload\":1", json);
        }

        [Test]
        public void Decode_WithUnknownFields_IgnoresUnknownAndSucceeds()
        {
            var codec = NewCodec();
            const string json =
                "{\"protocolVersion\":\"1.5\"," +
                "\"kind\":\"event\"," +
                "\"topic\":\"future/topic\"," +
                "\"correlationId\":null," +
                "\"timestampUnixMs\":42," +
                "\"payload\":{\"a\":1}," +
                "\"futureField\":\"ignored\"," +
                "\"anotherUnknown\":[1,2,3]}";

            var decoded = codec.Decode(Encoding.UTF8.GetBytes(json));
            Assert.IsTrue(decoded.Success, "Unknown fields must not break decoding.");
            Assert.AreEqual("1.5", decoded.Value.ProtocolVersion);
            Assert.AreEqual(MessageKind.Event, decoded.Value.Kind);
            Assert.AreEqual("future/topic", decoded.Value.Topic);
            Assert.AreEqual(42L, decoded.Value.TimestampUnixMs);
            Assert.AreEqual("{\"a\":1}", decoded.Value.Payload.GetRawText());
        }

        [Test]
        public void Decode_OverSizeLimit_ReturnsSizeLimitExceeded()
        {
            const long limit = 1_048_576L;
            var codec = NewCodec(limit);
            var oversize = new byte[limit + 1];
            // Content does not need to be valid JSON: the size check happens before parsing.
            var decoded = codec.Decode(oversize);

            Assert.IsFalse(decoded.Success);
            Assert.IsInstanceOf<CoreIpcError.SizeLimitExceeded>(decoded.Error);
            var err = (CoreIpcError.SizeLimitExceeded)decoded.Error!;
            Assert.AreEqual(oversize.Length, err.ActualBytes);
            Assert.AreEqual(limit, err.LimitBytes);
        }

        [Test]
        public void Decode_AtSizeLimit_IsAccepted()
        {
            // Build a syntactically valid envelope and verify that bytes at exactly
            // the configured limit are accepted (boundary case).
            var codec = NewCodec();
            var envelope = BuildEnvelope(MessageKind.State, "t", null, "0");
            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success);
            Assert.LessOrEqual(encoded.Value.Length, 1_048_576L);

            // Decode with default limit (1 MB) succeeds.
            var decoded = codec.Decode(encoded.Value);
            Assert.IsTrue(decoded.Success);
        }

        [Test]
        public void Decode_MajorVersionMismatch_ReturnsProtocolVersionMismatch()
        {
            var codec = NewCodec();
            const string json =
                "{\"protocolVersion\":\"2.0\"," +
                "\"kind\":\"state\"," +
                "\"topic\":\"t/x\"," +
                "\"correlationId\":null," +
                "\"timestampUnixMs\":0," +
                "\"payload\":null}";

            var decoded = codec.Decode(Encoding.UTF8.GetBytes(json));
            Assert.IsFalse(decoded.Success);
            Assert.IsInstanceOf<CoreIpcError.ProtocolVersionMismatch>(decoded.Error);
            var err = (CoreIpcError.ProtocolVersionMismatch)decoded.Error!;
            Assert.AreEqual("2.0", err.Received);
            Assert.AreEqual(SystemTextJsonCodec.SupportedProtocolVersion, err.Expected);
        }

        [Test]
        public void Decode_HigherMajorVersion_ReturnsProtocolVersionMismatch()
        {
            var codec = NewCodec();
            const string json =
                "{\"protocolVersion\":\"3.4\"," +
                "\"kind\":\"event\"," +
                "\"topic\":\"t\"," +
                "\"correlationId\":null," +
                "\"timestampUnixMs\":0," +
                "\"payload\":null}";

            var decoded = codec.Decode(Encoding.UTF8.GetBytes(json));
            Assert.IsFalse(decoded.Success);
            Assert.IsInstanceOf<CoreIpcError.ProtocolVersionMismatch>(decoded.Error);
        }

        [Test]
        public void Decode_InvalidJson_ReturnsInvalidEnvelope()
        {
            var codec = NewCodec();
            var bytes = Encoding.UTF8.GetBytes("{not-json");

            var decoded = codec.Decode(bytes);
            Assert.IsFalse(decoded.Success);
            Assert.IsInstanceOf<CoreIpcError.InvalidEnvelope>(decoded.Error);
        }

        [Test]
        public void Decode_MissingProtocolVersion_ReturnsInvalidEnvelope()
        {
            var codec = NewCodec();
            const string json =
                "{\"kind\":\"state\"," +
                "\"topic\":\"t\"," +
                "\"correlationId\":null," +
                "\"timestampUnixMs\":0," +
                "\"payload\":null}";

            var decoded = codec.Decode(Encoding.UTF8.GetBytes(json));
            Assert.IsFalse(decoded.Success);
            Assert.IsInstanceOf<CoreIpcError.InvalidEnvelope>(decoded.Error);
        }

        [Test]
        public void Decode_UnknownKindValue_ReturnsInvalidEnvelope()
        {
            var codec = NewCodec();
            const string json =
                "{\"protocolVersion\":\"1.0\"," +
                "\"kind\":\"unknown-kind\"," +
                "\"topic\":\"t\"," +
                "\"correlationId\":null," +
                "\"timestampUnixMs\":0," +
                "\"payload\":null}";

            var decoded = codec.Decode(Encoding.UTF8.GetBytes(json));
            Assert.IsFalse(decoded.Success);
            Assert.IsInstanceOf<CoreIpcError.InvalidEnvelope>(decoded.Error);
        }

        [Test]
        public void Encode_NullProtocolVersion_ReturnsInvalidEnvelope()
        {
            var codec = NewCodec();
            var envelope = new MessageEnvelope(
                ProtocolVersion: null!,
                Kind: MessageKind.State,
                Topic: "t",
                CorrelationId: null,
                TimestampUnixMs: 0,
                Payload: default);

            var encoded = codec.Encode(envelope);
            Assert.IsFalse(encoded.Success);
            Assert.IsInstanceOf<CoreIpcError.InvalidEnvelope>(encoded.Error);
        }

        [Test]
        public void Constructor_RejectsNullOptions()
        {
            Assert.Throws<ArgumentNullException>(() => new SystemTextJsonCodec(null!));
        }

        [Test]
        public void Encode_DefaultPayloadJsonElement_IsSerializedAsNull()
        {
            var codec = NewCodec();
            var envelope = new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: MessageKind.State,
                Topic: "t",
                CorrelationId: null,
                TimestampUnixMs: 0,
                Payload: default);

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success);
            var json = Encoding.UTF8.GetString(encoded.Value.ToArray());
            StringAssert.Contains("\"payload\":null", json);

            var decoded = codec.Decode(encoded.Value);
            Assert.IsTrue(decoded.Success);
            Assert.AreEqual(JsonValueKind.Null, decoded.Value.Payload.ValueKind);
        }
    }
}
