#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;
using IpcResult = VTuberSystemBase.CoreIpc.Abstractions.IpcResult;
using IpcMessageKind = VTuberSystemBase.CoreIpc.Abstractions.MessageKind;
using UiMessageKind = VTuberSystemBase.UiToolkitShell.Commands.MessageKind;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 12.5 (Integration): IPC モック注入による送信↔受信 round-trip 結合テスト。
    /// <para>
    /// <see cref="FakeIpcClient"/> を <see cref="UiCommandClient"/> /
    /// <see cref="UiSubscriptionClient"/> Facade に差し込み、UI 側 Facade だけで
    /// <c>PublishState</c> → <c>Subscribe</c> コールバック受信のパスが完結することを検証する
    /// （Requirements 10.3, 10.5, 10.6）。<see cref="FakeIpcClient.SendInterceptor"/> を
    /// 自己ループとして構成し、<c>core-ipc-foundation</c>（spec #1 Requirement 8）の
    /// <c>InMemoryLoopbackTransport</c> が果たす「自プロセス内自己ループ」の役割を
    /// テスト時のみ模倣する。手順詳細は
    /// <c>Tests/Runtime/IpcRoundTripIntegrationTests.md</c> を参照。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class IpcRoundTripIntegrationTests
    {
        private const string StateTopic = "ui/round-trip/state";
        private const string EventTopic = "ui/round-trip/event";
        private const string RequestTopic = "ui/round-trip/request";
        private const string OtherTopic = "ui/round-trip/other";

        private FakeIpcClient _bus = null!;
        private ConnectionStatus _connectionStatus = null!;
        private RecordingDiagnosticsLogger _logger = null!;
        private UiCommandClient _commandClient = null!;
        private UiSubscriptionClient _subscriptionClient = null!;

        [SetUp]
        public void SetUp()
        {
            MainThreadAffinity.Capture();

            _bus = new FakeIpcClient();
            ConfigureSelfLoop(_bus);
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.Connected);

            _connectionStatus = new ConnectionStatus(_bus);
            _logger = new RecordingDiagnosticsLogger();
            _commandClient = new UiCommandClient(_bus, _connectionStatus, _logger);
            _subscriptionClient = new UiSubscriptionClient(_bus, _logger);
        }

        [TearDown]
        public void TearDown()
        {
            _connectionStatus?.Dispose();
            MainThreadAffinity.Reset();
        }

        /// <summary>
        /// Wires the fake bus so that any successful outbound <c>PublishState</c> /
        /// <c>PublishEvent</c> is immediately echoed back to the matching subscribers on the
        /// same thread, mirroring <c>core-ipc-foundation</c>'s in-process loopback transport
        /// (spec #1 Requirement 8). Disconnected sends are short-circuited by
        /// <see cref="UiCommandClient"/> before reaching the bus, so this loop only runs while
        /// the bus reports <see cref="ConnectionState.Connected"/>.
        /// </summary>
        private static void ConfigureSelfLoop(FakeIpcClient bus)
        {
            bus.SendInterceptor = record =>
            {
                if (!bus.IsConnected) return IpcResult.Fail(new CoreIpcError.NotConnected());

                switch (record.Kind)
                {
                    case IpcMessageKind.State:
                        bus.InjectState(record.Topic, record.Payload);
                        break;
                    case IpcMessageKind.Event:
                        bus.InjectEvent(record.Topic, record.Payload);
                        break;
                    // Request/Response correlation is exercised separately via
                    // RegisterRequestHandler in the corresponding test below.
                }
                return IpcResult.Ok();
            };
        }

        // ---- Round-trip via UI Facades -------------------------------------

        [Test]
        [Description("PublishState 経由で送出した payload が UiSubscriptionClient.Subscribe コールバックに envelope 付きで届く（Req 10.3, 10.5, 10.6）")]
        public void PublishState_RoundTripsThroughSubscriptionFacade()
        {
            MessageEnvelope<PosePayload>? captured = null;
            using var token = _subscriptionClient.Subscribe<PosePayload>(
                StateTopic,
                UiMessageKind.State,
                env => captured = env);

            var sendResult = _commandClient.PublishState(StateTopic, new PosePayload
            {
                PoseId = "wave",
                Intensity = 0.75f,
            });

            Assert.That(sendResult.Success, Is.True, "PublishState must succeed once the bus reports Connected");
            Assert.That(captured.HasValue, Is.True, "subscription callback must be invoked by the self-loop");
            Assert.That(captured!.Value.Topic, Is.EqualTo(StateTopic));
            Assert.That(captured.Value.Kind, Is.EqualTo(UiMessageKind.State));
            Assert.That(captured.Value.Payload, Is.Not.Null);
            Assert.That(captured.Value.Payload.PoseId, Is.EqualTo("wave"));
            Assert.That(captured.Value.Payload.Intensity, Is.EqualTo(0.75f));
        }

        [Test]
        [Description("PublishEvent 経由のメッセージは Subscribe(MessageKind.Event) で受信され、State 購読には届かない（kind 別ルーティング維持; Req 10.5）")]
        public void PublishEvent_RoundTripsToEventSubscribersOnly()
        {
            int stateCount = 0;
            int eventCount = 0;

            using var stateToken = _subscriptionClient.Subscribe<PosePayload>(
                EventTopic,
                UiMessageKind.State,
                _ => stateCount++);
            using var eventToken = _subscriptionClient.Subscribe<PosePayload>(
                EventTopic,
                UiMessageKind.Event,
                _ => eventCount++);

            var sendResult = _commandClient.PublishEvent(EventTopic, new PosePayload
            {
                PoseId = "bow",
                Intensity = 1.0f,
            });

            Assert.That(sendResult.Success, Is.True);
            Assert.That(eventCount, Is.EqualTo(1), "event subscriber must receive the published event payload");
            Assert.That(stateCount, Is.EqualTo(0), "state subscriber must not receive event-kind messages");
        }

        [Test]
        [Description("複数 Publish の連続送信が、同一購読者にすべて配信される（送信順序は維持される; Req 10.5）")]
        public void PublishState_MultiplePublishes_AreAllDeliveredInOrder()
        {
            var received = new System.Collections.Generic.List<string>();
            using var token = _subscriptionClient.Subscribe<PosePayload>(
                StateTopic,
                UiMessageKind.State,
                env => received.Add(env.Payload.PoseId));

            for (var i = 0; i < 5; i++)
            {
                var ok = _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "p" + i });
                Assert.That(ok.Success, Is.True, $"iteration {i} should succeed");
            }

            Assert.That(received, Is.EqualTo(new[] { "p0", "p1", "p2", "p3", "p4" }));
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(5));
        }

        [Test]
        [Description("複数購読者が同一 topic を購読しているとき、1 度の Publish ですべての購読者に配信される（Req 10.5）")]
        public void PublishState_FanOutToMultipleSubscribers()
        {
            int firstCount = 0;
            int secondCount = 0;

            using var first = _subscriptionClient.Subscribe<PosePayload>(StateTopic, UiMessageKind.State, _ => firstCount++);
            using var second = _subscriptionClient.Subscribe<PosePayload>(StateTopic, UiMessageKind.State, _ => secondCount++);

            var ok = _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "fanout" });

            Assert.That(ok.Success, Is.True);
            Assert.That(firstCount, Is.EqualTo(1));
            Assert.That(secondCount, Is.EqualTo(1));
        }

        [Test]
        [Description("異なる topic 間ではメッセージが混線しない（topic ルーティングが Facade 越しに保持される; Req 10.5）")]
        public void PublishState_DifferentTopics_DoNotCrosstalk()
        {
            int topicACount = 0;
            int topicBCount = 0;

            using var a = _subscriptionClient.Subscribe<PosePayload>(StateTopic, UiMessageKind.State, _ => topicACount++);
            using var b = _subscriptionClient.Subscribe<PosePayload>(OtherTopic, UiMessageKind.State, _ => topicBCount++);

            _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "a" });
            _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "a2" });
            _commandClient.PublishState(OtherTopic, new PosePayload { PoseId = "b" });

            Assert.That(topicACount, Is.EqualTo(2));
            Assert.That(topicBCount, Is.EqualTo(1));
        }

        [Test]
        [Description("購読トークン Dispose 後は以降の Publish 由来メッセージが届かない（Req 5.7 round-trip 経由での再確認; Req 10.5）")]
        public void DisposedSubscription_StopsReceivingRoundTrippedMessages()
        {
            int callCount = 0;
            var token = _subscriptionClient.Subscribe<PosePayload>(StateTopic, UiMessageKind.State, _ => callCount++);

            _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "before" });
            Assert.That(callCount, Is.EqualTo(1));

            token.Dispose();

            _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "after" });
            Assert.That(callCount, Is.EqualTo(1), "callback must not fire after the subscription token is disposed");
        }

        // ---- Diagnostics: send/receive log pair ----------------------------

        [Test]
        [Description("round-trip 1 件につき UiCommandClient の SendStarted / SendResult と UiSubscriptionClient の Received が LogCategory.Ipc で記録される（Req 11.4, 11.5）")]
        public void RoundTrip_EmitsSendAndReceiveLogsInIpcCategory()
        {
            using var token = _subscriptionClient.Subscribe<PosePayload>(StateTopic, UiMessageKind.State, _ => { });

            var ok = _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "logged" });
            Assert.That(ok.Success, Is.True);

            var ipcEntries = _logger.Entries.Where(e => e.Category == LogCategory.Ipc).ToArray();
            Assert.That(
                ipcEntries.Any(e => e.Message.StartsWith("SendStarted") && e.Message.Contains(StateTopic)),
                Is.True,
                "expected a SendStarted log line tagged with the publish topic");
            Assert.That(
                ipcEntries.Any(e => e.Message.StartsWith("SendResult") && e.Message.Contains("ok")),
                Is.True,
                "expected a SendResult ok log line");
            Assert.That(
                ipcEntries.Any(e => e.Message.StartsWith("Received") && e.Message.Contains(StateTopic)),
                Is.True,
                "expected a Received log line emitted by UiSubscriptionClient");
        }

        // ---- Negative path: disconnected state ----------------------------

        [Test]
        [Description("接続が切れている場合、UiCommandClient が NotConnected を即時返却し、自己ループは発火しない（Req 9.4 round-trip 経由での再確認）")]
        public void DisconnectedBus_PublishStateShortCircuits_NoRoundTrip()
        {
            int callCount = 0;
            using var token = _subscriptionClient.Subscribe<PosePayload>(StateTopic, UiMessageKind.State, _ => callCount++);

            _bus.SetConnectionState(ConnectionState.Disconnected);

            SendResult result = default;
            Assert.DoesNotThrow(() => result = _commandClient.PublishState(StateTopic, new PosePayload { PoseId = "x" }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));
            Assert.That(callCount, Is.EqualTo(0), "self-loop must not fire while disconnected");
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(0), "send must short-circuit before reaching the bus");
        }

        // ---- Request/Response: handler-based round-trip --------------------

        [Test]
        [Description("RequestAsync は登録済みハンドラ経由で Response が返る完全な round-trip を完遂する（Req 5.5 + 10.3 round-trip 検証）")]
        public async Task RequestAsync_RoundTripsThroughRegisteredHandler()
        {
            using var handlerToken = _bus.RegisterRequestHandler<RequestPayload, ResponsePayload>(
                RequestTopic,
                (req, _) => Task.FromResult(new ResponsePayload { Echo = req.Echo + "!" }));

            RequestResult<ResponsePayload> result = await _commandClient
                .RequestAsync<RequestPayload, ResponsePayload>(RequestTopic, new RequestPayload { Echo = "ping" });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Response, Is.Not.Null);
            Assert.That(result.Response!.Echo, Is.EqualTo("ping!"));
        }

        // ---- Test payload types -------------------------------------------

        private sealed class PosePayload
        {
            public string PoseId { get; set; } = string.Empty;
            public float Intensity { get; set; }
        }

        private sealed class RequestPayload
        {
            public string Echo { get; set; } = string.Empty;
        }

        private sealed class ResponsePayload
        {
            public string Echo { get; set; } = string.Empty;
        }
    }
}
