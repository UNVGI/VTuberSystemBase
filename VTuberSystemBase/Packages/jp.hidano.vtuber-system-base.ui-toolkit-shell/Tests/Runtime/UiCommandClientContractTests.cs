#nullable enable
using System;
using System.Threading;
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
    /// Task 4.3 (Red): <c>IUiCommandClient</c> 契約テスト。<c>PublishState</c> /
    /// <c>PublishEvent</c> の即時 <c>SendResult</c> 返却、<c>RequestAsync</c> の非同期
    /// <c>RequestResult&lt;TResponse&gt;</c> 返却、接続未確立時 <c>SendError.NotConnected</c>
    /// 即時返却、topic バリデーション違反時 <c>TopicInvalid</c>、タイムアウト時
    /// <c>RequestError.Timeout</c>、失敗時に例外を外に投げない（UI クラッシュしない）契約を
    /// TDD で固定する。 4.4 で <c>IUiCommandClient</c> / <c>UiCommandClient</c> /
    /// <c>SendResult</c> / <c>SendError</c> / <c>SendErrorCode</c> /
    /// <c>RequestResult&lt;TResponse&gt;</c> / <c>RequestError</c> /
    /// <c>RequestErrorCode</c> を実装するまでは「型未定義」（CS0246）で失敗する。
    /// design.md §Commands §UiCommandClient 参照（Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.9, 9.4）。
    /// </summary>
    [TestFixture]
    public sealed class UiCommandClientContractTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadAffinity.Capture();
        }

        [TearDown]
        public void TearDown()
        {
            MainThreadAffinity.Reset();
        }

        private static (FakeIpcClient bus, IConnectionStatus status, IDiagnosticsLogger logger) CreateConnectedDeps()
        {
            var bus = new FakeIpcClient();
            bus.SetConnectionState(ConnectionState.Connecting);
            bus.SetConnectionState(ConnectionState.Connected);
            var status = new ConnectionStatus(bus);
            var logger = new DiagnosticsLogger();
            return (bus, status, logger);
        }

        private static (FakeIpcClient bus, IConnectionStatus status, IDiagnosticsLogger logger) CreateDisconnectedDeps()
        {
            var bus = new FakeIpcClient();
            // bus stays in Disconnected state
            var status = new ConnectionStatus(bus);
            var logger = new DiagnosticsLogger();
            return (bus, status, logger);
        }

        [Test]
        [Description("接続確立済みのとき PublishState は SendResult.Success=true を即時返却し、core-ipc の PublishState へ委譲する（Req 5.2, 5.3）")]
        public void PublishState_WhenConnected_ReturnsSuccess_AndForwardsToBus()
        {
            var (bus, status, logger) = CreateConnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            SendResult result = client.PublishState("ui/test/state", new { foo = 1 });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(bus.SentMessages.Count, Is.EqualTo(1));
            Assert.That(bus.SentMessages[0].Kind, Is.EqualTo(IpcMessageKind.State));
            Assert.That(bus.SentMessages[0].Topic, Is.EqualTo("ui/test/state"));
        }

        [Test]
        [Description("接続確立済みのとき PublishEvent は SendResult.Success=true を即時返却し、core-ipc の PublishEvent へ委譲する（Req 5.2, 5.4）")]
        public void PublishEvent_WhenConnected_ReturnsSuccess_AndForwardsToBus()
        {
            var (bus, status, logger) = CreateConnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            SendResult result = client.PublishEvent("ui/test/event", new { foo = 1 });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(bus.SentMessages.Count, Is.EqualTo(1));
            Assert.That(bus.SentMessages[0].Kind, Is.EqualTo(IpcMessageKind.Event));
            Assert.That(bus.SentMessages[0].Topic, Is.EqualTo("ui/test/event"));
        }

        [Test]
        [Description("接続未確立のとき PublishState は SendError.NotConnected を即時返却し、例外を外に投げない（Req 5.9, 9.4）")]
        public void PublishState_WhenNotConnected_ReturnsNotConnected_NoException()
        {
            var (bus, status, logger) = CreateDisconnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            SendResult result = default;
            Assert.DoesNotThrow(() => result = client.PublishState("ui/test/state", new { foo = 1 }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));
        }

        [Test]
        [Description("接続未確立のとき PublishEvent は SendError.NotConnected を即時返却し、例外を外に投げない（Req 5.9, 9.4）")]
        public void PublishEvent_WhenNotConnected_ReturnsNotConnected_NoException()
        {
            var (bus, status, logger) = CreateDisconnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            SendResult result = default;
            Assert.DoesNotThrow(() => result = client.PublishEvent("ui/test/event", new { foo = 1 }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));
        }

        [Test]
        [Description("空文字 / null topic は SendErrorCode.TopicInvalid を即時返却し、例外を外に投げない（design.md §UiCommandClient Validation; Req 5.9）")]
        public void PublishState_WithEmptyTopic_ReturnsTopicInvalid_NoException()
        {
            var (bus, status, logger) = CreateConnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            SendResult result = default;
            Assert.DoesNotThrow(() => result = client.PublishState(string.Empty, new { foo = 1 }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(SendErrorCode.TopicInvalid));
        }

        [Test]
        [Description("topic に許可されない文字（ASCII 英数 + / + - + _ 以外）が含まれる場合は SendErrorCode.TopicInvalid を即時返却（design.md §UiCommandClient Validation）")]
        public void PublishState_WithInvalidTopicCharacters_ReturnsTopicInvalid()
        {
            var (bus, status, logger) = CreateConnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            SendResult result = client.PublishState("ui/テスト/state", new { foo = 1 });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(SendErrorCode.TopicInvalid));
        }

        [Test]
        [Description("接続確立済みで RequestAsync が応答を受信したとき RequestResult<TResponse>.Success=true と Response が返る（Req 5.5）")]
        public async Task RequestAsync_WhenConnected_ReturnsSuccessfulResponse()
        {
            var (bus, status, logger) = CreateConnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            var task = client.RequestAsync<RequestPayload, ResponsePayload>(
                "ui/test/request",
                new RequestPayload { Echo = "hi" });

            // 非同期: pending request が登録されたあと、テスト側が応答を流し込む
            Assert.That(bus.PendingRequestCorrelationIds.Count, Is.EqualTo(1));
            bus.RespondToLastRequest(new ResponsePayload { Echo = "hi" });

            RequestResult<ResponsePayload> result = await task;

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.Response, Is.Not.Null);
            Assert.That(result.Response!.Echo, Is.EqualTo("hi"));
        }

        [Test]
        [Description("接続未確立のとき RequestAsync は RequestErrorCode.NotConnected を返し、例外を外に投げない（Req 5.9, 9.4）")]
        public async Task RequestAsync_WhenNotConnected_ReturnsNotConnectedError_NoException()
        {
            var (bus, status, logger) = CreateDisconnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            RequestResult<ResponsePayload> result = await client.RequestAsync<RequestPayload, ResponsePayload>(
                "ui/test/request",
                new RequestPayload { Echo = "hi" });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(RequestErrorCode.NotConnected));
        }

        [Test]
        [Description("RequestAsync のタイムアウト超過は RequestErrorCode.Timeout を返し、例外を外に投げない（core-ipc D-8 の継承; Req 5.9）")]
        public async Task RequestAsync_OnTimeout_ReturnsTimeoutError_NoException()
        {
            var (bus, status, logger) = CreateConnectedDeps();
            IUiCommandClient client = new UiCommandClient(bus, status, logger);

            RequestResult<ResponsePayload> result = await client.RequestAsync<RequestPayload, ResponsePayload>(
                "ui/test/request",
                new RequestPayload { Echo = "hi" },
                timeout: TimeSpan.FromMilliseconds(50));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(RequestErrorCode.Timeout));
        }

        [Test]
        [Description("ICoreIpcBus を null で渡した場合は ArgumentNullException（boundary input validation）")]
        public void Constructor_NullBus_Throws()
        {
            var fake = new FakeIpcClient();
            var status = new ConnectionStatus(fake);
            var logger = new DiagnosticsLogger();

            Assert.Throws<ArgumentNullException>(() => new UiCommandClient(null!, status, logger));
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
