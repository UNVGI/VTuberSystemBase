#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 6.3: <see cref="SkinValidator"/> contract tests. Pin the
    /// <c>ISkinValidator.Validate</c> shape (root + per-tab selector walk),
    /// the <see cref="SkinValidationReport"/> aggregation contract, the
    /// "no side effects on UIDocument state" invariant, and the
    /// <see cref="LogCategory.Skin"/> emission rule (design.md §Skin
    /// §SkinValidator; Requirement 6.1, 6.2, 6.5, 6.6).
    /// </summary>
    [TestFixture]
    public sealed class SkinValidatorTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private SkinValidator _validator = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _validator = new SkinValidator(_logger);
        }

        [Test]
        [Description("コンストラクタは null logger を拒否する（DI 契約）")]
        public void Constructor_NullLogger_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SkinValidator(null!));
        }

        [Test]
        [Description("Validate は null rootPanel を拒否する（前提条件: OnEnable 完了済み rootVisualElement）")]
        public void Validate_NullRoot_Throws()
        {
            var tabs = BuildAllValidTabRoots();
            Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, tabs));
        }

        [Test]
        [Description("Validate は null tabRoots 辞書を拒否する（前提条件: 呼出し元が辞書を構築する）")]
        public void Validate_NullTabRoots_Throws()
        {
            var root = BuildValidRootPanel();
            Assert.Throws<ArgumentNullException>(() => _validator.Validate(root, null!));
        }

        [Test]
        [Description("ルート + 全タブが必須クラスを揃えていれば AllValid==true / Issues==空（design.md §SkinValidator 正常パス）")]
        public void Validate_AllRequiredClassesPresent_ReturnsAllValidTrue()
        {
            var rootPanel = BuildValidRootPanel();
            var tabRoots = BuildAllValidTabRoots();

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.True);
            Assert.That(report.Issues, Is.Empty);
        }

        [Test]
        [Description("正常パスでは DiagnosticsLogger にエントリが残らない（規約違反のみログ出力）")]
        public void Validate_NoIssues_DoesNotEmitLog()
        {
            var rootPanel = BuildValidRootPanel();
            var tabRoots = BuildAllValidTabRoots();

            _validator.Validate(rootPanel, tabRoots);

            Assert.That(_logger.Entries, Is.Empty);
        }

        [Test]
        [Description("Root に必須クラス（vsb-tab-bar）が無い場合、TabId==null の Issue として報告される（規約違反検出 task 6.3 観測可能な完了状態）")]
        public void Validate_RootMissingTabBar_AppendsIssueWithNullTabId()
        {
            var rootPanel = new VisualElement();
            // Intentionally omit vsb-tab-bar
            var btn = new VisualElement();
            btn.AddToClassList(SkinValidationRules.Root.TabBarButton);
            rootPanel.Add(btn);
            var notif = new VisualElement();
            notif.AddToClassList(SkinValidationRules.Root.NotificationBar);
            rootPanel.Add(notif);

            var tabRoots = BuildAllValidTabRoots();

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.False);
            Assert.That(report.Issues.Count, Is.EqualTo(1));
            Assert.That(report.Issues[0].TabId, Is.Null,
                "Root-level issues use TabId == null per design.md §SkinValidationIssue");
            Assert.That(report.Issues[0].MissingSelector,
                Is.EqualTo(SkinValidationRules.Root.TabBar));
        }

        [Test]
        [Description("特定タブの必須 modifier 欠落時、該当 TabId を Issue.TabId に持つ Issue が返る（task 6.3 観測可能な完了状態）")]
        public void Validate_TabMissingRequiredModifier_AppendsIssueWithThatTabId()
        {
            var rootPanel = BuildValidRootPanel();
            var tabRoots = new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, BuildTabRootMissingModifier(TabId.Character) },
                { TabId.StageLighting, BuildValidTabRoot(TabId.StageLighting) },
                { TabId.CameraSwitcher, BuildValidTabRoot(TabId.CameraSwitcher) },
            };

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.False);
            var characterIssue = report.Issues.Single();
            Assert.That(characterIssue.TabId, Is.EqualTo(TabId.Character));
            Assert.That(characterIssue.MissingSelector,
                Is.EqualTo(SkinValidationRules.CharacterTab.TabRootModifier));
        }

        [Test]
        [Description("複数タブ + ルートが同時に違反した場合、各 Issue にそれぞれの TabId が紐付く（縮退検出の網羅性）")]
        public void Validate_MultipleScopesMissingClasses_AggregatesIssuesPerScope()
        {
            var rootPanel = new VisualElement();
            // Root: omit everything → 3 root-level issues
            var tabRoots = new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, BuildTabRootMissingModifier(TabId.Character) },
                { TabId.StageLighting, BuildTabRootMissingModifier(TabId.StageLighting) },
                { TabId.CameraSwitcher, BuildValidTabRoot(TabId.CameraSwitcher) },
            };

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.False);
            // 3 root + 1 Character + 1 StageLighting = 5
            Assert.That(report.Issues.Count, Is.EqualTo(5));

            var byScope = report.Issues.ToLookup(i => i.TabId);
            Assert.That(byScope[null].Count(), Is.EqualTo(3),
                "Three root selectors are required and all are missing here");
            Assert.That(byScope[TabId.Character].Count(), Is.EqualTo(1));
            Assert.That(byScope[TabId.StageLighting].Count(), Is.EqualTo(1));
            Assert.That(byScope[TabId.CameraSwitcher].Count(), Is.EqualTo(0));
        }

        [Test]
        [Description("Validator は副作用として UIDocument の状態を変更しない（design.md §SkinValidator Invariants）")]
        public void Validate_DoesNotMutateInputs()
        {
            var rootPanel = BuildValidRootPanel();
            var tabRoots = BuildAllValidTabRoots();

            var rootClassesBefore = SnapshotClassListsRecursive(rootPanel);
            var characterClassesBefore = SnapshotClassListsRecursive(tabRoots[TabId.Character]);

            _validator.Validate(rootPanel, tabRoots);

            Assert.That(SnapshotClassListsRecursive(rootPanel), Is.EqualTo(rootClassesBefore),
                "Validator must not mutate root panel class lists");
            Assert.That(SnapshotClassListsRecursive(tabRoots[TabId.Character]),
                Is.EqualTo(characterClassesBefore),
                "Validator must not mutate Character tab class lists");
        }

        [Test]
        [Description("失敗時に DiagnosticsLogger.Log が LogLevel.Error / LogCategory.Skin で発火する（task 6.3 ログが Skin カテゴリで残る）")]
        public void Validate_LogsEachIssueAtErrorLevel_WithSkinCategory()
        {
            var rootPanel = new VisualElement();
            var tabRoots = BuildAllValidTabRoots();

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.False);
            Assert.That(_logger.Entries.Count, Is.EqualTo(report.Issues.Count));
            foreach (var entry in _logger.Entries)
            {
                Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
                Assert.That(entry.Category, Is.EqualTo(LogCategory.Skin));
            }
        }

        [Test]
        [Description("ログメッセージに Issue.MissingSelector が含まれる（運用者がログだけで欠落セレクタを特定できる）")]
        public void Validate_LogMessage_ContainsMissingSelector()
        {
            var rootPanel = new VisualElement();
            var tabRoots = BuildAllValidTabRoots();

            _validator.Validate(rootPanel, tabRoots);

            Assert.That(_logger.Entries, Is.Not.Empty);
            foreach (var className in SkinValidationRules.RequiredRootClasses)
            {
                Assert.That(
                    _logger.Entries.Any(e => e.Message.Contains(className)),
                    Is.True,
                    $"Expected log message to surface missing selector '{className}'");
            }
        }

        [Test]
        [Description("default(SkinValidationReport) は AllValid==true / Issues==空コレクションとして安全に扱える")]
        public void DefaultReport_IsTreatedAsValidAndExposesEmptyIssues()
        {
            var report = default(SkinValidationReport);
            Assert.That(report.AllValid, Is.True);
            Assert.That(report.Issues, Is.Not.Null);
            Assert.That(report.Issues, Is.Empty);
        }

        [Test]
        [Description("ISkinValidator が公開インタフェースとして存在し、SkinValidator が実装している（design.md §SkinValidator Service Interface）")]
        public void SkinValidator_Implements_ISkinValidator()
        {
            Assert.That(_validator, Is.InstanceOf<ISkinValidator>());
        }

        [Test]
        [Description("ルートは ClassListContains（自身に付与）でも検出される（rootVisualElement 自身が必須クラスを持つケースを許容）")]
        public void Validate_RootSelector_OnRootElementItself_IsDetected()
        {
            var rootPanel = new VisualElement();
            rootPanel.AddToClassList(SkinValidationRules.Root.TabBar);
            rootPanel.AddToClassList(SkinValidationRules.Root.TabBarButton);
            rootPanel.AddToClassList(SkinValidationRules.Root.NotificationBar);
            var tabRoots = BuildAllValidTabRoots();

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.True);
        }

        // ----- task 12.4: UXML-driven必須クラス欠落検出テスト -----------------

        private const string CompleteUxmlPath =
            "Packages/com.hidano.vtuber-system-base.ui-toolkit-shell/Tests/Runtime/Fixtures/SkinValidator_Complete.uxml";

        private const string MissingRequiredUxmlPath =
            "Packages/com.hidano.vtuber-system-base.ui-toolkit-shell/Tests/Runtime/Fixtures/SkinValidator_MissingRequired.uxml";

        [Test]
        [Description("task 12.4: 全クラスが揃った UXML をクローンして Validate するとAllValid==true / Issues==空 (Requirement 6.5, 6.6)")]
        public void Validate_CompleteUxmlFixture_ReturnsAllValidWithEmptyIssues()
        {
            using var tree = LoadFixtureTree(CompleteUxmlPath);
            var (rootPanel, tabRoots) = ExtractShellRoots(tree.Instance);

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.True,
                "Complete fixture must satisfy every required selector");
            Assert.That(report.Issues, Is.Empty);
            Assert.That(_logger.Entries, Is.Empty,
                "正常パスでは Skin カテゴリのエラーログを出さない");
        }

        [Test]
        [Description("task 12.4: 必須クラスを欠落させた UXML は AllValid==false かつ欠落セレクタを Report に含める (Requirement 6.5, 6.6)")]
        public void Validate_MissingRequiredUxmlFixture_ReportsMissingSelectors()
        {
            using var tree = LoadFixtureTree(MissingRequiredUxmlPath);
            var (rootPanel, tabRoots) = ExtractShellRoots(tree.Instance);

            var report = _validator.Validate(rootPanel, tabRoots);

            Assert.That(report.AllValid, Is.False);
            // Root: vsb-notification-bar 欠落 1 件 + Character: modifier 欠落 1 件 = 2 件
            Assert.That(report.Issues.Count, Is.EqualTo(2));

            var rootIssue = report.Issues.Single(i => i.TabId is null);
            Assert.That(rootIssue.MissingSelector,
                Is.EqualTo(SkinValidationRules.Root.NotificationBar),
                "ルート欠落 Issue は TabId==null かつ MissingSelector が vsb-notification-bar");

            var characterIssue = report.Issues.Single(i => i.TabId == TabId.Character);
            Assert.That(characterIssue.MissingSelector,
                Is.EqualTo(SkinValidationRules.CharacterTab.TabRootModifier),
                "Character タブ欠落 Issue は TabId==Character かつ modifier セレクタ");

            Assert.That(report.Issues.Any(i => i.TabId == TabId.StageLighting), Is.False,
                "完備な StageLighting タブには Issue を生成しない");
            Assert.That(report.Issues.Any(i => i.TabId == TabId.CameraSwitcher), Is.False,
                "完備な CameraSwitcher タブには Issue を生成しない");
        }

        [Test]
        [Description("task 12.4: 2 パターンの UXML で Report が差分化する (AllValid と Issues.Count が反転)")]
        public void Validate_CompleteVsMissingUxml_ProducesDistinguishableReports()
        {
            SkinValidationReport completeReport;
            SkinValidationReport missingReport;

            using (var completeTree = LoadFixtureTree(CompleteUxmlPath))
            {
                var (root, tabs) = ExtractShellRoots(completeTree.Instance);
                completeReport = _validator.Validate(root, tabs);
            }

            // Reset logger between fixtures so log assertions reflect only the failure case.
            _logger = new RecordingDiagnosticsLogger();
            _validator = new SkinValidator(_logger);

            using (var missingTree = LoadFixtureTree(MissingRequiredUxmlPath))
            {
                var (root, tabs) = ExtractShellRoots(missingTree.Instance);
                missingReport = _validator.Validate(root, tabs);
            }

            Assert.That(completeReport.AllValid, Is.True);
            Assert.That(missingReport.AllValid, Is.False);
            Assert.That(completeReport.Issues.Count, Is.EqualTo(0));
            Assert.That(missingReport.Issues.Count, Is.GreaterThan(0));
            Assert.That(_logger.Entries.Count, Is.EqualTo(missingReport.Issues.Count),
                "欠落 UXML の Validate 1 回につき Issue 件数と等しいログが Skin カテゴリで残る");
            Assert.That(_logger.Entries.All(e => e.Category == LogCategory.Skin), Is.True);
        }

        // ----- UXML fixture helpers ---------------------------------------

        private readonly struct LoadedFixtureTree : IDisposable
        {
            public LoadedFixtureTree(VisualElement instance)
            {
                Instance = instance;
            }

            public VisualElement Instance { get; }

            public void Dispose()
            {
                Instance?.RemoveFromHierarchy();
            }
        }

        private static LoadedFixtureTree LoadFixtureTree(string path)
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assume.That(vta, Is.Not.Null,
                $"Fixture UXML must be reachable at '{path}' (task 12.4)");
            var instance = vta.Instantiate();
            return new LoadedFixtureTree(instance);
        }

        private static (VisualElement rootPanel, IReadOnlyDictionary<TabId, VisualElement> tabRoots)
            ExtractShellRoots(VisualElement instance)
        {
            var rootPanel = instance.Q<VisualElement>("root-panel");
            Assume.That(rootPanel, Is.Not.Null,
                "Fixture UXML must declare an element named 'root-panel'");

            var character = instance.Q<VisualElement>("tab-root-character");
            var stage = instance.Q<VisualElement>("tab-root-stage-lighting");
            var camera = instance.Q<VisualElement>("tab-root-camera-switcher");
            Assume.That(character, Is.Not.Null,
                "Fixture UXML must declare an element named 'tab-root-character'");
            Assume.That(stage, Is.Not.Null,
                "Fixture UXML must declare an element named 'tab-root-stage-lighting'");
            Assume.That(camera, Is.Not.Null,
                "Fixture UXML must declare an element named 'tab-root-camera-switcher'");

            var tabRoots = new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, character! },
                { TabId.StageLighting, stage! },
                { TabId.CameraSwitcher, camera! },
            };
            return (rootPanel!, tabRoots);
        }

        // ----- helpers ----------------------------------------------------

        private static VisualElement BuildValidRootPanel()
        {
            var root = new VisualElement();
            var tabBar = new VisualElement();
            tabBar.AddToClassList(SkinValidationRules.Root.TabBar);
            var btn = new VisualElement();
            btn.AddToClassList(SkinValidationRules.Root.TabBarButton);
            tabBar.Add(btn);
            root.Add(tabBar);
            var notif = new VisualElement();
            notif.AddToClassList(SkinValidationRules.Root.NotificationBar);
            root.Add(notif);
            return root;
        }

        private static IReadOnlyDictionary<TabId, VisualElement> BuildAllValidTabRoots()
        {
            return new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, BuildValidTabRoot(TabId.Character) },
                { TabId.StageLighting, BuildValidTabRoot(TabId.StageLighting) },
                { TabId.CameraSwitcher, BuildValidTabRoot(TabId.CameraSwitcher) },
            };
        }

        private static VisualElement BuildValidTabRoot(TabId tabId)
        {
            var root = new VisualElement();
            foreach (var className in SkinValidationRules.RequiredTabClassesFor(tabId))
            {
                root.AddToClassList(className);
            }
            return root;
        }

        private static VisualElement BuildTabRootMissingModifier(TabId tabId)
        {
            var root = new VisualElement();
            // Add only the shared TabRoot (vsb-tab-root) and intentionally omit the
            // tab-identifying modifier (vsb-tab-root--character etc.).
            root.AddToClassList("vsb-tab-root");
            return root;
        }

        private static List<string> SnapshotClassListsRecursive(VisualElement element)
        {
            var snapshot = new List<string>();
            CollectClassesRecursive(element, snapshot);
            return snapshot;
        }

        private static void CollectClassesRecursive(VisualElement element, List<string> sink)
        {
            sink.Add("[" + string.Join(",", element.GetClasses()) + "]");
            for (int i = 0; i < element.childCount; i++)
            {
                CollectClassesRecursive(element[i], sink);
            }
        }
    }
}
