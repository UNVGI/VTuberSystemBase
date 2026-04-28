#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Correlation;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Subscription;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class CoreIpcBusTests
    {
        private sealed class SamplePayload
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;

            public SamplePayload() { }

            public SamplePayload(int id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        private sealed class FakeOutboundChannel : IIpcOutboundChannel
        {
            public bool IsConnectedValue { get; set; } = true;
            public List<byte[]> Sent { get; } = new();
            public Func<ReadOnlyMemory<byte>, Exception?>? SendThrowsFactory { get; set; }
            public bool ThrowOnSend { get; set; }

            public bool IsConnected => IsConnectedValue;

            public ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
            {
                if (ThrowOnSend)
                {
                    throw new InvalidOperationException("simulated send failure");
                }
                var custom = SendThrowsFactory?.Invoke(bytes);
                if (custom is not null) throw custom;

                Sent.Add(bytes.ToArray());
                return default;
            }
        }

        private sealed class StubDiagnostics : IConnectionDiagnostics
        {
            public ConnectionState CurrentState { get; set; } = ConnectionState.Disconnected;
            public int ReconnectAttemptCount { get; set; }
            public int PendingRequestCount { get; set; }
            public int StateSlotCount { get; set; }
            public int EventQueueCount { get; set; }
            public int ConnectedClientCount { get; set; }

            public event Action<ConnectionState, ConnectionState>? ConnectionStateChanged;

            public void RaiseStateChanged(ConnectionState previous, ConnectionState current)
            {
                ConnectionStateChanged?.Invoke(previous, current);
            }

            public DiagnosticsSnapshot TakeSnapshot()
            {
                return new DiagnosticsSnapshot(
                    TakenAt: DateTimeOffset.UnixEpoch,
                    ClientState: CurrentState,
                    ServerConnectedCount: ConnectedClientCount,
                    ReconnectAttemptCount: ReconnectAttemptCount,
                    PendingRequestCount: PendingRequestCount,
                    StateSlotCount: StateSlotCount,
                    EventQueueCount: EventQueueCount);
            }
        }

        private sealed class TestHarness
        {
            public CoreIpcOptions Options { get; }
            public SystemTextJsonCodec Codec { get; }
            public FakeOutboundChannel Outbound { get; } = new();
            public RequestCorrelationRegistry Correlation { get; }
            public TopicSubscriptionRegistry Subscriptions { get; } = new();
            public MainThreadDispatchQueue Dispatch { get; }
            public StubDiagnostics Diagnostics { get; } = new();
            public CoreIpcBus Bus { get; }

            public TestHarness(CoreIpcOptions? options = null, long fixedTimestamp = 1_700_000_000_000L)
            {
                Options = options ?? new CoreIpcOptions
                {
                    DefaultRequestTimeout = TimeSpan.FromMilliseconds(200),
                };
                Codec = new SystemTextJsonCodec(Options);
                Correlation = new RequestCorrelationRegistry(Options);
                Dispatch = new MainThreadDispatchQueue(Options);
                Dispatch.SetHandlerLookup(Subscriptions);
                Bus = new CoreIpcBus(
                    Options,
                    Codec,
                    Outbound,
                    Correlation,
                    Subscriptions,
                    Diagnostics,
                    timestampProvider: () => fixedTimestamp);
            }

            public MessageEnvelope DecodeLastSent()
            {
                Assert.That(Outbound.Sent, Is.Not.Empty);
                var bytes = Outbound.Sent[Outbound.Sent.Count - 1];
                var decoded = Codec.Decode(bytes);
                Assert.IsTrue(decoded.Success, "Codec.Decode of captured outbound bytes should succeed.");
                return decoded.Value;
            }
        }

        // ---------- Constructor ----------

        [Test]
        public void Ctor_NullOptions_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CoreIpcBus(
                null!,
                new SystemTextJsonCodec(),
                new FakeOutboundChannel(),
                new RequestCorrelationRegistry(),
                new TopicSubscriptionRegistry(),
                new StubDiagnostics()));
        }

        [Test]
        public void Ctor_NullCodec_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CoreIpcBus(
                new CoreIpcOptions(),
                null!,
                new FakeOutboundChannel(),
                new RequestCorrelationRegistry(),
                new TopicSubscriptionRegistry(),
                new StubDiagnostics()));
        }

        [Test]
        public void Ctor_NullOutbound_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CoreIpcBus(
                new CoreIpcOptions(),
                new SystemTextJsonCodec(),
                null!,
                new RequestCorrelationRegistry(),
                new TopicSubscriptionRegistry(),
                new StubDiagnostics()));
        }

        [Test]
        public void Diagnostics_ReturnsInjectedInstance()
        {
            var h = new TestHarness();
            Assert.AreSame(h.Diagnostics, h.Bus.Diagnostics);
        }

        // ---------- NotConnected (Req 5.4) ----------

        [Test]
        public void PublishState_WhenNotConnected_ReturnsNotConnected()
        {
            var h = new TestHarness();
            h.Outbound.IsConnectedValue = false;

            var result = h.Bus.PublishState("topic/a", new SamplePayload(1, "x"));

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.NotConnected>(result.Error);
            Assert.AreEqual(0, h.Outbound.Sent.Count, "no bytes should be sent when not connected");
        }

        [Test]
        public void PublishEvent_WhenNotConnected_ReturnsNotConnected()
        {
            var h = new TestHarness();
            h.Outbound.IsConnectedValue = false;

            var result = h.Bus.PublishEvent("topic/a", new SamplePayload(1, "x"));

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.NotConnected>(result.Error);
            Assert.AreEqual(0, h.Outbound.Sent.Count);
        }

        [Test]
        public async Task RequestAsync_WhenNotConnected_ReturnsNotConnected()
        {
            var h = new TestHarness();
            h.Outbound.IsConnectedValue = false;

            var result = await h.Bus.RequestAsync<SamplePayload, SamplePayload>(
                "topic/a", new SamplePayload(1, "x"));

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.NotConnected>(result.Error);
            Assert.AreEqual(0, h.Outbound.Sent.Count);
            Assert.AreEqual(0, h.Correlation.PendingRequestCount,
                "no pending correlation should be registered when disconnected");
        }

        // ---------- InvalidTopic ----------

        [Test]
        public void PublishState_WithEmptyTopic_ReturnsInvalidTopic()
        {
            var h = new TestHarness();
            var result = h.Bus.PublishState(string.Empty, new SamplePayload(1, "x"));

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.InvalidTopic>(result.Error);
            Assert.AreEqual(0, h.Outbound.Sent.Count);
        }

        [Test]
        public void PublishEvent_WithNullTopic_ReturnsInvalidTopic()
        {
            var h = new TestHarness();
            var result = h.Bus.PublishEvent(null!, new SamplePayload(1, "x"));

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.InvalidTopic>(result.Error);
            Assert.AreEqual(0, h.Outbound.Sent.Count);
        }

        [Test]
        public async Task RequestAsync_WithEmptyTopic_ReturnsInvalidTopic()
        {
            var h = new TestHarness();
            var result = await h.Bus.RequestAsync<SamplePayload, SamplePayload>(
                string.Empty, new SamplePayload(1, "x"));

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.InvalidTopic>(result.Error);
            Assert.AreEqual(0, h.Correlation.PendingRequestCount);
        }

        // ---------- SizeLimitExceeded (Req 3.9) ----------

        [Test]
        public void PublishState_WithOversizedPayload_ReturnsSizeLimitExceeded()
        {
            var options = new CoreIpcOptions { MaxMessageSizeBytes = 1024 };
            var h = new TestHarness(options);

            var bigPayload = new string('x', 2048);
            var result = h.Bus.PublishState("topic/big", bigPayload);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.SizeLimitExceeded>(result.Error);
            var sizeErr = (CoreIpcError.SizeLimitExceeded)result.Error!;
            Assert.AreEqual(1024L, sizeErr.LimitBytes);
            Assert.Greater(sizeErr.ActualBytes, 1024L);
            Assert.AreEqual(0, h.Outbound.Sent.Count, "no bytes should be sent when payload exceeds size limit");
        }

        [Test]
        public void PublishEvent_AtOrUnderLimit_Succeeds_ButOverLimit_Fails()
        {
            var options = new CoreIpcOptions { MaxMessageSizeBytes = 256 };
            var h = new TestHarness(options);

            var smallResult = h.Bus.PublishEvent("topic/s", new SamplePayload(1, "ok"));
            Assert.IsTrue(smallResult.Success, "small payload should succeed");

            var bigResult = h.Bus.PublishEvent("topic/s", new string('y', 1024));
            Assert.IsFalse(bigResult.Success);
            Assert.IsInstanceOf<CoreIpcError.SizeLimitExceeded>(bigResult.Error);
        }

        // ---------- Successful publish encodes correct envelope ----------

        [Test]
        public void PublishState_Success_SendsEncodedStateEnvelope()
        {
            var h = new TestHarness();
            var result = h.Bus.PublishState("topic/a", new SamplePayload(7, "hello"));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, h.Outbound.Sent.Count);

            var envelope = h.DecodeLastSent();
            Assert.AreEqual("1.0", envelope.ProtocolVersion);
            Assert.AreEqual(MessageKind.State, envelope.Kind);
            Assert.AreEqual("topic/a", envelope.Topic);
            Assert.IsNull(envelope.CorrelationId, "state envelopes must not carry a correlation id");
            Assert.AreEqual(JsonValueKind.Object, envelope.Payload.ValueKind);
            Assert.AreEqual(7, envelope.Payload.GetProperty("id").GetInt32());
            Assert.AreEqual("hello", envelope.Payload.GetProperty("name").GetString());
        }

        [Test]
        public void PublishEvent_Success_SendsEncodedEventEnvelope()
        {
            var h = new TestHarness();
            var result = h.Bus.PublishEvent("topic/e", new SamplePayload(9, "evt"));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, h.Outbound.Sent.Count);

            var envelope = h.DecodeLastSent();
            Assert.AreEqual(MessageKind.Event, envelope.Kind);
            Assert.AreEqual("topic/e", envelope.Topic);
            Assert.IsNull(envelope.CorrelationId);
            Assert.AreEqual(9, envelope.Payload.GetProperty("id").GetInt32());
        }

        // ---------- RequestAsync correlation + timeout + match ----------

        [Test]
        public async Task RequestAsync_AllocatesCorrelationId_AndSendsRequestEnvelope()
        {
            var h = new TestHarness();
            // Long timeout so the test sees the registered pending without firing.
            var requestOptions = new RequestOptions(TimeSpan.FromSeconds(30));

            var requestTask = h.Bus.RequestAsync<SamplePayload, SamplePayload>(
                "topic/req",
                new SamplePayload(42, "ping"),
                requestOptions);

            // Allow the synchronous part of RequestAsync to enqueue the send.
            await Task.Yield();

            Assert.AreEqual(1, h.Outbound.Sent.Count, "exactly one Request envelope should have been sent");
            Assert.AreEqual(1, h.Correlation.PendingRequestCount,
                "exactly one pending correlation should be registered");

            var envelope = h.DecodeLastSent();
            Assert.AreEqual(MessageKind.Request, envelope.Kind);
            Assert.AreEqual("topic/req", envelope.Topic);
            Assert.IsNotNull(envelope.CorrelationId);
            Assert.IsFalse(string.IsNullOrEmpty(envelope.CorrelationId));
            Assert.AreEqual(42, envelope.Payload.GetProperty("id").GetInt32());

            // Match a fake response back through the correlation registry.
            using var responseDoc = JsonDocument.Parse("{\"id\":99,\"name\":\"pong\"}");
            var matched = h.Correlation.MatchResponse(envelope.CorrelationId!, responseDoc.RootElement);
            Assert.IsTrue(matched, "MatchResponse should find the registered correlation id");

            var result = await requestTask;
            Assert.IsTrue(result.Success);
            Assert.AreEqual(99, result.Value!.Id);
            Assert.AreEqual("pong", result.Value.Name);
        }

        [Test]
        public async Task RequestAsync_NoResponse_ReturnsRequestTimeout()
        {
            var h = new TestHarness();
            var requestOptions = new RequestOptions(TimeSpan.FromMilliseconds(50));

            var result = await h.Bus.RequestAsync<SamplePayload, SamplePayload>(
                "topic/timeout",
                new SamplePayload(1, "x"),
                requestOptions);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.RequestTimeout>(result.Error);
        }

        [Test]
        public async Task RequestAsync_SendThrows_FailsPending_AndReturnsTransportFailure()
        {
            var h = new TestHarness();
            h.Outbound.ThrowOnSend = true;
            var requestOptions = new RequestOptions(TimeSpan.FromSeconds(30));

            var result = await h.Bus.RequestAsync<SamplePayload, SamplePayload>(
                "topic/err",
                new SamplePayload(1, "x"),
                requestOptions);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.TransportFailure>(result.Error);
            Assert.AreEqual(0, h.Correlation.PendingRequestCount,
                "pending correlation should be cleaned up after send failure");
        }

        // ---------- Subscriptions delegate to TopicSubscriptionRegistry ----------

        [Test]
        public void SubscribeState_DelegatesToRegistry_AndDispatchInvokesHandlerWithDeserializedPayload()
        {
            var h = new TestHarness();
            var received = new List<SamplePayload>();

            using var token = h.Bus.SubscribeState<SamplePayload>("topic/state",
                payload => received.Add(payload));

            Assert.AreEqual(1, h.Subscriptions.CountFor("topic/state", MessageKind.State));

            var envelope = BuildEnvelope(MessageKind.State, "topic/state",
                "{\"id\":3,\"name\":\"abc\"}");
            h.Dispatch.Enqueue(envelope);
            h.Dispatch.Flush();

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(3, received[0].Id);
            Assert.AreEqual("abc", received[0].Name);
        }

        [Test]
        public void SubscribeEvent_DelegatesToRegistry_AndDispatchInvokesHandler()
        {
            var h = new TestHarness();
            var received = new List<int>();

            using var token = h.Bus.SubscribeEvent<int>("topic/evt", payload => received.Add(payload));

            Assert.AreEqual(1, h.Subscriptions.CountFor("topic/evt", MessageKind.Event));

            var envelope = BuildEnvelope(MessageKind.Event, "topic/evt", "5");
            h.Dispatch.Enqueue(envelope);
            h.Dispatch.Flush();

            CollectionAssert.AreEqual(new[] { 5 }, received);
        }

        [Test]
        public void Subscribe_DisposeToken_RemovesRegistration()
        {
            var h = new TestHarness();
            var token = h.Bus.SubscribeState<SamplePayload>("topic/s", _ => { });
            Assert.AreEqual(1, h.Subscriptions.CountFor("topic/s", MessageKind.State));

            token.Dispose();
            Assert.AreEqual(0, h.Subscriptions.CountFor("topic/s", MessageKind.State));
        }

        [Test]
        public void Subscribe_NullTopicOrEmpty_Throws()
        {
            var h = new TestHarness();
            Assert.Throws<ArgumentNullException>(() => h.Bus.SubscribeState<int>(null!, _ => { }));
            Assert.Throws<ArgumentException>(() => h.Bus.SubscribeEvent<int>(string.Empty, _ => { }));
        }

        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            var h = new TestHarness();
            Assert.Throws<ArgumentNullException>(
                () => h.Bus.SubscribeState<int>("topic/x", null!));
        }

        // ---------- RegisterRequestHandler ----------

        [Test]
        public async Task RegisterRequestHandler_RegistersUnderRequestKind_AndSendsResponseEnvelope()
        {
            var h = new TestHarness();

            using var token = h.Bus.RegisterRequestHandler<SamplePayload, SamplePayload>(
                "topic/rpc",
                (req, _) => Task.FromResult(new SamplePayload(req.Id * 2, req.Name + "!")));

            Assert.AreEqual(1, h.Subscriptions.CountFor("topic/rpc", MessageKind.Request));

            // Simulate inbound Request envelope routed by the receive path.
            var inbound = BuildEnvelope(
                MessageKind.Request,
                "topic/rpc",
                "{\"id\":4,\"name\":\"q\"}",
                correlationId: "cid-handler-1");

            Assert.IsTrue(h.Subscriptions.TryGetHandlers("topic/rpc", MessageKind.Request, out var handlers));
            Assert.AreEqual(1, handlers.Count);
            handlers[0](inbound);

            // Wait for the async handler to complete and the response to be sent.
            await WaitForAsync(() => h.Outbound.Sent.Count == 1, TimeSpan.FromSeconds(2));

            var responseEnvelope = h.DecodeLastSent();
            Assert.AreEqual(MessageKind.Response, responseEnvelope.Kind);
            Assert.AreEqual("topic/rpc", responseEnvelope.Topic);
            Assert.AreEqual("cid-handler-1", responseEnvelope.CorrelationId);
            Assert.AreEqual(8, responseEnvelope.Payload.GetProperty("id").GetInt32());
            Assert.AreEqual("q!", responseEnvelope.Payload.GetProperty("name").GetString());
        }

        [Test]
        public void RegisterRequestHandler_NullArgs_Throws()
        {
            var h = new TestHarness();
            Assert.Throws<ArgumentNullException>(() => h.Bus.RegisterRequestHandler<int, int>(
                null!, (_, _) => Task.FromResult(0)));
            Assert.Throws<ArgumentException>(() => h.Bus.RegisterRequestHandler<int, int>(
                string.Empty, (_, _) => Task.FromResult(0)));
            Assert.Throws<ArgumentNullException>(() => h.Bus.RegisterRequestHandler<int, int>(
                "topic/x", null!));
        }

        // ---------- Helpers ----------

        private static MessageEnvelope BuildEnvelope(
            MessageKind kind,
            string topic,
            string payloadJson,
            string? correlationId = null)
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: kind,
                Topic: topic,
                CorrelationId: correlationId,
                TimestampUnixMs: 0L,
                Payload: doc.RootElement.Clone());
        }

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return;
                await Task.Delay(10);
            }
            Assert.Fail("Condition was not satisfied within " + timeout);
        }
    }
}
