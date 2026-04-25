#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;
using IUiSubscriptionClient = VTuberSystemBase.UiToolkitShell.Commands.IUiSubscriptionClient;
using UiSubscriptionClient = VTuberSystemBase.UiToolkitShell.Commands.UiSubscriptionClient;
using UiMessageKind = VTuberSystemBase.UiToolkitShell.Commands.MessageKind;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 4.5 (Red): <c>IUiSubscriptionClient</c> 契約テスト。<c>Subscribe(topic, kind, callback)</c>
    /// が <c>ISubscriptionToken</c> を返し、<c>Dispose</c> 後は callback が呼ばれないこと、
    /// callback が Unity メインスレッドで発火すること、callback 内の例外が他購読に波及せず
    /// <see cref="DiagnosticsLogger"/> に記録されることを TDD で固定する。
    /// 4.6 で <c>IUiSubscriptionClient</c> / <c>UiSubscriptionClient</c> /
    /// <c>MessageKind</c> / <c>MessageEnvelope&lt;TPayload&gt;</c> /
    /// <c>ISubscriptionToken</c> を実装するまでは「型未定義」（CS0246）で失敗する。
    /// design.md §Commands §UiSubscriptionClient 参照（Requirements 5.6, 5.7）。
    /// </summary>
    [TestFixture]
    public sealed class UiSubscriptionClientContractTests
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

        private static (FakeIpcClient bus, DiagnosticsLogger logger) CreateConnectedDeps()
        {
            var bus = new FakeIpcClient();
            bus.SetConnectionState(ConnectionState.Connecting);
            bus.SetConnectionState(ConnectionState.Connected);
            var logger = new DiagnosticsLogger();
            return (bus, logger);
        }

        [Test]
        [Description("Subscribe(State) は IsActive=true のトークンを返し、その後注入された state メッセージを Topic / Kind / Payload 付きで callback に届ける（Req 5.6; design.md §UiSubscriptionClient Service Interface）")]
        public void Subscribe_State_ReturnsActiveToken_AndDeliversInjectedMessageWithEnvelope()
        {
            var (bus, logger) = CreateConnectedDeps();
            IUiSubscriptionClient client = new UiSubscriptionClient(bus, logger);

            string? receivedTopic = null;
            UiMessageKind receivedKind = default;
            TestPayload? receivedPayload = null;

            var token = client.Subscribe<TestPayload>(
                "ui/test/state",
                UiMessageKind.State,
                env =>
                {
                    receivedTopic = env.Topic;
                    receivedKind = env.Kind;
                    receivedPayload = env.Payload;
                });

            Assert.That(token, Is.Not.Null);
            Assert.That(token.Topic, Is.EqualTo("ui/test/state"));
            Assert.That(token.IsActive, Is.True);

            bus.InjectState("ui/test/state", new TestPayload { Value = "hello" });

            Assert.That(receivedTopic, Is.EqualTo("ui/test/state"));
            Assert.That(receivedKind, Is.EqualTo(UiMessageKind.State));
            Assert.That(receivedPayload, Is.Not.Null);
            Assert.That(receivedPayload!.Value, Is.EqualTo("hello"));
        }

        [Test]
        [Description("Subscribe(Event) は event 系メッセージのみを受け取り、State 注入では発火しない（design.md §UiSubscriptionClient: kind 別ルーティング）")]
        public void Subscribe_StateAndEvent_AreRoutedSeparatelyByKind()
        {
            var (bus, logger) = CreateConnectedDeps();
            IUiSubscriptionClient client = new UiSubscriptionClient(bus, logger);

            int stateCount = 0;
            int eventCount = 0;

            client.Subscribe<TestPayload>("ui/test/topic", UiMessageKind.State, _ => stateCount++);
            client.Subscribe<TestPayload>("ui/test/topic", UiMessageKind.Event, _ => eventCount++);

            bus.InjectState("ui/test/topic", new TestPayload { Value = "s" });
            bus.InjectEvent("ui/test/topic", new TestPayload { Value = "e" });

            Assert.That(stateCount, Is.EqualTo(1), "state subscriber must fire exactly once for the injected state message");
            Assert.That(eventCount, Is.EqualTo(1), "event subscriber must fire exactly once for the injected event message");
        }

        [Test]
        [Description("ISubscriptionToken.Dispose 後は IsActive=false へ単調遷移し、以降のメッセージ注入で callback は呼ばれない（Req 5.7; design.md §UiSubscriptionClient Invariants）")]
        public void Token_Dispose_StopsCallbackInvocation_AndFlipsIsActiveToFalse()
        {
            var (bus, logger) = CreateConnectedDeps();
            IUiSubscriptionClient client = new UiSubscriptionClient(bus, logger);

            int callCount = 0;
            var token = client.Subscribe<TestPayload>(
                "ui/test/state",
                UiMessageKind.State,
                _ => Interlocked.Increment(ref callCount));

            bus.InjectState("ui/test/state", new TestPayload { Value = "1" });
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(token.IsActive, Is.True);

            token.Dispose();

            Assert.That(token.IsActive, Is.False, "IsActive must transition true -> false on Dispose (no reactivation)");

            bus.InjectState("ui/test/state", new TestPayload { Value = "2" });
            Assert.That(callCount, Is.EqualTo(1), "callback must not be invoked after the token is disposed");
        }

        [Test]
        [Description("callback は Unity メインスレッド上で発火する（Req 5.6; design.md §UiSubscriptionClient: core-ipc D-3 継承の通過パス）")]
        public void Subscribe_CallbackInvokedOnCapturedMainThread()
        {
            var (bus, logger) = CreateConnectedDeps();
            IUiSubscriptionClient client = new UiSubscriptionClient(bus, logger);

            var recorder = new MainThreadAffinity.Recorder();

            client.Subscribe<TestPayload>(
                "ui/test/state",
                UiMessageKind.State,
                _ => recorder.Record());

            bus.InjectState("ui/test/state", new TestPayload { Value = "x" });

            Assert.That(recorder.WasInvoked, Is.True, "callback must have been invoked at least once");
            Assert.That(
                recorder.Matches(MainThreadAffinity.CapturedThreadId),
                Is.True,
                $"expected callback on captured main thread {MainThreadAffinity.CapturedThreadId} but observed {recorder.ObservedThreadId}");
        }

        [Test]
        [Description("ある callback が例外を投げても他の購読者には影響せず、注入呼出し自身も例外を外に投げない（Req 5.7; design.md §UiSubscriptionClient Risks/Validation）")]
        public void Subscribe_CallbackException_DoesNotPropagate_AndOtherSubscribersStillReceive()
        {
            var (bus, logger) = CreateConnectedDeps();
            IUiSubscriptionClient client = new UiSubscriptionClient(bus, logger);

            bool secondReceived = false;

            client.Subscribe<TestPayload>(
                "ui/test/state",
                UiMessageKind.State,
                _ => throw new InvalidOperationException("boom"));
            client.Subscribe<TestPayload>(
                "ui/test/state",
                UiMessageKind.State,
                _ => secondReceived = true);

            // The exception is logged at Error level via DiagnosticsLogger -> Debug.LogError; tell
            // the Unity test runner to expect that single error so the test does not fail spuriously.
            LogAssert.Expect(LogType.Error, new Regex(".*"));

            Assert.DoesNotThrow(
                () => bus.InjectState("ui/test/state", new TestPayload { Value = "x" }),
                "subscriber exceptions must be caught inside the subscription facade");

            Assert.That(
                secondReceived,
                Is.True,
                "the second subscriber must still receive the message after the first one threw");
        }

        [Test]
        [Description("callback 内で送出された例外は DiagnosticsLogger に Error レベルで記録され、購読自体は継続する（Req 5.7; design.md §UiSubscriptionClient Validation）")]
        public void Subscribe_CallbackException_IsLoggedAtErrorLevel_AndSubscriptionRemainsActive()
        {
            var (bus, logger) = CreateConnectedDeps();
            IUiSubscriptionClient client = new UiSubscriptionClient(bus, logger);

            int callCount = 0;
            var token = client.Subscribe<TestPayload>(
                "ui/test/state",
                UiMessageKind.State,
                _ =>
                {
                    Interlocked.Increment(ref callCount);
                    throw new InvalidOperationException("boom");
                });

            // Two injections -> two expected error logs.
            LogAssert.Expect(LogType.Error, new Regex(".*"));
            LogAssert.Expect(LogType.Error, new Regex(".*"));

            bus.InjectState("ui/test/state", new TestPayload { Value = "x" });
            bus.InjectState("ui/test/state", new TestPayload { Value = "y" });

            Assert.That(callCount, Is.EqualTo(2), "subscription must remain active after a callback exception");
            Assert.That(token.IsActive, Is.True, "subscription token must remain active after a callback exception");

            var entries = logger.SnapshotRecentEntries();
            Assert.That(
                entries.Any(e => e.Level == LogLevel.Error),
                Is.True,
                "expected at least one Error-level entry recording the subscriber exception");
        }

        [Test]
        [Description("Subscribe の引数バリデーション: null callback は ArgumentNullException、空 topic は ArgumentException を即座に投げる（design.md §UiSubscriptionClient Preconditions）")]
        public void Subscribe_InvalidArguments_ThrowImmediately()
        {
            var (bus, logger) = CreateConnectedDeps();
            IUiSubscriptionClient client = new UiSubscriptionClient(bus, logger);

            Assert.Throws<ArgumentNullException>(
                () => client.Subscribe<TestPayload>("ui/test/state", UiMessageKind.State, null!),
                "null callback must throw ArgumentNullException at the boundary");

            Assert.Throws<ArgumentException>(
                () => client.Subscribe<TestPayload>(string.Empty, UiMessageKind.State, _ => { }),
                "empty topic must throw ArgumentException at the boundary");
        }

        private sealed class TestPayload
        {
            public string Value { get; set; } = string.Empty;
        }
    }
}
