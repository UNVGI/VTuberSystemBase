#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 11.1: <see cref="UiShellBootstrapper"/> が <see cref="UiToolkitShellSkinProfile"/>
    /// を読み込み、ルート / タブ UXML / USS を差し替える経路の結合テスト
    /// （Requirement 6.3, 6.4, 6.5, 6.6, 6.7, 6.8; design.md §Bootstrap §Skin）。
    /// 既定プロファイル → 別 SO 注入の 2 ケースで USS 変化と UXML 差し替えが反映され、
    /// SkinValidator が必須クラス欠落タブのみを失敗マークし他タブ/シェル全体は継続し、
    /// CommonUiStyleSheets が後ろほど優先される順序で積まれることを固定する。
    /// </summary>
    [TestFixture]
    public sealed class UiShellSkinIntegrationTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private FakeIpcClient _bus = null!;
        private FakeRootUiDocumentFactory _rootFactory = null!;
        private FakeTabMountStrategy _tabMount = null!;
        private FakeAddressablesInitializer _addressables = null!;
        private List<UnityEngine.Object> _disposables = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _bus = new FakeIpcClient();
            _rootFactory = new FakeRootUiDocumentFactory();
            _tabMount = new FakeTabMountStrategy();
            _addressables = new FakeAddressablesInitializer();
            _disposables = new List<UnityEngine.Object>();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                if (_disposables[i] != null) UnityEngine.Object.DestroyImmediate(_disposables[i]);
            }
            _disposables.Clear();
        }

        // ---- helpers ----------------------------------------------------

        private UiToolkitShellSkinProfile NewSkinProfile()
        {
            var skin = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            _disposables.Add(skin);
            var rootVta = ScriptableObject.CreateInstance<VisualTreeAsset>();
            _disposables.Add(rootVta);
            skin.RootVisualTreeAsset = rootVta;
            return skin;
        }

        private StyleSheet NewStyleSheet()
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            _disposables.Add(sheet);
            return sheet;
        }

        private UiShellConfig MakeConfig(UiToolkitShellSkinProfile skin)
        {
            return new UiShellConfig
            {
                SkinProfile = skin,
                IpcBus = _bus,
                TabMountStrategy = _tabMount,
                AddressablesInitializer = _addressables,
                DiagnosticsLogger = _logger,
            };
        }

        private UiShellBootstrapper MakeBootstrapper()
        {
            return new UiShellBootstrapper(_rootFactory);
        }

        private static IList<StyleSheet> SnapshotStyleSheets(VisualElement element)
        {
            var copy = new List<StyleSheet>();
            for (var i = 0; i < element.styleSheets.count; i++)
            {
                copy.Add(element.styleSheets[i]);
            }
            return copy;
        }

        // ---- 1. UXML 差し替え（既定 SO → 別 SO） ------------------------

        [Test]
        [Description("デフォルト SkinProfile.RootVisualTreeAsset がそのまま IRootUiDocumentFactory に渡される")]
        public void StartShell_RoutesSkinProfileRootVisualTreeAssetToFactory()
        {
            var skin = NewSkinProfile();
            using var bootstrapper = MakeBootstrapper();

            var result = bootstrapper.StartShell(MakeConfig(skin));

            Assert.That(result.Success, Is.True, $"StartShell failed: {result.Error} {result.Detail}");
            Assert.That(_rootFactory.LastSkinProfile, Is.SameAs(skin));
            Assert.That(_rootFactory.LastSkinRootVisualTreeAsset, Is.SameAs(skin.RootVisualTreeAsset),
                "SkinProfile.RootVisualTreeAsset must be the asset routed to the factory.");
        }

        [Test]
        [Description("別 SO に差し替えると新しい RootVisualTreeAsset が経路に流れる（UXML 差し替え）")]
        public void StartShell_SwappedSkinProfile_RoutesNewRootVisualTreeAsset()
        {
            var skinA = NewSkinProfile();
            using (var bootstrapper = MakeBootstrapper())
            {
                var resultA = bootstrapper.StartShell(MakeConfig(skinA));
                Assert.That(resultA.Success, Is.True);
                Assert.That(_rootFactory.LastSkinRootVisualTreeAsset, Is.SameAs(skinA.RootVisualTreeAsset));
            }

            // Reset fakes so the second startup observes a fresh routing.
            _rootFactory = new FakeRootUiDocumentFactory();
            _tabMount = new FakeTabMountStrategy();
            var skinB = NewSkinProfile();
            using var bootstrapper2 = MakeBootstrapper();

            var resultB = bootstrapper2.StartShell(MakeConfig(skinB));

            Assert.That(resultB.Success, Is.True);
            Assert.That(_rootFactory.LastSkinProfile, Is.SameAs(skinB));
            Assert.That(_rootFactory.LastSkinRootVisualTreeAsset, Is.SameAs(skinB.RootVisualTreeAsset));
            Assert.That(skinA.RootVisualTreeAsset, Is.Not.SameAs(skinB.RootVisualTreeAsset),
                "Sanity: 2 つのスキンは別 VisualTreeAsset を持つこと。");
        }

        // ---- 2. USS 適用（root + per-tab + 順序契約） -------------------

        [Test]
        [Description("RootStyleSheets がルート VisualElement.styleSheets に配列順で積まれる")]
        public void StartShell_AppliesRootStyleSheetsToRootVisualElement()
        {
            var skin = NewSkinProfile();
            var s1 = NewStyleSheet();
            var s2 = NewStyleSheet();
            skin.RootStyleSheets.Add(s1);
            skin.RootStyleSheets.Add(s2);

            using var bootstrapper = MakeBootstrapper();
            var result = bootstrapper.StartShell(MakeConfig(skin));

            Assert.That(result.Success, Is.True);
            var sheets = SnapshotStyleSheets(bootstrapper.RootVisualElement!);
            Assert.That(sheets, Does.Contain(s1));
            Assert.That(sheets, Does.Contain(s2));
            Assert.That(sheets.IndexOf(s1), Is.LessThan(sheets.IndexOf(s2)),
                "RootStyleSheets は配列順に積まれること。");
        }

        [Test]
        [Description("CommonUiStyleSheets は RootStyleSheets の後に積まれ、後ろほど優先される")]
        public void StartShell_CommonUiStyleSheets_AppendedAfterRootSheets()
        {
            var skin = NewSkinProfile();
            var rootSheet = NewStyleSheet();
            var commonSheetA = NewStyleSheet();
            var commonSheetB = NewStyleSheet();
            skin.RootStyleSheets.Add(rootSheet);
            skin.CommonUiStyleSheets.Add(commonSheetA);
            skin.CommonUiStyleSheets.Add(commonSheetB);

            using var bootstrapper = MakeBootstrapper();
            var result = bootstrapper.StartShell(MakeConfig(skin));

            Assert.That(result.Success, Is.True);
            var sheets = SnapshotStyleSheets(bootstrapper.RootVisualElement!);
            Assert.That(sheets, Does.Contain(rootSheet));
            Assert.That(sheets, Does.Contain(commonSheetA));
            Assert.That(sheets, Does.Contain(commonSheetB));
            Assert.That(sheets.IndexOf(rootSheet), Is.LessThan(sheets.IndexOf(commonSheetA)),
                "CommonUiStyleSheets は RootStyleSheets よりも後に積まれること（後ろほど優先）。");
            Assert.That(sheets.IndexOf(commonSheetA), Is.LessThan(sheets.IndexOf(commonSheetB)),
                "CommonUiStyleSheets 内も配列順に積まれること。");
        }

        [Test]
        [Description("CharacterTabStyleSheets は Character タブの bound root にだけ積まれる")]
        public void StartShell_AppliesPerTabStyleSheets_OnlyToBoundTabRoot()
        {
            var skin = NewSkinProfile();
            var charSheet = NewStyleSheet();
            var stageSheet = NewStyleSheet();
            skin.CharacterTabStyleSheets.Add(charSheet);
            skin.StageLightingTabStyleSheets.Add(stageSheet);

            using var bootstrapper = MakeBootstrapper();
            var result = bootstrapper.StartShell(MakeConfig(skin));

            Assert.That(result.Success, Is.True);
            var charRoot = _tabMount.CreatedRoots[TabId.Character];
            var stageRoot = _tabMount.CreatedRoots[TabId.StageLighting];
            var camRoot = _tabMount.CreatedRoots[TabId.CameraSwitcher];

            Assert.That(SnapshotStyleSheets(charRoot), Has.Member(charSheet),
                "Character タブには CharacterTabStyleSheets が積まれること。");
            Assert.That(SnapshotStyleSheets(charRoot), Has.No.Member(stageSheet),
                "Character タブに StageLightingTabStyleSheets が積まれてはならない。");
            Assert.That(SnapshotStyleSheets(stageRoot), Has.Member(stageSheet));
            Assert.That(SnapshotStyleSheets(camRoot), Has.No.Member(charSheet));
            Assert.That(SnapshotStyleSheets(camRoot), Has.No.Member(stageSheet));
        }

        // ---- 3. SkinValidator 結合：必須クラス欠落 → 該当タブ非活性化 ----

        [Test]
        [Description("Character タブの必須モディファイア欠落で SkinValidator が該当タブのみ failed にする")]
        public void StartShell_TabMissingRequiredClass_MarksOnlyThatTabFailed()
        {
            _tabMount.OmitModifierClassFor.Add(TabId.Character);
            var skin = NewSkinProfile();

            using var bootstrapper = MakeBootstrapper();
            var result = bootstrapper.StartShell(MakeConfig(skin));

            Assert.That(result.Success, Is.True,
                "Skin 検証失敗 1 タブだけではシェル全体は継続する（Requirement 6.6）。");
            Assert.That(bootstrapper.SkinValidationReport, Is.Not.Null);
            Assert.That(bootstrapper.SkinValidationReport!.Value.AllValid, Is.False);

            var progress = bootstrapper.TabPanelRegistry!.GetPreloadProgress();
            Assert.That(progress.FailedTabs, Has.Member(TabId.Character),
                "Character タブが SkinValidator により failed マークされていること。");
            Assert.That(progress.FailedTabs, Has.No.Member(TabId.StageLighting));
            Assert.That(progress.FailedTabs, Has.No.Member(TabId.CameraSwitcher));
            Assert.That(bootstrapper.TabPanelRegistry.IsPreloadComplete, Is.True,
                "失敗タブも preload 完了扱い（Requirement 3.5）。");
        }

        [Test]
        [Description("全タブの必須クラスが揃っていれば SkinValidationReport.AllValid == true、失敗タブなし")]
        public void StartShell_AllTabsHaveRequiredClasses_NoFailedTabs()
        {
            var skin = NewSkinProfile();

            using var bootstrapper = MakeBootstrapper();
            var result = bootstrapper.StartShell(MakeConfig(skin));

            Assert.That(result.Success, Is.True);
            Assert.That(bootstrapper.SkinValidationReport, Is.Not.Null);
            Assert.That(bootstrapper.SkinValidationReport!.Value.AllValid, Is.True,
                "FakeTabMountStrategy はデフォルトで全タブの必須クラスを付与するため AllValid であること。");
            var progress = bootstrapper.TabPanelRegistry!.GetPreloadProgress();
            Assert.That(progress.FailedTabs.Count, Is.EqualTo(0));
        }

        // ---- 4. ResolveTabRoots は registry の bound roots を返す -------

        [Test]
        [Description("TabPanelRegistry.SnapshotTabRoots は NotifyTabMounted で束ねた要素を返す")]
        public void TabPanelRegistry_SnapshotTabRoots_ReturnsBoundElements()
        {
            var skin = NewSkinProfile();
            using var bootstrapper = MakeBootstrapper();

            bootstrapper.StartShell(MakeConfig(skin));

            var snapshot = bootstrapper.TabPanelRegistry!.SnapshotTabRoots();
            Assert.That(snapshot, Has.Count.EqualTo(3));
            Assert.That(snapshot[TabId.Character], Is.SameAs(_tabMount.CreatedRoots[TabId.Character]));
            Assert.That(snapshot[TabId.StageLighting], Is.SameAs(_tabMount.CreatedRoots[TabId.StageLighting]));
            Assert.That(snapshot[TabId.CameraSwitcher], Is.SameAs(_tabMount.CreatedRoots[TabId.CameraSwitcher]));
        }
    }
}
