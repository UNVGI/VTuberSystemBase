#nullable enable
using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 9.1: <see cref="NotificationBarController"/> contract tests. Pin the
    /// four warning categories (Connection / Reconnecting / DisplayFallback /
    /// PreloadFailure), the vertical-stack rendering with a per-item close
    /// button, the dismissal-then-redisplay behaviour driven by repeated state
    /// events (Requirement 9.5, 9.6, 6.6), and the maximum-3 cap that overflows
    /// to the diagnostics panel.
    /// </summary>
    [TestFixture]
    public sealed class NotificationBarControllerTests
    {
        private const string ItemClass = "vsb-notification-bar__item";
        private const string ItemConnectionClass = "vsb-notification-bar__item--connection";
        private const string ItemDisplayFallbackClass = "vsb-notification-bar__item--display-fallback";
        private const string ItemPreloadFailureClass = "vsb-notification-bar__item--preload-failure";
        private const string CloseButtonClass = "vsb-notification-bar__item-close";

        private RecordingDiagnosticsLogger _logger = null!;
        private FakeConnectionStatus _connection = null!;
        private TabPanelRegistry _registry = null!;
        private VisualElement _host = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _connection = new FakeConnectionStatus();
            _registry = new TabPanelRegistry(_logger);
            _host = new VisualElement { name = "vsb-notification-bar" };
            _host.AddToClassList("vsb-notification-bar");
        }

        private NotificationBarController CreateController()
        {
            return new NotificationBarController(_host, _connection, _registry, _logger);
        }

        private static int CountItems(VisualElement host)
        {
            var count = 0;
            host.Query<VisualElement>(className: ItemClass).ForEach(_ => count++);
            return count;
        }

        // ---- construction ----------------------------------------------

        [Test]
        [Description("コンストラクタは null host / connection / registry / logger を拒否する")]
        public void Constructor_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(
                () => new NotificationBarController(null!, _connection, _registry, _logger));
            Assert.Throws<ArgumentNullException>(
                () => new NotificationBarController(_host, null!, _registry, _logger));
            Assert.Throws<ArgumentNullException>(
                () => new NotificationBarController(_host, _connection, null!, _logger));
            Assert.Throws<ArgumentNullException>(
                () => new NotificationBarController(_host, _connection, _registry, null!));
        }

        [Test]
        [Description("初期状態（接続未確立 / プリロード未完了 / フォールバックなし）では通知行を 0 件で開始する")]
        public void Constructor_InitialState_NoItemsRendered()
        {
            using var ctrl = CreateController();

            Assert.That(CountItems(_host), Is.EqualTo(0));
            Assert.That(ctrl.ActiveNotificationCount, Is.EqualTo(0));
        }

        // ---- connection notifications ----------------------------------

        [Test]
        [Description("接続断（Connected → Disconnected）で接続警告が 1 件追加される（Requirement 9.5）")]
        public void Connection_Disconnect_ShowsWarning()
        {
            using var ctrl = CreateController();

            _connection.RaiseStatus(ConnectionStatusCode.Connected, ConnectionStatusCode.Disconnected);

            Assert.That(CountItems(_host), Is.EqualTo(1));
            var item = _host.Q<VisualElement>(className: ItemConnectionClass);
            Assert.That(item, Is.Not.Null);
        }

        [Test]
        [Description("再接続中（Reconnecting）で接続警告が表示される（Requirement 9.5）")]
        public void Connection_Reconnecting_ShowsWarning()
        {
            using var ctrl = CreateController();

            _connection.RaiseStatus(ConnectionStatusCode.Disconnected, ConnectionStatusCode.Reconnecting);

            Assert.That(CountItems(_host), Is.EqualTo(1));
            Assert.That(_host.Q<VisualElement>(className: ItemConnectionClass), Is.Not.Null);
        }

        [Test]
        [Description("接続恒常断（FailedPermanently）で接続警告が表示される（Requirement 9.5）")]
        public void Connection_FailedPermanently_ShowsWarning()
        {
            using var ctrl = CreateController();

            _connection.RaiseStatus(ConnectionStatusCode.Reconnecting, ConnectionStatusCode.FailedPermanently);

            Assert.That(CountItems(_host), Is.EqualTo(1));
        }

        [Test]
        [Description("接続復旧（→ Connected）で既存の接続警告が消える")]
        public void Connection_RecoveredToConnected_ClearsWarning()
        {
            using var ctrl = CreateController();
            _connection.RaiseStatus(ConnectionStatusCode.Connected, ConnectionStatusCode.Disconnected);
            Assume.That(CountItems(_host), Is.EqualTo(1));

            _connection.RaiseStatus(ConnectionStatusCode.Reconnecting, ConnectionStatusCode.Connected);

            Assert.That(CountItems(_host), Is.EqualTo(0));
        }

        [Test]
        [Description("接続警告は遷移を重ねても 1 件に集約される（同一カテゴリは縦積みしない）")]
        public void Connection_MultipleTransitions_DedupeToSingleItem()
        {
            using var ctrl = CreateController();

            _connection.RaiseStatus(ConnectionStatusCode.Connected, ConnectionStatusCode.Disconnected);
            _connection.RaiseStatus(ConnectionStatusCode.Disconnected, ConnectionStatusCode.Reconnecting);
            _connection.RaiseStatus(ConnectionStatusCode.Reconnecting, ConnectionStatusCode.FailedPermanently);

            Assert.That(CountItems(_host), Is.EqualTo(1));
        }

        // ---- close (dismiss) -------------------------------------------

        [Test]
        [Description("閉じるボタンクリックで通知行が一時非表示化される（Requirement 9.5 操作要件）")]
        public void CloseButton_Click_DismissesItem()
        {
            using var ctrl = CreateController();
            _connection.RaiseStatus(ConnectionStatusCode.Connected, ConnectionStatusCode.Disconnected);
            Assume.That(CountItems(_host), Is.EqualTo(1));

            var close = _host.Q<Button>(className: CloseButtonClass);
            Assert.That(close, Is.Not.Null, "close button should exist on rendered notification");
            InvokeClickable(close);

            Assert.That(CountItems(_host), Is.EqualTo(0));
        }

        [Test]
        [Description("閉じても状態が継続して再通知が来たら警告が再表示される（Requirement 9.5 再表示条件）")]
        public void Connection_ReNotify_AfterDismiss_RestoresWarning()
        {
            using var ctrl = CreateController();
            _connection.RaiseStatus(ConnectionStatusCode.Connected, ConnectionStatusCode.Disconnected);
            var firstClose = _host.Q<Button>(className: CloseButtonClass);
            InvokeClickable(firstClose);
            Assume.That(CountItems(_host), Is.EqualTo(0));

            // Operator should see a fresh warning if the disconnection state
            // continues and another transition arrives (e.g. Disconnected → Reconnecting).
            _connection.RaiseStatus(ConnectionStatusCode.Disconnected, ConnectionStatusCode.Reconnecting);

            Assert.That(CountItems(_host), Is.EqualTo(1));
        }

        // ---- preload failure -------------------------------------------

        [Test]
        [Description("タブのプリロード失敗で警告が追加される（Requirement 6.6）")]
        public void PreloadFailure_AddsWarning()
        {
            using var ctrl = CreateController();

            _registry.MarkTabFailed(TabId.StageLighting, "skin missing");

            Assert.That(CountItems(_host), Is.EqualTo(1));
            Assert.That(_host.Q<VisualElement>(className: ItemPreloadFailureClass), Is.Not.Null);
        }

        [Test]
        [Description("複数タブの失敗ごとに別個の警告が縦積みされる（同カテゴリでもタブ ID 単位で別件）")]
        public void PreloadFailure_TwoTabs_StackVertically()
        {
            using var ctrl = CreateController();

            _registry.MarkTabFailed(TabId.StageLighting, "skin missing");
            _registry.MarkTabFailed(TabId.CameraSwitcher, "addressables init failed");

            Assert.That(CountItems(_host), Is.EqualTo(2));
        }

        // ---- display fallback ------------------------------------------

        [Test]
        [Description("Display 1 フォールバック警告は ShowDisplayFallback で公開 API 経由で追加される（Requirement 9.6）")]
        public void DisplayFallback_Show_AddsWarning()
        {
            using var ctrl = CreateController();

            ctrl.ShowDisplayFallback("display-1", "Output is rendering on Display 1 (fallback).");

            Assert.That(CountItems(_host), Is.EqualTo(1));
            Assert.That(_host.Q<VisualElement>(className: ItemDisplayFallbackClass), Is.Not.Null);
        }

        [Test]
        [Description("ClearDisplayFallback で同 key の警告が消える（fallback 解除時の挙動）")]
        public void DisplayFallback_Clear_RemovesWarning()
        {
            using var ctrl = CreateController();
            ctrl.ShowDisplayFallback("display-1", "fallback");
            Assume.That(CountItems(_host), Is.EqualTo(1));

            ctrl.ClearDisplayFallback("display-1");

            Assert.That(CountItems(_host), Is.EqualTo(0));
        }

        [Test]
        [Description("ShowDisplayFallback は同 key に対しては差し替え（重複追加しない）")]
        public void DisplayFallback_Show_SameKey_DoesNotDuplicate()
        {
            using var ctrl = CreateController();

            ctrl.ShowDisplayFallback("display-1", "first");
            ctrl.ShowDisplayFallback("display-1", "second");

            Assert.That(CountItems(_host), Is.EqualTo(1));
        }

        // ---- four-warning vertical stack -------------------------------

        [Test]
        [Description("4 種警告（接続断 + Display フォールバック + プリロード失敗 ×2）の同時発生で 3 件まで縦積みされ、超過分は診断パネル（ログ）へ流れる")]
        public void FourWarnings_RenderMaxThree_LogOverflow()
        {
            using var ctrl = CreateController();

            _connection.RaiseStatus(ConnectionStatusCode.Connected, ConnectionStatusCode.Disconnected);
            ctrl.ShowDisplayFallback("display-1", "fallback");
            _registry.MarkTabFailed(TabId.Character, "uxml missing");
            _registry.MarkTabFailed(TabId.StageLighting, "skin missing");

            Assert.That(ctrl.ActiveNotificationCount, Is.EqualTo(4));
            Assert.That(CountItems(_host), Is.EqualTo(3),
                "only 3 items should be rendered; the 4th overflows to the diagnostics panel");

            // Overflow must be observable through the diagnostics logger so the
            // diagnostics panel can pick it up.
            var sawOverflow = false;
            foreach (var entry in _logger.Entries)
            {
                if (entry.Level >= LogLevel.Warning &&
                    entry.Message.Contains("overflow", StringComparison.OrdinalIgnoreCase))
                {
                    sawOverflow = true;
                    break;
                }
            }
            Assert.That(sawOverflow, Is.True,
                "overflow beyond 3 items must be recorded via the diagnostics logger");
        }

        // ---- dispose ---------------------------------------------------

        [Test]
        [Description("Dispose 後は OnStatusChanged / OnPreloadChanged を購読解除し、後続イベントで描画されない")]
        public void Dispose_UnsubscribesFromEvents()
        {
            var ctrl = CreateController();
            ctrl.Dispose();

            _connection.RaiseStatus(ConnectionStatusCode.Connected, ConnectionStatusCode.Disconnected);
            _registry.MarkTabFailed(TabId.Character, "skin missing");

            Assert.That(CountItems(_host), Is.EqualTo(0));
        }

        // ---- helpers ---------------------------------------------------

        private static void InvokeClickable(Button button)
        {
            if (button is null) throw new ArgumentNullException(nameof(button));
            // Reach into Clickable's private 'clicked' event to fire the
            // wired Action without depending on Unity's pointer-event pipeline
            // in EditMode (mirrors TabBarControllerTests).
            var field = typeof(Clickable).GetField(
                "clicked",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Clickable.clicked backing field");
            var del = (Action?)field!.GetValue(button.clickable);
            Assert.That(del, Is.Not.Null, "close button must have a click handler wired");
            del!.Invoke();
        }

        // ---- in-test fakes ---------------------------------------------

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
