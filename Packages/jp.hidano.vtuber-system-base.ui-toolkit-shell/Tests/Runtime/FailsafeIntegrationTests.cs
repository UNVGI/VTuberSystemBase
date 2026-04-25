#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.FailsafeAndConnection;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 9.3: メイン出力側未接続時のフェイルセーフ挙動を結合テストで固定する。
    /// <para>
    /// <see cref="FakeIpcClient"/> の接続を失敗状態で固定したうえで、UI 起動・タブ切替・
    /// 共通コンポーネント動作が継続すること（Requirements 9.1, 9.2, 9.7）と、
    /// <see cref="UiCommandClient.PublishState{TPayload}"/> が
    /// <see cref="SendErrorCode.NotConnected"/> を即時返却し UI 側に例外が波及しないこと
    /// （Requirement 9.4）、後から接続が確立した場合に送信が通常成功に切り替わること
    /// （Requirement 9.3）を、Commands / FailsafeAndConnection / Panels / CommonUi の
    /// コンポーネントを実環境に近い形で組み合わせて検証する（design.md §Testing
    /// Strategy 「メイン出力未接続での起動完遂」）。
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class FailsafeIntegrationTests
    {
        private const string TabBarButtonClass = "vsb-tab-bar__button";
        private const string CharacterButtonName = "vsb-tab-bar__button--character";
        private const string StageButtonName = "vsb-tab-bar__button--stage-lighting";
        private const string CameraButtonName = "vsb-tab-bar__button--camera-switcher";
        private const string FallbackTopic = "output/display/fallback";

        private RecordingDiagnosticsLogger _logger = null!;
        private FakeIpcClient _bus = null!;
        private ConnectionStatus _connectionStatus = null!;
        private UiCommandClient _commandClient = null!;
        private UiSubscriptionClient _subscriptionClient = null!;
        private TabPanelRegistry _registry = null!;
        private VisualElement _tabBarHost = null!;
        private VisualElement _notificationHost = null!;
        private TabBarController _tabBar = null!;
        private NotificationBarController _notificationBar = null!;
        private MainOutputStatusWatcher _watcher = null!;
        private Dictionary<TabId, VisualElement> _tabRoots = null!;
        private Button _btnCharacter = null!;
        private Button _btnStage = null!;
        private Button _btnCamera = null!;

        [SetUp]
        public void SetUp()
        {
            MainThreadAffinity.Capture();
            _logger = new RecordingDiagnosticsLogger();

            // Bus stays in Disconnected state — never transition to Connected during the
            // permanent-failure scenarios. Tests that exercise the late-connect path will
            // explicitly drive the bus to Connected later.
            _bus = new FakeIpcClient();

            _connectionStatus = new ConnectionStatus(_bus);
            _commandClient = new UiCommandClient(_bus, _connectionStatus, _logger);
            _subscriptionClient = new UiSubscriptionClient(_bus, _logger);

            _registry = new TabPanelRegistry(_logger);
            _tabRoots = new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, new VisualElement { name = "tab-character" } },
                { TabId.StageLighting, new VisualElement { name = "tab-stage-lighting" } },
                { TabId.CameraSwitcher, new VisualElement { name = "tab-camera-switcher" } },
            };

            _tabBarHost = BuildTabBarHost(out _btnCharacter, out _btnStage, out _btnCamera);

            _notificationHost = new VisualElement { name = "vsb-notification-bar" };
            _notificationHost.AddToClassList("vsb-notification-bar");
            _notificationBar = new NotificationBarController(_notificationHost, _connectionStatus, _registry, _logger);
            _watcher = new MainOutputStatusWatcher(_subscriptionClient, _notificationBar, _logger);
        }

        [TearDown]
        public void TearDown()
        {
            _tabBar?.Dispose();
            _watcher?.Dispose();
            _notificationBar?.Dispose();
            _connectionStatus?.Dispose();
            MainThreadAffinity.Reset();
        }

        // ---- helpers --------------------------------------------------------

        private static VisualElement BuildTabBarHost(out Button character, out Button stage, out Button camera)
        {
            var host = new VisualElement { name = "vsb-shell-root" };
            var bar = new VisualElement { name = "vsb-tab-bar" };
            bar.AddToClassList("vsb-tab-bar");
            character = MakeTabButton(CharacterButtonName);
            stage = MakeTabButton(StageButtonName);
            camera = MakeTabButton(CameraButtonName);
            bar.Add(character);
            bar.Add(stage);
            bar.Add(camera);
            host.Add(bar);
            return host;
        }

        private static Button MakeTabButton(string name)
        {
            var b = new Button { name = name };
            b.AddToClassList(TabBarButtonClass);
            return b;
        }

        private void MountAllTabs()
        {
            foreach (var pair in _tabRoots)
            {
                _registry.NotifyTabMounted(pair.Key, pair.Value);
            }
        }

        private void StartShell()
        {
            // Tab bar comes up after the registry has observed all 3 mounts; mirrors
            // the bootstrapper's preload-then-activate ordering (design.md §Flow 1).
            MountAllTabs();
            _tabBar = new TabBarController(_registry, _tabBarHost, _logger);
        }

        // ---- Scenario 1: 接続永続失敗（PermanentlyDisconnected） ----------

        [Test]
        [Description("接続が永続失敗していても UI 起動（プリロード→タブバー有効化→初期タブ Character 表示）が完了する（Requirement 9.1）")]
        public void PermanentFailure_StartShell_Completes_UiBecomesOperable()
        {
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.PermanentlyDisconnected);

            Assert.DoesNotThrow(StartShell, "shell start must not surface IPC failure as exception");

            Assert.That(_registry.IsPreloadComplete, Is.True, "preload must complete independently of IPC state");
            Assert.That(_tabBar.IsEnabled, Is.True, "tab bar must be enabled after preload, regardless of connection");
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character), "initial tab must activate even without IPC");
            Assert.That(_connectionStatus.IsConnected, Is.False);
            Assert.That(_connectionStatus.CurrentStatus, Is.EqualTo(ConnectionStatusCode.FailedPermanently));
        }

        [Test]
        [Description("接続未確立の状態でもタブ切替が継続して機能する（Requirements 9.2, 9.7）")]
        public void PermanentFailure_TabSwitching_RemainsOperable()
        {
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.PermanentlyDisconnected);
            StartShell();

            // 3 タブを循環的に切り替えても都度 SwitchTo が成功し例外を投げない
            Assert.DoesNotThrow(() => _tabBar.HandleTabButtonClicked(TabId.StageLighting));
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.StageLighting));

            Assert.DoesNotThrow(() => _tabBar.HandleTabButtonClicked(TabId.CameraSwitcher));
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.CameraSwitcher));

            Assert.DoesNotThrow(() => _tabBar.HandleTabButtonClicked(TabId.Character));
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));

            Assert.That(_tabRoots[TabId.Character].style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_tabRoots[TabId.StageLighting].style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(_tabRoots[TabId.CameraSwitcher].style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        [Description("接続未確立中も共通 UI コンポーネント（VsbNumberedList）の操作とイベント発火が継続する（Requirement 9.7）")]
        public void PermanentFailure_CommonUiComponent_RemainsFunctional()
        {
            _bus.SetConnectionState(ConnectionState.PermanentlyDisconnected);
            StartShell();

            var list = new VsbNumberedList();
            var added = 0;
            var removed = 0;
            list.ItemAdded += (_, _) => added++;
            list.ItemRemoved += _ => removed++;

            Assert.DoesNotThrow(() => list.AddItem(new VisualElement()));
            Assert.DoesNotThrow(() => list.AddItem(new VisualElement()));
            Assert.DoesNotThrow(() => list.RemoveAt(0));

            Assert.That(added, Is.EqualTo(2), "VsbNumberedList must continue to fire ItemAdded while IPC is down");
            Assert.That(removed, Is.EqualTo(1), "VsbNumberedList must continue to fire ItemRemoved while IPC is down");
        }

        [Test]
        [Description("接続未確立時の PublishState は SendErrorCode.NotConnected を即時返却し例外を外に投げない（Requirement 9.4）")]
        public void PermanentFailure_PublishState_ReturnsNotConnected_NoException()
        {
            _bus.SetConnectionState(ConnectionState.PermanentlyDisconnected);
            StartShell();

            SendResult result = default;
            Assert.DoesNotThrow(() => result = _commandClient.PublishState("ui/test/state", new { foo = 1 }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(0), "send must short-circuit before reaching the bus");
        }

        [Test]
        [Description("接続未確立時の PublishEvent も SendErrorCode.NotConnected を即時返却し例外を外に投げない（Requirement 9.4）")]
        public void PermanentFailure_PublishEvent_ReturnsNotConnected_NoException()
        {
            _bus.SetConnectionState(ConnectionState.PermanentlyDisconnected);
            StartShell();

            SendResult result = default;
            Assert.DoesNotThrow(() => result = _commandClient.PublishEvent("ui/test/event", new { foo = 1 }));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.HasValue, Is.True);
            Assert.That(result.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));
        }

        [Test]
        [Description("未接続状態が継続している間に複数回 PublishState を呼んでも UI 側に例外が波及しない（Requirements 9.2, 9.4）")]
        public void PermanentFailure_RepeatedPublish_NeverThrows()
        {
            _bus.SetConnectionState(ConnectionState.PermanentlyDisconnected);
            StartShell();

            for (var i = 0; i < 16; i++)
            {
                var iteration = i;
                Assert.DoesNotThrow(
                    () => _commandClient.PublishState("ui/test/state", new { iteration }),
                    $"iteration {iteration} threw under permanent disconnect");
            }
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(0));
        }

        // ---- Scenario 2: 後から接続確立 -------------------------------------

        [Test]
        [Description("起動時は未接続でも、後から接続が確立すれば PublishState が通常成功する（Requirement 9.3）")]
        public void LateConnect_AfterStartup_PublishStateSwitchesToSuccess()
        {
            // Phase 1: shell starts while bus is disconnected.
            StartShell();
            var preConnectResult = _commandClient.PublishState("ui/test/state", new { phase = "before" });
            Assert.That(preConnectResult.Success, Is.False);
            Assert.That(preConnectResult.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(0));

            // Phase 2: connection comes up later.
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.Connected);
            Assert.That(_connectionStatus.IsConnected, Is.True, "ConnectionStatus must reflect the new Connected state");

            var postConnectResult = _commandClient.PublishState("ui/test/state", new { phase = "after" });
            Assert.That(postConnectResult.Success, Is.True, "PublishState must succeed once the bus reports Connected");
            Assert.That(postConnectResult.Error, Is.Null);
            Assert.That(_bus.SentMessages.Count, Is.EqualTo(1), "successful send must reach the bus exactly once");
            Assert.That(_bus.SentMessages[0].Topic, Is.EqualTo("ui/test/state"));
        }

        [Test]
        [Description("接続復旧の前後で UI（タブ切替・通知バー）が一貫して動作する（Requirements 9.2, 9.3, 9.7）")]
        public void LateConnect_TabSwitchingAndNotifications_StayConsistentAcrossTransition()
        {
            StartShell();

            // 未接続中もタブ切替が可能
            _tabBar.HandleTabButtonClicked(TabId.StageLighting);
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.StageLighting));

            // 接続確立への遷移
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.Connected);

            // 接続後もタブ切替が継続
            _tabBar.HandleTabButtonClicked(TabId.CameraSwitcher);
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.CameraSwitcher));

            // 接続後にメイン出力 fallback state を受信すると通知バーが反応する
            _bus.InjectState(FallbackTopic, new MainOutputStatusPayload { IsFallback = true, Reason = "post-connect" });
            Assert.That(_watcher.IsInFallback, Is.True);
        }

        [Test]
        [Description("再切断（Connected → Disconnected）後の PublishState は再び NotConnected を返す（Requirements 9.3, 9.4）")]
        public void Reconnect_ThenDisconnect_PublishStateReturnsNotConnectedAgain()
        {
            StartShell();
            _bus.SetConnectionState(ConnectionState.Connecting);
            _bus.SetConnectionState(ConnectionState.Connected);

            var connectedResult = _commandClient.PublishState("ui/test/state", new { });
            Assert.That(connectedResult.Success, Is.True, "sanity: PublishState succeeds while connected");

            _bus.SetConnectionState(ConnectionState.Disconnected);

            SendResult disconnectedResult = default;
            Assert.DoesNotThrow(() => disconnectedResult = _commandClient.PublishState("ui/test/state", new { }));
            Assert.That(disconnectedResult.Success, Is.False);
            Assert.That(disconnectedResult.Error!.Value.Code, Is.EqualTo(SendErrorCode.NotConnected));
        }
    }
}
