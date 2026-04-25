#nullable enable
using System;
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class MessageEnvelopeAndEnumTypeTests
    {
        [Test]
        public void MessageKind_DefinesAllFourKinds()
        {
            Assert.AreEqual(0, (int)MessageKind.State);
            Assert.AreEqual(1, (int)MessageKind.Event);
            Assert.AreEqual(2, (int)MessageKind.Request);
            Assert.AreEqual(3, (int)MessageKind.Response);

            CollectionAssert.AreEquivalent(
                new[] { MessageKind.State, MessageKind.Event, MessageKind.Request, MessageKind.Response },
                Enum.GetValues(typeof(MessageKind)));
        }

        [Test]
        public void ConnectionState_DefinesAllFiveStates()
        {
            Assert.AreEqual(0, (int)ConnectionState.Disconnected);
            Assert.AreEqual(1, (int)ConnectionState.Connecting);
            Assert.AreEqual(2, (int)ConnectionState.Connected);
            Assert.AreEqual(3, (int)ConnectionState.Reconnecting);
            Assert.AreEqual(4, (int)ConnectionState.PermanentlyDisconnected);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    ConnectionState.Disconnected,
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                    ConnectionState.Reconnecting,
                    ConnectionState.PermanentlyDisconnected,
                },
                Enum.GetValues(typeof(ConnectionState)));
        }

        [Test]
        public void RuntimeState_DefinesAllFiveStates()
        {
            Assert.AreEqual(0, (int)RuntimeState.NotInitialized);
            Assert.AreEqual(1, (int)RuntimeState.Initializing);
            Assert.AreEqual(2, (int)RuntimeState.Running);
            Assert.AreEqual(3, (int)RuntimeState.ShuttingDown);
            Assert.AreEqual(4, (int)RuntimeState.Disposed);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    RuntimeState.NotInitialized,
                    RuntimeState.Initializing,
                    RuntimeState.Running,
                    RuntimeState.ShuttingDown,
                    RuntimeState.Disposed,
                },
                Enum.GetValues(typeof(RuntimeState)));
        }

        [Test]
        public void MessageEnvelope_IsReadonlyRecordStruct_AndExposesAllSpecifiedFields()
        {
            var envelopeType = typeof(MessageEnvelope);

            Assert.IsTrue(envelopeType.IsValueType, "MessageEnvelope must be a struct.");
            Assert.IsTrue(
                typeof(IEquatable<MessageEnvelope>).IsAssignableFrom(envelopeType),
                "Record struct must implement IEquatable<MessageEnvelope>.");
            Assert.IsNotNull(
                envelopeType.GetMethod("Deconstruct"),
                "Record struct with positional parameters must expose a Deconstruct method.");
            Assert.IsNotNull(
                envelopeType.GetMethod("PrintMembers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                "Record struct must expose a PrintMembers helper.");

            Assert.AreEqual(typeof(string), envelopeType.GetProperty("ProtocolVersion")?.PropertyType);
            Assert.AreEqual(typeof(MessageKind), envelopeType.GetProperty("Kind")?.PropertyType);
            Assert.AreEqual(typeof(string), envelopeType.GetProperty("Topic")?.PropertyType);
            Assert.AreEqual(typeof(string), envelopeType.GetProperty("CorrelationId")?.PropertyType);
            Assert.AreEqual(typeof(long), envelopeType.GetProperty("TimestampUnixMs")?.PropertyType);
            Assert.AreEqual(typeof(JsonElement), envelopeType.GetProperty("Payload")?.PropertyType);
        }

        [Test]
        public void MessageEnvelope_PreservesConstructorArguments()
        {
            using var doc = JsonDocument.Parse("{\"hello\":\"world\"}");
            var payload = doc.RootElement.Clone();

            var envelope = new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: MessageKind.Event,
                Topic: "lighting/preset",
                CorrelationId: "corr-123",
                TimestampUnixMs: 1_745_539_200_000L,
                Payload: payload);

            Assert.AreEqual("1.0", envelope.ProtocolVersion);
            Assert.AreEqual(MessageKind.Event, envelope.Kind);
            Assert.AreEqual("lighting/preset", envelope.Topic);
            Assert.AreEqual("corr-123", envelope.CorrelationId);
            Assert.AreEqual(1_745_539_200_000L, envelope.TimestampUnixMs);
            Assert.AreEqual(JsonValueKind.Object, envelope.Payload.ValueKind);
        }

        [Test]
        public void MessageEnvelope_AllowsNullCorrelationIdForStateAndEvent()
        {
            var envelope = new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: MessageKind.State,
                Topic: "slot/1/assignment",
                CorrelationId: null,
                TimestampUnixMs: 0L,
                Payload: default);

            Assert.IsNull(envelope.CorrelationId);
        }

        [Test]
        public void MessageEnvelope_StructuralEquality_OnIdenticalValues()
        {
            using var doc = JsonDocument.Parse("{\"x\":1}");
            var payload = doc.RootElement.Clone();

            var a = new MessageEnvelope("1.0", MessageKind.Request, "t/req", "c-1", 100L, payload);
            var b = new MessageEnvelope("1.0", MessageKind.Request, "t/req", "c-1", 100L, payload);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }
    }
}
