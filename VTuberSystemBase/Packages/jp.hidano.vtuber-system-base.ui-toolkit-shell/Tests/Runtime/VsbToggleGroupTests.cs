#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 7.4: <see cref="VsbToggleGroup"/> contract tests.
    /// <see cref="VsbControlBase"/> の <c>vsb-</c> プレフィクス登録、
    /// <see cref="VsbToggleGroup.keys"/> UxmlAttribute（カンマ区切り）からの
    /// option 要素生成、<see cref="VsbToggleGroup.Select(string)"/> による
    /// <see cref="VsbToggleGroup.SelectionChanged"/> の排他発火、
    /// 必須 USS セレクタ（<c>vsb-toggle-group</c>,
    /// <c>vsb-toggle-group__option</c>,
    /// <c>vsb-toggle-group__option--selected</c>）の付与
    /// （design.md §CommonUi §VsbToggleGroup; Requirement 7.1, 7.2, 7.3, 7.4, 7.7）。
    /// </summary>
    [TestFixture]
    public sealed class VsbToggleGroupTests
    {
        // ---------- Class prefix / required selectors (Requirement 6.2 / 7.3) ----------

        [Test]
        [Description("インスタンス化直後にブロッククラス vsb-toggle-group が付与されている（vsb- プレフィクス契約）")]
        public void Constructor_AppliesVsbToggleGroupClass()
        {
            var group = new VsbToggleGroup();

            Assert.That(group.ClassListContains("vsb-toggle-group"), Is.True);
        }

        [Test]
        [Description("既定状態ではキーは空・選択は null・option 子要素も存在しない（UXML 属性未指定の初期状態）")]
        public void DefaultState_HasNoKeysAndNoSelection()
        {
            var group = new VsbToggleGroup();

            Assert.That(group.Keys, Is.Empty);
            Assert.That(group.selectedKey, Is.Null);
            Assert.That(group.Query<VisualElement>(className: "vsb-toggle-group__option").ToList(), Is.Empty);
        }

        // ---------- DiagnosticsLogger DI（VsbControlBase 共通契約） ----------

        [Test]
        [Description("コンストラクタで IDiagnosticsLogger を受け取れる（VsbControlBase 共通の DI 集約点）")]
        public void DiagnosticsLogger_CanBeInjectedViaConstructor()
        {
            var logger = new RecordingDiagnosticsLogger();

            var group = new VsbToggleGroup(logger);

            Assert.That(group.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        [Test]
        [Description("UXML から default ctor で生成された後でも SetDiagnosticsLogger で注入できる（UxmlFactory 経路の DI）")]
        public void SetDiagnosticsLogger_AfterDefaultCtor_AssignsLogger()
        {
            var group = new VsbToggleGroup();
            var logger = new RecordingDiagnosticsLogger();

            group.SetDiagnosticsLogger(logger);

            Assert.That(group.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        // ---------- Keys UxmlAttribute parsing ----------

        [Test]
        [Description("keys にカンマ区切り文字列を渡すと option 子要素が同数生成され、必須 BEM クラスが付与される")]
        public void Keys_CommaSeparated_BuildsOptionsWithBemClass()
        {
            var group = new VsbToggleGroup();

            group.keys = "alpha,bravo,charlie";

            Assert.That(group.Keys, Is.EqualTo(new[] { "alpha", "bravo", "charlie" }));
            var options = group.Query<VisualElement>(className: "vsb-toggle-group__option").ToList();
            Assert.That(options.Count, Is.EqualTo(3),
                "vsb-toggle-group__option BEM クラスを持つ要素がキー数だけ生成される");
            Assert.That(options[0].name, Is.EqualTo("alpha"));
            Assert.That(options[1].name, Is.EqualTo("bravo"));
            Assert.That(options[2].name, Is.EqualTo("charlie"));
        }

        [Test]
        [Description("keys の各要素の前後空白はトリムされ、空文字列要素は無視される（UXML での書式ゆらぎ吸収）")]
        public void Keys_TrimsWhitespaceAndDropsEmptyEntries()
        {
            var group = new VsbToggleGroup();

            group.keys = " alpha ,, bravo , ";

            Assert.That(group.Keys, Is.EqualTo(new[] { "alpha", "bravo" }));
        }

        [Test]
        [Description("keys 内の重複は最初の出現のみ採用され option も 1 個だけ生成される")]
        public void Keys_DuplicatesAreCollapsed()
        {
            var group = new VsbToggleGroup();

            group.keys = "alpha,bravo,alpha";

            Assert.That(group.Keys, Is.EqualTo(new[] { "alpha", "bravo" }));
            var options = group.Query<VisualElement>(className: "vsb-toggle-group__option").ToList();
            Assert.That(options.Count, Is.EqualTo(2));
        }

        [Test]
        [Description("keys を空文字列で再代入すると既存 option が全て破棄される（UXML 駆動の再構築経路）")]
        public void Keys_ReassignedToEmpty_RemovesAllOptions()
        {
            var group = new VsbToggleGroup { keys = "alpha,bravo" };

            group.keys = string.Empty;

            Assert.That(group.Keys, Is.Empty);
            Assert.That(group.Query<VisualElement>(className: "vsb-toggle-group__option").ToList(),
                Is.Empty);
        }

        // ---------- Select / SelectionChanged ----------

        [Test]
        [Description("Select(key) は SelectionChanged(key) を 1 回発火し、selectedKey と selected モディファイアを更新する（Requirement 7.4）")]
        public void Select_ExistingKey_FiresSelectionChangedOnce()
        {
            var group = new VsbToggleGroup { keys = "alpha,bravo" };
            var observed = new List<string>();
            group.SelectionChanged += observed.Add;

            group.Select("alpha");

            Assert.That(observed, Is.EqualTo(new[] { "alpha" }));
            Assert.That(group.selectedKey, Is.EqualTo("alpha"));

            var selectedNodes = group.Query<VisualElement>(className: "vsb-toggle-group__option--selected").ToList();
            Assert.That(selectedNodes.Count, Is.EqualTo(1));
            Assert.That(selectedNodes[0].name, Is.EqualTo("alpha"));
        }

        [Test]
        [Description("Select(同じ key) は再度 SelectionChanged を発火しない（idempotency 契約）")]
        public void Select_SameKeyTwice_FiresSelectionChangedOnlyOnce()
        {
            var group = new VsbToggleGroup { keys = "alpha,bravo" };
            var observed = new List<string>();
            group.SelectionChanged += observed.Add;

            group.Select("alpha");
            group.Select("alpha");

            Assert.That(observed, Is.EqualTo(new[] { "alpha" }));
        }

        [Test]
        [Description("2 個以上の Key を持つグループで Select を切り替えると排他選択になる（同時選択不可。tasks.md 7.4 観測可能な完了状態）")]
        public void Select_SwitchingKeys_IsMutuallyExclusive()
        {
            var group = new VsbToggleGroup { keys = "alpha,bravo,charlie" };
            var observed = new List<string>();
            group.SelectionChanged += observed.Add;

            group.Select("alpha");
            group.Select("bravo");
            group.Select("charlie");

            Assert.That(observed, Is.EqualTo(new[] { "alpha", "bravo", "charlie" }));
            Assert.That(group.selectedKey, Is.EqualTo("charlie"));

            var selectedNodes = group.Query<VisualElement>(className: "vsb-toggle-group__option--selected").ToList();
            Assert.That(selectedNodes.Count, Is.EqualTo(1),
                "排他選択: --selected モディファイアは常に 1 個の option にのみ付与される");
            Assert.That(selectedNodes[0].name, Is.EqualTo("charlie"),
                "最後に Select した key が排他的に選択された状態になる");
        }

        [Test]
        [Description("Select(未登録 key) は ArgumentException を投げ、UI クラッシュではなく明示的に失敗する（Requirement 5.9 準拠の例外契約）")]
        public void Select_UnknownKey_Throws()
        {
            var group = new VsbToggleGroup { keys = "alpha,bravo" };

            Assert.That(() => group.Select("unknown"), Throws.ArgumentException);
        }

        [Test]
        [Description("Select(null) は ArgumentNullException を投げる")]
        public void Select_NullKey_Throws()
        {
            var group = new VsbToggleGroup { keys = "alpha" };

            Assert.That(() => group.Select(null!), Throws.ArgumentNullException);
        }

        [Test]
        [Description("選択中に keys を再設定して以前の key が含まれない場合、SelectionChanged を発火せずに選択が解除される（UXML 再初期化経路の偽陽性回避）")]
        public void Keys_Reassigned_DropsSelectionWithoutEvent_IfKeyMissing()
        {
            var group = new VsbToggleGroup { keys = "alpha,bravo" };
            group.Select("alpha");
            var observed = new List<string>();
            group.SelectionChanged += observed.Add;

            group.keys = "charlie,delta";

            Assert.That(observed, Is.Empty,
                "keys 再設定は UXML 駆動の初期化と同等であり SelectionChanged を発火しない");
            Assert.That(group.selectedKey, Is.Null,
                "以前の選択 key が新しい keys に含まれないため選択は解除される");
            Assert.That(group.Query<VisualElement>(className: "vsb-toggle-group__option--selected").ToList(),
                Is.Empty);
        }

        [Test]
        [Description("選択中に keys を再設定して以前の key がまだ含まれていれば、選択状態と --selected モディファイアが維持される")]
        public void Keys_Reassigned_PreservesSelection_IfKeyStillPresent()
        {
            var group = new VsbToggleGroup { keys = "alpha,bravo" };
            group.Select("bravo");
            var observed = new List<string>();
            group.SelectionChanged += observed.Add;

            group.keys = "alpha,bravo,charlie";

            Assert.That(observed, Is.Empty);
            Assert.That(group.selectedKey, Is.EqualTo("bravo"));
            var selectedNodes = group.Query<VisualElement>(className: "vsb-toggle-group__option--selected").ToList();
            Assert.That(selectedNodes.Count, Is.EqualTo(1));
            Assert.That(selectedNodes[0].name, Is.EqualTo("bravo"));
        }
    }
}
