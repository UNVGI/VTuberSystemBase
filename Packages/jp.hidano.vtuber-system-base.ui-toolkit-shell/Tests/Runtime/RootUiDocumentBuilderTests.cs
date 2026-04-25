#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 8.1: <see cref="RootUiDocumentBuilder"/> contract tests. Pin the
    /// shared <see cref="PanelSettings"/> creation contract
    /// (<c>targetDisplay = 0</c> always, with a warning log when a non-zero
    /// value is requested), the <c>Build</c> overload that wires the root
    /// <see cref="UIDocument"/> against the supplied
    /// <see cref="UiToolkitShellSkinProfile"/>, and the structural shape of
    /// the bundled <c>TabBar.uxml</c> default skin (Requirement 1.1, 1.2,
    /// 1.3, 1.7, 6.8).
    /// </summary>
    [TestFixture]
    public sealed class RootUiDocumentBuilderTests
    {
        private const string TabBarUxmlPath =
            "Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/Runtime.UxmlUss/TabBar.uxml";

        private const string EmptyTabShellUxmlPath =
            "Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/Runtime.UxmlUss/EmptyTabShell.uxml";

        private const string NotificationBarUxmlPath =
            "Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/Runtime.UxmlUss/NotificationBar.uxml";

        private RecordingDiagnosticsLogger _logger = null!;
        private RootUiDocumentBuilder _builder = null!;
        private readonly List<UnityEngine.Object> _disposables = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _builder = new RootUiDocumentBuilder(_logger);
            _disposables.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                if (_disposables[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_disposables[i]);
                }
            }
            _disposables.Clear();
        }

        // ---- constructor ------------------------------------------------

        [Test]
        [Description("コンストラクタは null logger を拒否する（DI 契約）")]
        public void Constructor_NullLogger_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RootUiDocumentBuilder(null!));
        }

        // ---- CreateSharedPanelSettings ----------------------------------

        [Test]
        [Description("targetDisplay=0 を要求すると PanelSettings.targetDisplay==0 で返る（Requirement 1.2）")]
        public void CreateSharedPanelSettings_RequestZero_ReturnsTargetDisplayZero()
        {
            var panelSettings = _builder.CreateSharedPanelSettings(0);
            Track(panelSettings);

            Assert.That(panelSettings, Is.Not.Null);
            Assert.That(panelSettings.targetDisplay, Is.EqualTo(0));
        }

        [Test]
        [Description("targetDisplay=0 では警告ログを出さない（無誤通知）")]
        public void CreateSharedPanelSettings_RequestZero_EmitsNoWarning()
        {
            Track(_builder.CreateSharedPanelSettings(0));

            Assert.That(_logger.Entries, Is.Empty);
        }

        [Test]
        [Description("targetDisplay=1 を要求すると 0 に強制され、警告ログが Lifecycle カテゴリで出る（Requirement 1.7）")]
        public void CreateSharedPanelSettings_RequestNonZero_ForcesZero_AndWarnsOnLifecycle()
        {
            var panelSettings = _builder.CreateSharedPanelSettings(1);
            Track(panelSettings);

            Assert.That(panelSettings.targetDisplay, Is.EqualTo(0),
                "Requirement 1.7: shell must never render to Display 2+");
            Assert.That(_logger.Entries.Count, Is.EqualTo(1));
            var entry = _logger.Entries[0];
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Category, Is.EqualTo(LogCategory.Lifecycle));
            Assert.That(entry.Message, Does.Contain("targetDisplay"));
            Assert.That(entry.Message, Does.Contain("1"));
        }

        [Test]
        [Description("負値や 2 以上の targetDisplay も同様に 0 に強制される（防御的網羅）")]
        [TestCase(-1)]
        [TestCase(2)]
        [TestCase(7)]
        public void CreateSharedPanelSettings_AnyNonZero_ForcesZero(int requested)
        {
            var panelSettings = _builder.CreateSharedPanelSettings(requested);
            Track(panelSettings);

            Assert.That(panelSettings.targetDisplay, Is.EqualTo(0));
            Assert.That(_logger.Entries.Count, Is.EqualTo(1),
                "Each non-zero request must produce exactly one warning");
        }

        [Test]
        [Description("呼び出すたびに新しい PanelSettings インスタンスが生成される（共有は呼出元の責務）")]
        public void CreateSharedPanelSettings_ReturnsFreshInstancePerCall()
        {
            var first = _builder.CreateSharedPanelSettings(0);
            Track(first);
            var second = _builder.CreateSharedPanelSettings(0);
            Track(second);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(first, Is.Not.SameAs(second));
        }

        [Test]
        [Description("PanelSettings.name が DefaultPanelSettingsName で初期化される（診断用の固定識別子）")]
        public void CreateSharedPanelSettings_AssignsExpectedName()
        {
            var panelSettings = _builder.CreateSharedPanelSettings(0);
            Track(panelSettings);

            Assert.That(panelSettings.name,
                Is.EqualTo(RootUiDocumentBuilder.DefaultPanelSettingsName));
        }

        // ---- Build (profile + panel settings) ---------------------------

        [Test]
        [Description("Build は null profile を拒否する")]
        public void Build_NullProfile_Throws()
        {
            var ps = _builder.CreateSharedPanelSettings(0);
            Track(ps);

            Assert.Throws<ArgumentNullException>(
                () => _builder.Build(null!, ps));
        }

        [Test]
        [Description("Build は null panelSettings を拒否する")]
        public void Build_NullPanelSettings_Throws()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);

            Assert.Throws<ArgumentNullException>(
                () => _builder.Build(profile, (PanelSettings)null!));
        }

        [Test]
        [Description("Build は RootVisualTreeAsset 未設定の profile を拒否する（Requirement 6.8 既定スキン同梱前提）")]
        public void Build_ProfileMissingRootVta_Throws()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            Track(profile);
            var ps = _builder.CreateSharedPanelSettings(0);
            Track(ps);

            Assert.Throws<ArgumentException>(
                () => _builder.Build(profile, ps));
        }

        [Test]
        [Description("Build は受け取った PanelSettings をそのまま BuildResult に返す（共有契約）")]
        public void Build_ReturnsSamePanelSettings()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);
            var ps = _builder.CreateSharedPanelSettings(0);
            Track(ps);

            var result = _builder.Build(profile, ps);
            TrackResult(result);

            Assert.That(result.PanelSettings, Is.SameAs(ps));
        }

        [Test]
        [Description("Build は HostGameObject を生成し、UIDocument コンポーネントを付与する（Requirement 1.1）")]
        public void Build_CreatesHostGameObjectWithUIDocument()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);
            var ps = _builder.CreateSharedPanelSettings(0);
            Track(ps);

            var result = _builder.Build(profile, ps);
            TrackResult(result);

            Assert.That(result.HostGameObject, Is.Not.Null);
            Assert.That(result.HostGameObject.name,
                Is.EqualTo(RootUiDocumentBuilder.DefaultRootGameObjectName));
            Assert.That(result.UIDocument, Is.Not.Null);
            Assert.That(result.UIDocument.gameObject, Is.SameAs(result.HostGameObject));
        }

        [Test]
        [Description("HostGameObject は HideAndDontSave で生成される（シーン汚染しない）")]
        public void Build_HostGameObject_HasHideAndDontSaveFlags()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);
            var ps = _builder.CreateSharedPanelSettings(0);
            Track(ps);

            var result = _builder.Build(profile, ps);
            TrackResult(result);

            Assert.That(result.HostGameObject.hideFlags,
                Is.EqualTo(HideFlags.HideAndDontSave));
        }

        [Test]
        [Description("UIDocument.panelSettings は共有 PanelSettings に設定される（Requirement 1.2）")]
        public void Build_AssignsPanelSettingsToUIDocument()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);
            var ps = _builder.CreateSharedPanelSettings(0);
            Track(ps);

            var result = _builder.Build(profile, ps);
            TrackResult(result);

            Assert.That(result.UIDocument.panelSettings, Is.SameAs(ps));
        }

        [Test]
        [Description("UIDocument.visualTreeAsset は profile.RootVisualTreeAsset に設定される（Requirement 6.4）")]
        public void Build_AssignsRootVisualTreeAssetToUIDocument()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);
            var ps = _builder.CreateSharedPanelSettings(0);
            Track(ps);

            var result = _builder.Build(profile, ps);
            TrackResult(result);

            Assert.That(result.UIDocument.visualTreeAsset,
                Is.SameAs(profile.RootVisualTreeAsset));
        }

        [Test]
        [Description("Build(profile, requestedTargetDisplay) オーバーロードでも targetDisplay は 0 に強制 + 警告（Requirement 1.7）")]
        public void Build_WithRequestedDisplayOverload_ForcesZero_AndWarns()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);

            var result = _builder.Build(profile, requestedTargetDisplay: 3);
            TrackResult(result);

            Assert.That(result.PanelSettings.targetDisplay, Is.EqualTo(0));
            Assert.That(_logger.Entries.Count, Is.EqualTo(1));
            Assert.That(_logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(_logger.Entries[0].Category, Is.EqualTo(LogCategory.Lifecycle));
        }

        [Test]
        [Description("Build(profile, requestedTargetDisplay=0) は警告を出さない")]
        public void Build_WithRequestedDisplayOverload_Zero_EmitsNoWarning()
        {
            var profile = CreateProfileWithRootVta();
            Track(profile);

            var result = _builder.Build(profile, requestedTargetDisplay: 0);
            TrackResult(result);

            Assert.That(_logger.Entries, Is.Empty);
        }

        // ---- TabBar.uxml structural shape (Requirement 1.3) -------------

        [Test]
        [Description("TabBar.uxml が Runtime.UxmlUss/ 配下にロードできる（既定スキン同梱; Requirement 6.8）")]
        public void TabBarUxml_IsLoadableFromPackage()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TabBarUxmlPath);
            Assert.That(vta, Is.Not.Null,
                $"TabBar.uxml must be reachable at '{TabBarUxmlPath}' for the bundled default skin");
        }

        [Test]
        [Description("TabBar.uxml をクローンするとタブバー領域・タブコンテンツ領域・通知バー領域が同じツリーに揃う（Requirement 1.3）")]
        public void TabBarUxml_Instantiated_ContainsTabBarAndTabContentAndNotificationBar()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TabBarUxmlPath);
            Assume.That(vta, Is.Not.Null);

            var instance = vta.Instantiate();
            try
            {
                Assert.That(FindElementByClass(instance, SkinValidationRules.Root.TabBar),
                    Is.Not.Null,
                    "TabBar.uxml must declare an element with class 'vsb-tab-bar'");
                Assert.That(FindElementByClass(instance, "vsb-tab-content"),
                    Is.Not.Null,
                    "TabBar.uxml must declare a tab content area with class 'vsb-tab-content'");
                Assert.That(FindElementByClass(instance, SkinValidationRules.Root.NotificationBar),
                    Is.Not.Null,
                    "TabBar.uxml must declare an element with class 'vsb-notification-bar'");
            }
            finally
            {
                instance.RemoveFromHierarchy();
            }
        }

        [Test]
        [Description("TabBar.uxml には 3 個の vsb-tab-bar__button が並ぶ（Requirement 2.1 / 2.2 の前提）")]
        public void TabBarUxml_Instantiated_HasThreeTabBarButtons()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TabBarUxmlPath);
            Assume.That(vta, Is.Not.Null);

            var instance = vta.Instantiate();
            try
            {
                var buttons = CollectElementsByClass(instance, SkinValidationRules.Root.TabBarButton);
                Assert.That(buttons.Count, Is.EqualTo(3),
                    "Three tab buttons (Character / StageLighting / CameraSwitcher) are expected");
            }
            finally
            {
                instance.RemoveFromHierarchy();
            }
        }

        [Test]
        [Description("EmptyTabShell.uxml が Runtime.UxmlUss/ 配下にロードでき、vsb-tab-root クラスを保持する（Requirement 10.2）")]
        public void EmptyTabShellUxml_IsLoadable_AndExposesVsbTabRootClass()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EmptyTabShellUxmlPath);
            Assert.That(vta, Is.Not.Null,
                $"EmptyTabShell.uxml must be reachable at '{EmptyTabShellUxmlPath}'");

            var instance = vta.Instantiate();
            try
            {
                Assert.That(FindElementByClass(instance, "vsb-tab-root"),
                    Is.Not.Null,
                    "EmptyTabShell.uxml must declare an element with class 'vsb-tab-root'");
            }
            finally
            {
                instance.RemoveFromHierarchy();
            }
        }

        [Test]
        [Description("NotificationBar.uxml が Runtime.UxmlUss/ 配下にロードでき、vsb-notification-bar クラスを保持する（task 8.1）")]
        public void NotificationBarUxml_IsLoadable_AndExposesNotificationBarClass()
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(NotificationBarUxmlPath);
            Assert.That(vta, Is.Not.Null,
                $"NotificationBar.uxml must be reachable at '{NotificationBarUxmlPath}'");

            var instance = vta.Instantiate();
            try
            {
                Assert.That(FindElementByClass(instance, SkinValidationRules.Root.NotificationBar),
                    Is.Not.Null,
                    "NotificationBar.uxml must declare an element with class 'vsb-notification-bar'");
            }
            finally
            {
                instance.RemoveFromHierarchy();
            }
        }

        // ----- helpers ---------------------------------------------------

        private void Track(UnityEngine.Object obj)
        {
            if (obj != null)
            {
                _disposables.Add(obj);
            }
        }

        private void TrackResult(RootUiDocumentBuildResult result)
        {
            // PanelSettings はテスト本体で個別に Track 済み (overload では Track 漏れがあるためここで再保険)。
            Track(result.PanelSettings);
            Track(result.HostGameObject);
        }

        private UiToolkitShellSkinProfile CreateProfileWithRootVta()
        {
            var profile = ScriptableObject.CreateInstance<UiToolkitShellSkinProfile>();
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TabBarUxmlPath);
            if (vta == null)
            {
                // Fallback when AssetDatabase has not yet imported the new file
                // (the same in-memory VTA is enough for property-assignment tests).
                vta = ScriptableObject.CreateInstance<VisualTreeAsset>();
                Track(vta);
            }
            profile.RootVisualTreeAsset = vta;
            return profile;
        }

        private static VisualElement? FindElementByClass(VisualElement root, string className)
        {
            if (root.ClassListContains(className)) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindElementByClass(root[i], className);
                if (found != null) return found;
            }
            return null;
        }

        private static List<VisualElement> CollectElementsByClass(VisualElement root, string className)
        {
            var sink = new List<VisualElement>();
            CollectElementsByClassRecursive(root, className, sink);
            return sink;
        }

        private static void CollectElementsByClassRecursive(
            VisualElement element, string className, List<VisualElement> sink)
        {
            if (element.ClassListContains(className))
            {
                sink.Add(element);
            }
            for (var i = 0; i < element.childCount; i++)
            {
                CollectElementsByClassRecursive(element[i], className, sink);
            }
        }
    }
}
