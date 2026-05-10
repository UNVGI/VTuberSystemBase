#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 8.4: <see cref="TabBarController"/> contract tests. Pin the
    /// preload-disabled state on construction (Requirement 2.7, 3.2),
    /// the activation of the initial Character tab when preload completes
    /// (Requirement 3.3), the active-class toggling on the three buttons
    /// (Requirement 2.2), the click → <c>TabPanelRegistry.SwitchTo</c>
    /// dispatch (Requirement 2.6), the no-op handling of clicks before
    /// preload finishes (Requirement 2.7), and the failed-tab carve-out where
    /// a degraded tab keeps its disabled class even after preload completes
    /// (Requirement 3.5, 9.2).
    /// </summary>
    [TestFixture]
    public sealed class TabBarControllerTests
    {
        private const string TabBarButtonClass = "vsb-tab-bar__button";
        private const string TabBarButtonActiveClass = "vsb-tab-bar__button--active";
        private const string TabBarButtonDisabledClass = "vsb-tab-bar__button--disabled";

        private const string CharacterButtonName = "vsb-tab-bar__button--character";
        private const string StageButtonName = "vsb-tab-bar__button--stage-lighting";
        private const string CameraButtonName = "vsb-tab-bar__button--camera-switcher";

        private RecordingDiagnosticsLogger _logger = null!;
        private TabPanelRegistry _registry = null!;
        private VisualElement _host = null!;
        private Button _btnCharacter = null!;
        private Button _btnStage = null!;
        private Button _btnCamera = null!;
        private Dictionary<TabId, VisualElement> _tabRoots = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _registry = new TabPanelRegistry(_logger);
            _host = BuildTabBarHost(out _btnCharacter, out _btnStage, out _btnCamera);
            _tabRoots = new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, new VisualElement { name = "tab-character" } },
                { TabId.StageLighting, new VisualElement { name = "tab-stage-lighting" } },
                { TabId.CameraSwitcher, new VisualElement { name = "tab-camera-switcher" } },
            };
        }

        // ---- helpers ---------------------------------------------------

        private static VisualElement BuildTabBarHost(
            out Button character,
            out Button stage,
            out Button camera)
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

        // ---- construction ----------------------------------------------

        [Test]
        [Description("コンストラクタは null registry / host / logger を拒否する（DI 契約）")]
        public void Constructor_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(
                () => new TabBarController(null!, _host, _logger));
            Assert.Throws<ArgumentNullException>(
                () => new TabBarController(_registry, null!, _logger));
            Assert.Throws<ArgumentNullException>(
                () => new TabBarController(_registry, _host, null!));
        }

        [Test]
        [Description("ホストに必要なボタンが存在しない場合は InvalidOperationException で失敗する")]
        public void Constructor_MissingButton_Throws()
        {
            var brokenHost = new VisualElement();
            // No buttons attached.
            Assert.Throws<InvalidOperationException>(
                () => new TabBarController(_registry, brokenHost, _logger));
        }

        [Test]
        [Description("プリロード未完了の初期状態では 3 ボタン全てに disabled クラスが付与される（Requirement 2.7, 3.2）")]
        public void Constructor_BeforePreloadComplete_AllButtonsHaveDisabledClass()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            Assert.That(_btnCharacter.ClassListContains(TabBarButtonDisabledClass), Is.True);
            Assert.That(_btnStage.ClassListContains(TabBarButtonDisabledClass), Is.True);
            Assert.That(_btnCamera.ClassListContains(TabBarButtonDisabledClass), Is.True);
            Assert.That(ctrl.IsEnabled, Is.False);
        }

        [Test]
        [Description("プリロード未完了の初期状態ではどのボタンにも active クラスが付かない")]
        public void Constructor_BeforePreloadComplete_NoActiveClass()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            Assert.That(_btnCharacter.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_btnStage.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_btnCamera.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_registry.ActiveTab, Is.Null);
        }

        [Test]
        [Description("プリロード未完了で SetEnabled(false) によりボタン操作が UI レベルでも無効化される")]
        public void Constructor_BeforePreloadComplete_ButtonsAreNotEnabledSelf()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            Assert.That(_btnCharacter.enabledSelf, Is.False);
            Assert.That(_btnStage.enabledSelf, Is.False);
            Assert.That(_btnCamera.enabledSelf, Is.False);
        }

        // ---- preload completion ----------------------------------------

        [Test]
        [Description("プリロード完了で 3 ボタンの disabled クラスが除去される（Requirement 3.3）")]
        public void PreloadComplete_RemovesDisabledClass_FromAllSuccessTabs()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            MountAllTabs();

            Assert.That(_btnCharacter.ClassListContains(TabBarButtonDisabledClass), Is.False);
            Assert.That(_btnStage.ClassListContains(TabBarButtonDisabledClass), Is.False);
            Assert.That(_btnCamera.ClassListContains(TabBarButtonDisabledClass), Is.False);
            Assert.That(ctrl.IsEnabled, Is.True);
        }

        [Test]
        [Description("プリロード完了時に初期タブ（Character）がアクティブ化され active クラスが付く（Requirement 3.3, 2.2）")]
        public void PreloadComplete_ActivatesInitialCharacterTab()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            MountAllTabs();

            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
            Assert.That(_btnCharacter.ClassListContains(TabBarButtonActiveClass), Is.True);
            Assert.That(_btnStage.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_btnCamera.ClassListContains(TabBarButtonActiveClass), Is.False);
        }

        [Test]
        [Description("失敗マークされたタブはプリロード完了後も disabled クラスを保持する（Requirement 3.5）")]
        public void PreloadComplete_FailedTab_KeepsDisabledClass()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            _registry.NotifyTabMounted(TabId.Character, _tabRoots[TabId.Character]);
            _registry.NotifyTabMounted(TabId.CameraSwitcher, _tabRoots[TabId.CameraSwitcher]);
            _registry.MarkTabFailed(TabId.StageLighting, "skin missing");

            Assert.That(_btnStage.ClassListContains(TabBarButtonDisabledClass), Is.True);
            Assert.That(_btnStage.enabledSelf, Is.False);
            Assert.That(_btnCharacter.ClassListContains(TabBarButtonDisabledClass), Is.False);
            Assert.That(_btnCamera.ClassListContains(TabBarButtonDisabledClass), Is.False);
        }

        [Test]
        [Description("コントローラ生成時に既にプリロードが完了していても初期タブが活性化される（晩生成のフェイルセーフ）")]
        public void Constructor_PreloadAlreadyComplete_ActivatesInitialTab()
        {
            MountAllTabs();

            using var ctrl = new TabBarController(_registry, _host, _logger);

            Assert.That(ctrl.IsEnabled, Is.True);
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
            Assert.That(_btnCharacter.ClassListContains(TabBarButtonActiveClass), Is.True);
        }

        // ---- click handling --------------------------------------------

        [Test]
        [Description("プリロード完了前のクリックは無視され、SwitchTo は呼ばれない（Requirement 2.7）")]
        public void Click_BeforePreloadComplete_IsIgnored()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            ctrl.HandleTabButtonClicked(TabId.Character);

            Assert.That(_registry.ActiveTab, Is.Null);
            Assert.That(_btnCharacter.ClassListContains(TabBarButtonActiveClass), Is.False);
        }

        [Test]
        [Description("プリロード完了後の他タブクリックで SwitchTo が呼ばれて ActiveTab が更新される（Requirement 2.6）")]
        public void Click_AfterPreloadComplete_SwitchesActiveTab()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);
            MountAllTabs();
            // Character is now active by default.
            Assume.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));

            ctrl.HandleTabButtonClicked(TabId.StageLighting);

            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.StageLighting));
        }

        [Test]
        [Description("クリック切替後は active クラスが新タブのボタンに移動する（Requirement 2.2）")]
        public void Click_AfterPreloadComplete_MovesActiveClass()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);
            MountAllTabs();

            ctrl.HandleTabButtonClicked(TabId.CameraSwitcher);

            Assert.That(_btnCharacter.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_btnStage.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_btnCamera.ClassListContains(TabBarButtonActiveClass), Is.True);
        }

        [Test]
        [Description("失敗マーク済みタブのボタンクリックは ActiveTab を変えず、active クラスも移らない")]
        public void Click_FailedTab_DoesNotSwitch()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);
            _registry.NotifyTabMounted(TabId.Character, _tabRoots[TabId.Character]);
            _registry.NotifyTabMounted(TabId.CameraSwitcher, _tabRoots[TabId.CameraSwitcher]);
            _registry.MarkTabFailed(TabId.StageLighting, "skin missing");

            // Character is initial active. Clicking the failed tab should be a no-op.
            ctrl.HandleTabButtonClicked(TabId.StageLighting);

            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
            Assert.That(_btnStage.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_btnCharacter.ClassListContains(TabBarButtonActiveClass), Is.True);
        }

        [Test]
        [Description("既にアクティブなタブのクリックは AlreadyActive で拒否されてもボタン状態は崩れない")]
        public void Click_OnAlreadyActiveTab_LeavesUiConsistent()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);
            MountAllTabs();

            ctrl.HandleTabButtonClicked(TabId.Character);

            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
            Assert.That(_btnCharacter.ClassListContains(TabBarButtonActiveClass), Is.True);
            Assert.That(_btnStage.ClassListContains(TabBarButtonActiveClass), Is.False);
            Assert.That(_btnCamera.ClassListContains(TabBarButtonActiveClass), Is.False);
        }

        [Test]
        [Description("Button.clicked にコントローラのハンドラが結線され、Action 起動経由で SwitchTo が走る（Requirement 2.6 配線確認）")]
        public void Button_Click_DispatchesToRegistrySwitchTo()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);
            MountAllTabs();

            // Reach into Clickable's event backing field via reflection to
            // invoke the wired Action without requiring a panel-attached
            // pointer event pipeline. This proves the controller bound the
            // click handler at construction and that the path
            // Button.clicked → controller → registry.SwitchTo works without
            // depending on Unity's pointer-event simulation in EditMode.
            var clickedField = typeof(Clickable).GetField(
                "clicked",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assume.That(clickedField, Is.Not.Null,
                "Clickable.clicked backing field is expected to exist for reflection invocation.");
            var clickedDelegate = (Action?)clickedField!.GetValue(_btnStage.clickable);
            Assume.That(clickedDelegate, Is.Not.Null,
                "TabBarController must have subscribed to Button.clicked at construction.");

            clickedDelegate!.Invoke();

            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.StageLighting));
        }

        // ---- diagnostics -----------------------------------------------

        [Test]
        [Description("プリロード未完了クリックは TabSwitch カテゴリのログを残す")]
        public void Click_BeforePreloadComplete_LogsTabSwitchCategory()
        {
            using var ctrl = new TabBarController(_registry, _host, _logger);

            ctrl.HandleTabButtonClicked(TabId.Character);

            var hasTabSwitchEntry = false;
            foreach (var entry in _logger.Entries)
            {
                if (entry.Category == LogCategory.TabSwitch) { hasTabSwitchEntry = true; break; }
            }
            Assert.That(hasTabSwitchEntry, Is.True);
        }

        // ---- dispose ---------------------------------------------------

        [Test]
        [Description("Dispose 後はレジストリのプリロード完了通知に反応しない（イベント解除）")]
        public void Dispose_StopsRespondingToPreloadEvents()
        {
            var ctrl = new TabBarController(_registry, _host, _logger);
            ctrl.Dispose();

            MountAllTabs();

            // Buttons must remain in their pre-dispose disabled state because
            // the controller no longer listens to OnPreloadChanged.
            Assert.That(_btnCharacter.ClassListContains(TabBarButtonDisabledClass), Is.True);
            Assert.That(ctrl.IsEnabled, Is.False);
        }

        [Test]
        [Description("Dispose 後のクリックハンドラ呼出しは何もしない（idempotent）")]
        public void Dispose_HandleClickIsNoop()
        {
            var ctrl = new TabBarController(_registry, _host, _logger);
            MountAllTabs();
            ctrl.Dispose();

            // After dispose, click should not throw and not change active tab.
            // (ActiveTab was set to Character at preload completion before
            // Dispose; we assert it doesn't move to StageLighting.)
            Assume.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
            ctrl.HandleTabButtonClicked(TabId.StageLighting);

            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
        }

        [Test]
        [Description("Dispose は二重呼出に対して例外を投げない（Idempotent）")]
        public void Dispose_DoubleDispose_DoesNotThrow()
        {
            var ctrl = new TabBarController(_registry, _host, _logger);

            ctrl.Dispose();
            Assert.DoesNotThrow(() => ctrl.Dispose());
        }
    }
}
