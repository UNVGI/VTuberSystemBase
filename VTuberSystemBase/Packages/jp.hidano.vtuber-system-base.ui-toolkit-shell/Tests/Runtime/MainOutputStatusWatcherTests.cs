#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.FailsafeAndConnection;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 9.2: <see cref="MainOutputStatusWatcher"/> contract tests. Pin the subscription
    /// against the <c>output/display/fallback</c> topic, the round-trip translation of the
    /// inbound state into <see cref="NotificationBarController.ShowDisplayFallback"/> and
    /// <see cref="NotificationBarController.ClearDisplayFallback"/>, and the diagnostic log
    /// emission for each transition (Requirements 9.6, 11.6).
    /// </summary>
    [TestFixture]
    public sealed class MainOutputStatusWatcherTests
    {
        private const string FallbackItemClass = "vsb-notification-bar__item--display-fallback";
        private const string Topic = "output/display/fallback";

        private FakeIpcClient _bus = null!;
        private DiagnosticsLogger _diagnosticsLogger = null!;
        private RecordingDiagnosticsLogger _recordingLogger = null!;
        private UiSubscriptionClient _subscriptionClient = null!;
        private FakeConnectionStatus _connection = null!;
        private TabPanelRegistry _registry = null!;
        private VisualElement _host = null!;
        private NotificationBarController _notificationBar = null!;

        [SetUp]
        public void SetUp()
        {
            _bus = new FakeIpcClient();
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.Connected);
            _diagnosticsLogger = new DiagnosticsLogger();
            _recordingLogger = new RecordingDiagnosticsLogger();
            _subscriptionClient = new UiSubscriptionClient(_bus, _recordingLogger);

            _connection = new FakeConnectionStatus();
            _registry = new TabPanelRegistry(_recordingLogger);
            _host = new VisualElement { name = "vsb-notification-bar" };
            _host.AddToClassList("vsb-notification-bar");
            _notificationBar = new NotificationBarController(_host, _connection, _registry, _recordingLogger);
        }

        [TearDown]
        public void TearDown()
        {
            _notificationBar.Dispose();
        }

        private static int CountFallbackItems(VisualElement host)
        {
            var count = 0;
            host.Query<VisualElement>(className: FallbackItemClass).ForEach(_ => count++);
            return count;
        }

        // ---- construction ---------------------------------------------------

        [Test]
        [Description("コンストラクタは null subscription / notificationBar / logger を拒否する")]
        public void Constructor_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(
                () => new MainOutputStatusWatcher(null!, _notificationBar, _recordingLogger));
            Assert.Throws<ArgumentNullException>(
                () => new MainOutputStatusWatcher(_subscriptionClient, null!, _recordingLogger));
            Assert.Throws<ArgumentNullException>(
                () => new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, null!));
        }

        [Test]
        [Description("生成直後はフォールバック警告が 0 件、購読は 1 本だけアクティブ（output/display/fallback トピック）")]
        public void Constructor_SubscribesToFallbackTopic_NoInitialWarning()
        {
            using var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);

            Assert.That(CountFallbackItems(_host), Is.EqualTo(0));
            Assert.That(watcher.IsInFallback, Is.False);
            Assert.That(MainOutputStatusWatcher.Topic, Is.EqualTo(Topic));
        }

        // ---- state arrival --------------------------------------------------

        [Test]
        [Description("Display 1 フォールバック state 受信（IsFallback=true）で通知バーに警告が 1 件追加される（Requirement 9.6）")]
        public void Receive_FallbackTrue_ShowsWarning()
        {
            using var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true, Reason = "no displays" });

            Assert.That(CountFallbackItems(_host), Is.EqualTo(1),
                "fallback state must surface a display-fallback notification");
            Assert.That(watcher.IsInFallback, Is.True);
        }

        [Test]
        [Description("フォールバック解除 state 受信（IsFallback=false）で警告が消える（Requirement 9.6）")]
        public void Receive_FallbackFalse_AfterTrue_ClearsWarning()
        {
            using var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);
            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true });
            Assume.That(CountFallbackItems(_host), Is.EqualTo(1));

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = false });

            Assert.That(CountFallbackItems(_host), Is.EqualTo(0),
                "resolved state must clear the display-fallback notification");
            Assert.That(watcher.IsInFallback, Is.False);
        }

        [Test]
        [Description("フォールバック発生 → 解除 → 再発生 → 解除の往復で UI 状態が同期する（フェイク IPC 経由）")]
        public void Receive_FallbackToggle_RoundTrip_TracksState()
        {
            using var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true });
            Assert.That(CountFallbackItems(_host), Is.EqualTo(1), "first fallback should show warning");
            Assert.That(watcher.IsInFallback, Is.True);

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = false });
            Assert.That(CountFallbackItems(_host), Is.EqualTo(0), "first resolution should clear warning");
            Assert.That(watcher.IsInFallback, Is.False);

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true });
            Assert.That(CountFallbackItems(_host), Is.EqualTo(1), "second fallback should re-show warning");
            Assert.That(watcher.IsInFallback, Is.True);

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = false });
            Assert.That(CountFallbackItems(_host), Is.EqualTo(0), "second resolution should clear warning again");
            Assert.That(watcher.IsInFallback, Is.False);
        }

        [Test]
        [Description("連続したフォールバック受信は同 key で差し替えられ、複数件にスタックしない")]
        public void Receive_FallbackTrue_Twice_DoesNotDuplicate()
        {
            using var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true, Reason = "first" });
            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true, Reason = "second" });

            Assert.That(CountFallbackItems(_host), Is.EqualTo(1),
                "duplicate fallback states should replace, not stack");
        }

        // ---- topic isolation ------------------------------------------------

        [Test]
        [Description("無関係なトピック（output/display/fallback 以外）の state 注入では警告が変化しない")]
        public void Receive_OtherTopic_DoesNotAffectFallback()
        {
            using var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);

            _bus.InjectState("output/something/else", new MainOutputStatusPayload { IsFallback = true });

            Assert.That(CountFallbackItems(_host), Is.EqualTo(0));
            Assert.That(watcher.IsInFallback, Is.False);
        }

        // ---- logging --------------------------------------------------------

        [Test]
        [Description("フォールバック発生・解除を診断ログに記録する（Requirement 11.6）")]
        public void Receive_FallbackTransitions_AreLogged()
        {
            using var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true, Reason = "no displays" });
            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = false });

            var watcherEntries = _recordingLogger.Entries
                .Where(e => e.Category == LogCategory.Connection)
                .ToArray();
            Assert.That(watcherEntries.Length, Is.GreaterThanOrEqualTo(2),
                "watcher must log at least two Connection-category entries (fallback + resolved)");
        }

        // ---- dispose --------------------------------------------------------

        [Test]
        [Description("Dispose 後は購読解除され、後続の state 注入で UI が変化しない")]
        public void Dispose_UnsubscribesFromTopic()
        {
            var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);
            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true });
            Assume.That(CountFallbackItems(_host), Is.EqualTo(1));

            watcher.Dispose();
            // Bring the UI back to a known no-warning state through the public API
            // before re-injecting; otherwise the previous warning is still rendered
            // independently of whether the watcher reacted.
            _notificationBar.ClearDisplayFallback(MainOutputStatusWatcher.FallbackKey);
            Assume.That(CountFallbackItems(_host), Is.EqualTo(0));

            _bus.InjectState(Topic, new MainOutputStatusPayload { IsFallback = true });

            Assert.That(CountFallbackItems(_host), Is.EqualTo(0),
                "post-Dispose state injections must not surface a warning");
        }

        [Test]
        [Description("Dispose は冪等（複数回呼び出しても例外を投げない）")]
        public void Dispose_Idempotent()
        {
            var watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _recordingLogger);
            watcher.Dispose();
            Assert.DoesNotThrow(() => watcher.Dispose());
        }

        // ---- in-test fakes --------------------------------------------------

        private sealed class FakeConnectionStatus : IConnectionStatus
        {
            public bool IsConnected => CurrentStatus == ConnectionStatusCode.Connected;
            public ConnectionStatusCode CurrentStatus { get; private set; } = ConnectionStatusCode.Initializing;

            public event Action<ConnectionStatusEvent>? OnStatusChanged;

            public void RaiseStatus(ConnectionStatusCode from, ConnectionStatusCode to, string? detail = null)
            {
                CurrentStatus = to;
                OnStatusChanged?.Invoke(new ConnectionStatusEvent(from, to, DateTimeOffset.UtcNow, detail));
            }
        }
    }
}
