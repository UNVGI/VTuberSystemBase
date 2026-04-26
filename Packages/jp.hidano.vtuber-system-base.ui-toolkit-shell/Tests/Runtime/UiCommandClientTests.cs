#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;
using IpcMessageKind = VTuberSystemBase.CoreIpc.Abstractions.MessageKind;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 12.3: representative consolidation of the
    /// <see cref="UiCommandClient"/> contracts most likely to break under
    /// future refactors — the three send paths (<c>PublishState</c> /
    /// <c>PublishEvent</c> / <c>RequestAsync</c>) routing onto the matching
    /// <see cref="ICoreIpcBus"/> primitive, and the propagation of the three
    /// canonical error codes (<c>NotConnected</c> / <c>TopicInvalid</c> /
    /// <c>Timeout</c>) without UI-side exceptions. Exhaustive contract tests
    /// live in <see cref="UiCommandClientContractTests"/> (task 4.3); this
    /// fixture pins the minimum signal we need green in CI to guard
    /// Requirements 5.2, 5.3, 5.4, 5.5, 5.9, 9.4, and 10.5.
    /// </summary>
    [TestFixture]
    public sealed class UiCommandClientTests
    {
        private FakeIpcClient _bus = null!;
        private ConnectionStatus _status = null!;
        private RecordingDiagnosticsLogger _logger = null!;
        private UiCommandClient _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _bus = new FakeIpcClient();
            _status = new ConnectionStatus(_bus);
            _logger = new RecordingDiagnosticsLogger();
            _sut = new UiCommandClient(_bus, _status, _logger);
            MainThreadAffinity.Capture();
        }

        [TearDown]
        public void TearDown()
        {
            _status.Dispose();
            MainThreadAffinity.Reset();
        }

        // ---- 3 系統呼び分け（routing differentiation, Req 5.2/5.3/5.4/5.5）---

        [Test]
        [Description("PublishState は ICoreIpcBus.PublishState 経路のみへ委譲し、Event/Request バケットを汚染しない (Req 5.3)")]
        public void PublishState_RoutesToBusPublishState_AndNotToOtherChannels()
        {
            Connect();

            SendResult result = _sut.PublishState("ui/character/state", new { active = true });

            Assert.That(result.Success, Is.True, "Connected PublishState must succeed.");
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(1),
                "Exactly one message must be forwarded to the bus.");
            var sent = _bus.SentMessages[0];
            Assert.That(sent.Kind, Is.EqualTo(IpcMessageKind.State),
                "PublishState must select the State channel of ICoreIpcBus.");
            Assert.That(sent.Topic, Is.EqualTo("ui/character/state"));
            Assert.That(sent.CorrelationId, Is.Null,
                "Publish-side sends do not carry correlation IDs (only Request does).");
            Assert.That(_bus.PendingRequestCorrelationIds, Is.Empty,
                "PublishState must never produce a pending request entry.");
        }

        [Test]
        [Description("PublishEvent は ICoreIpcBus.PublishEvent 経路のみへ委譲し、State/Request バケットを汚染しない (Req 5.4)")]
        public void PublishEvent_RoutesToBusPublishEvent_AndNotToOtherChannels()
        {
            Connect();

            SendResult result = _sut.PublishEvent("ui/character/event", new { kind = "click" });

            Assert.That(result.Success, Is.True, "Connected PublishEvent must succeed.");
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(1));
            var sent = _bus.SentMessages[0];
            Assert.That(sent.Kind, Is.EqualTo(IpcMessageKind.Event),
                "PublishEvent must select the Event channel of ICoreIpcBus.");
            Assert.That(sent.Topic, Is.EqualTo("ui/character/event"));
            Assert.That(sent.CorrelationId, Is.Null,
                "Publish-side sends do not carry correlation IDs (only Request does).");
            Assert.That(_bus.PendingRequestCorrelationIds, Is.Empty,
                "PublishEvent must never produce a pending request entry.");
        }

        [Test]
        [Description("RequestAsync は ICoreIpcBus.RequestAsync を呼び、相関 ID 付きで pending request を 1 件作る (Req 5.5)")]
        public async Task RequestAsync_RoutesToBusRequestAsync_AndAssignsCorrelationId()
        {
            Connect();

            Task<RequestResult<ResponsePayload>> pending = _sut.RequestAsync<RequestPayload, ResponsePayload>(
                "ui/character/request",
                new RequestPayload { Echo = "hi" });

            Assert.That(_bus.PendingRequestCorrelationIds.Count, Is.EqualTo(1),
                "RequestAsync must produce exactly one pending request on the bus.");
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(1));
            var sent = _bus.SentMessages[0];
            Assert.That(sent.Kind, Is.EqualTo(IpcMessageKind.Request),
                "RequestAsync must select the Request channel of ICoreIpcBus.");
            Assert.That(sent.Topic, Is.EqualTo("ui/character/request"));
            Assert.That(sent.CorrelationId, Is.Not.Null.And.Not.Empty,
                "Request-side sends must carry a non-empty correlation ID.");

            _bus.RespondToLastRequest(new ResponsePayload { Echo = "hi" });
            RequestResult<ResponsePayload> result = await pending;

            Assert.That(result.Success, Is.True);
            Assert.That(result.Response, Is.Not.Null);
            Assert.That(result.Response!.Echo, Is.EqualTo("hi"));
        }

        // ---- SendError 伝搬: NotConnected (Req 5.9, 9.4) ----------------------

        [Test]
        [Description("接続未確立の PublishState/PublishEvent は SendErrorCode.NotConnected を即時返却し、bus へ送信しない (Req 5.9, 9.4)")]
        public void PublishStateAndEvent_WhenNotConnected_ReturnNotConnected_AndDoNotForward()
        {
            // bus stays in Disconnected (the FakeIpcClient default).

            SendResult stateResult = default;
            SendResult eventResult = default;
            Assert.DoesNotThrow(() => stateResult = _sut.PublishState("ui/x/state", new { v = 1 }),
                "PublishState must not throw when the bus is disconnected.");
            Assert.DoesNotThrow(() => eventResult = _sut.PublishEvent("ui/x/event", new { v = 1 }),
                "PublishEvent must not throw when the bus is disconnected.");

            AssertSendError(stateResult, SendErrorCode.NotConnected,
                "PublishState must short-circuit to NotConnected before touching the bus.");
            AssertSendError(eventResult, SendErrorCode.NotConnected,
                "PublishEvent must short-circuit to NotConnected before touching the bus.");
            Assert.That(_bus.SentMessages, Is.Empty,
                "NotConnected short-circuit must happen before any bus.PublishState/Event call.");
        }

        [Test]
        [Description("接続未確立の RequestAsync は RequestErrorCode.NotConnected を返し、pending request を生成しない (Req 5.9, 9.4)")]
        public async Task RequestAsync_WhenNotConnected_ReturnsNotConnected_NoPendingRequest()
        {
            // bus stays in Disconnected (the FakeIpcClient default).

            RequestResult<ResponsePayload> result = await _sut.RequestAsync<RequestPayload, ResponsePayload>(
                "ui/x/request",
                new RequestPayload { Echo = "hi" });

            AssertRequestError(result, RequestErrorCode.NotConnected,
                "RequestAsync must short-circuit to NotConnected before touching the bus.");
            Assert.That(_bus.PendingRequestCorrelationIds, Is.Empty,
                "NotConnected short-circuit must happen before any pending request is registered.");
            Assert.That(_bus.SentMessages, Is.Empty,
                "NotConnected short-circuit must happen before any bus.RequestAsync call.");
        }

        // ---- SendError 伝搬: TopicInvalid (Req 5.9) ---------------------------

        [Test]
        [Description("空 topic は PublishState/PublishEvent が SendErrorCode.TopicInvalid を即時返却し、bus へ送信しない (Req 5.9)")]
        public void PublishStateAndEvent_WithEmptyTopic_ReturnTopicInvalid_AndDoNotForward()
        {
            Connect();

            SendResult stateResult = default;
            SendResult eventResult = default;
            Assert.DoesNotThrow(() => stateResult = _sut.PublishState(string.Empty, new { v = 1 }));
            Assert.DoesNotThrow(() => eventResult = _sut.PublishEvent(string.Empty, new { v = 1 }));

            AssertSendError(stateResult, SendErrorCode.TopicInvalid,
                "Empty topic must be rejected at the boundary as TopicInvalid.");
            AssertSendError(eventResult, SendErrorCode.TopicInvalid,
                "Empty topic must be rejected at the boundary as TopicInvalid.");
            Assert.That(_bus.SentMessages, Is.Empty,
                "TopicInvalid short-circuit must happen before any bus call.");
        }

        [Test]
        [Description("許可外文字を含む topic は SendErrorCode.TopicInvalid (PublishState/PublishEvent) と RequestErrorCode.TopicInvalid (RequestAsync) を返す (design.md §UiCommandClient Validation, Req 5.9)")]
        public async Task AllThreeChannels_WithInvalidTopicCharacters_ReturnTopicInvalid()
        {
            Connect();

            const string invalidTopic = "ui/テスト/state"; // contains non-ASCII characters

            SendResult stateResult = _sut.PublishState(invalidTopic, new { v = 1 });
            SendResult eventResult = _sut.PublishEvent(invalidTopic, new { v = 1 });
            RequestResult<ResponsePayload> requestResult = await _sut.RequestAsync<RequestPayload, ResponsePayload>(
                invalidTopic, new RequestPayload { Echo = "hi" });

            AssertSendError(stateResult, SendErrorCode.TopicInvalid,
                "Non-ASCII topic must be rejected by PublishState as TopicInvalid.");
            AssertSendError(eventResult, SendErrorCode.TopicInvalid,
                "Non-ASCII topic must be rejected by PublishEvent as TopicInvalid.");
            AssertRequestError(requestResult, RequestErrorCode.TopicInvalid,
                "Non-ASCII topic must be rejected by RequestAsync as TopicInvalid.");
            Assert.That(_bus.SentMessages, Is.Empty,
                "All three TopicInvalid short-circuits must happen before any bus call.");
            Assert.That(_bus.PendingRequestCorrelationIds, Is.Empty,
                "RequestAsync must reject before producing a pending entry.");
        }

        // ---- RequestError 伝搬: Timeout (Req 5.9; core-ipc D-8 inheritance) ---

        [Test]
        [Description("RequestAsync のタイムアウト超過は RequestErrorCode.Timeout を返し、例外を外に投げない (Req 5.9, core-ipc D-8 継承)")]
        public void RequestAsync_OnTimeout_ReturnsTimeoutError_NoException()
        {
            Connect();

            RequestResult<ResponsePayload> result = default;
            Assert.DoesNotThrowAsync(async () =>
                {
                    result = await _sut.RequestAsync<RequestPayload, ResponsePayload>(
                        "ui/character/request",
                        new RequestPayload { Echo = "hi" },
                        timeout: TimeSpan.FromMilliseconds(50));
                },
                "Timeout path must never escape as an exception (UI must not crash).");

            AssertRequestError(result, RequestErrorCode.Timeout,
                "Bus-side RequestTimeout must be mapped onto RequestErrorCode.Timeout.");
        }

        // ---- Helpers ----

        private void Connect()
        {
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.Connected);
        }

        private static void AssertSendError(SendResult result, SendErrorCode expected, string because)
        {
            Assert.That(result.Success, Is.False, because);
            Assert.That(result.Error.HasValue, Is.True,
                "Failed SendResult must carry a SendError payload.");
            Assert.That(result.Error!.Value.Code, Is.EqualTo(expected), because);
        }

        private static void AssertRequestError<TResponse>(
            RequestResult<TResponse> result,
            RequestErrorCode expected,
            string because)
        {
            Assert.That(result.Success, Is.False, because);
            Assert.That(result.Error.HasValue, Is.True,
                "Failed RequestResult must carry a RequestError payload.");
            Assert.That(result.Error!.Value.Code, Is.EqualTo(expected), because);
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
