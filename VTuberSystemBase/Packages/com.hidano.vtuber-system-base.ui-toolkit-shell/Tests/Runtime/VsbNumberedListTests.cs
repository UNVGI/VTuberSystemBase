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
    /// Task 7.3: <see cref="VsbNumberedList"/> contract tests.
    /// <see cref="VsbControlBase"/> の <c>vsb-</c> プレフィクス登録、
    /// <see cref="VsbNumberedList.AddItem(VisualElement)"/> /
    /// <see cref="VsbNumberedList.RemoveAt(int)"/> /
    /// <see cref="VsbNumberedList.Reorder(int, int)"/> による
    /// <see cref="VsbNumberedList.ItemAdded"/> /
    /// <see cref="VsbNumberedList.ItemRemoved"/> /
    /// <see cref="VsbNumberedList.ItemReordered"/> の発火、自動採番の維持、
    /// 必須 USS セレクタ（<c>vsb-numbered-list</c>,
    /// <c>vsb-numbered-list__item</c>, <c>vsb-numbered-list__index</c>,
    /// <c>vsb-numbered-list__content</c>）の付与
    /// （design.md §CommonUi §VsbNumberedList; Requirement 7.1, 7.2, 7.3, 7.4, 7.7）。
    /// </summary>
    [TestFixture]
    public sealed class VsbNumberedListTests
    {
        // ---------- Class prefix / required selectors (Requirement 6.2 / 7.3) ----------

        [Test]
        [Description("インスタンス化直後にブロッククラス vsb-numbered-list が付与されている（vsb- プレフィクス契約）")]
        public void Constructor_AppliesVsbNumberedListClass()
        {
            var list = new VsbNumberedList();

            Assert.That(list.ClassListContains("vsb-numbered-list"), Is.True);
        }

        [Test]
        [Description("AddItem 後に行コンテナ・index バッジ・content スロットの BEM 必須クラスが揃う（design.md USS セレクタ規約）")]
        public void AddItem_BuildsItemContainerWithBemClasses()
        {
            var list = new VsbNumberedList();

            list.AddItem(new VisualElement());

            Assert.That(list.Q<VisualElement>(className: "vsb-numbered-list__item"), Is.Not.Null,
                "vsb-numbered-list__item 行コンテナが見つからない");
            Assert.That(list.Q<Label>(className: "vsb-numbered-list__index"), Is.Not.Null,
                "vsb-numbered-list__index 番号バッジが見つからない");
            Assert.That(list.Q<VisualElement>(className: "vsb-numbered-list__content"), Is.Not.Null,
                "vsb-numbered-list__content スロットが見つからない");
        }

        // ---------- DiagnosticsLogger DI（VsbControlBase 共通契約） ----------

        [Test]
        [Description("コンストラクタで IDiagnosticsLogger を受け取れる（VsbControlBase 共通の DI 集約点）")]
        public void DiagnosticsLogger_CanBeInjectedViaConstructor()
        {
            var logger = new RecordingDiagnosticsLogger();

            var list = new VsbNumberedList(logger);

            Assert.That(list.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        [Test]
        [Description("UXML から default ctor で生成された後でも SetDiagnosticsLogger で注入できる（UxmlFactory 経路の DI）")]
        public void SetDiagnosticsLogger_AfterDefaultCtor_AssignsLogger()
        {
            var list = new VsbNumberedList();
            var logger = new RecordingDiagnosticsLogger();

            list.SetDiagnosticsLogger(logger);

            Assert.That(list.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        // ---------- AddItem ----------

        [Test]
        [Description("AddItem 完了直後に ItemAdded(index, item) が 1 回発火する（Requirement 7.4）")]
        public void AddItem_FiresItemAddedOnce_WithIndexAndItem()
        {
            var list = new VsbNumberedList();
            var observed = new List<(int index, VisualElement item)>();
            list.ItemAdded += (i, e) => observed.Add((i, e));
            var element = new VisualElement();

            list.AddItem(element);

            Assert.That(observed.Count, Is.EqualTo(1));
            Assert.That(observed[0].index, Is.EqualTo(0));
            Assert.That(observed[0].item, Is.SameAs(element));
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list.ItemAt(0), Is.SameAs(element));
        }

        [Test]
        [Description("AddItem は番号バッジを 1 始まりで自動採番する")]
        public void AddItem_AutoNumbersBadgeStartingAtOne()
        {
            var list = new VsbNumberedList();

            list.AddItem(new VisualElement());
            list.AddItem(new VisualElement());
            list.AddItem(new VisualElement());

            var labels = list.Query<Label>(className: "vsb-numbered-list__index").ToList();
            Assert.That(labels.Count, Is.EqualTo(3));
            Assert.That(labels[0].text, Is.EqualTo("1"));
            Assert.That(labels[1].text, Is.EqualTo("2"));
            Assert.That(labels[2].text, Is.EqualTo("3"));
        }

        [Test]
        [Description("AddItem(null) は ArgumentNullException を投げて UI クラッシュではなく明示的に失敗する")]
        public void AddItem_NullItem_Throws()
        {
            var list = new VsbNumberedList();

            Assert.That(() => list.AddItem(null!), Throws.ArgumentNullException);
        }

        // ---------- RemoveAt ----------

        [Test]
        [Description("RemoveAt 完了直後に ItemRemoved(index) が 1 回発火し、後続要素の番号が再採番される")]
        public void RemoveAt_FiresItemRemovedAndRenumbers()
        {
            var list = new VsbNumberedList();
            list.AddItem(new VisualElement());
            list.AddItem(new VisualElement());
            list.AddItem(new VisualElement());
            var observed = new List<int>();
            list.ItemRemoved += observed.Add;

            list.RemoveAt(1);

            Assert.That(observed, Is.EqualTo(new[] { 1 }));
            Assert.That(list.Count, Is.EqualTo(2));
            var labels = list.Query<Label>(className: "vsb-numbered-list__index").ToList();
            Assert.That(labels[0].text, Is.EqualTo("1"));
            Assert.That(labels[1].text, Is.EqualTo("2"),
                "削除後は残った要素が 1 始まりで再採番される");
        }

        [Test]
        [Description("RemoveAt は範囲外 index で ArgumentOutOfRangeException を投げる")]
        public void RemoveAt_OutOfRange_Throws()
        {
            var list = new VsbNumberedList();
            list.AddItem(new VisualElement());

            Assert.That(() => list.RemoveAt(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => list.RemoveAt(1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // ---------- Reorder ----------

        [Test]
        [Description("Reorder 完了直後に ItemReordered(from, to) が 1 回発火し、要素順序と番号バッジが更新される")]
        public void Reorder_FiresItemReorderedAndRenumbers()
        {
            var list = new VsbNumberedList();
            var first = new VisualElement { name = "first" };
            var second = new VisualElement { name = "second" };
            var third = new VisualElement { name = "third" };
            list.AddItem(first);
            list.AddItem(second);
            list.AddItem(third);
            var observed = new List<(int from, int to)>();
            list.ItemReordered += (f, t) => observed.Add((f, t));

            list.Reorder(0, 2);

            Assert.That(observed, Is.EqualTo(new[] { (0, 2) }));
            Assert.That(list.ItemAt(0), Is.SameAs(second));
            Assert.That(list.ItemAt(1), Is.SameAs(third));
            Assert.That(list.ItemAt(2), Is.SameAs(first));
            var labels = list.Query<Label>(className: "vsb-numbered-list__index").ToList();
            Assert.That(labels[0].text, Is.EqualTo("1"));
            Assert.That(labels[1].text, Is.EqualTo("2"));
            Assert.That(labels[2].text, Is.EqualTo("3"));
        }

        [Test]
        [Description("Reorder で from == to の場合はイベント発火せず、副作用も生じない（idempotency）")]
        public void Reorder_SameIndex_DoesNotFire()
        {
            var list = new VsbNumberedList();
            list.AddItem(new VisualElement());
            list.AddItem(new VisualElement());
            var observed = new List<(int from, int to)>();
            list.ItemReordered += (f, t) => observed.Add((f, t));

            list.Reorder(1, 1);

            Assert.That(observed, Is.Empty);
        }

        [Test]
        [Description("Reorder は範囲外 index で ArgumentOutOfRangeException を投げる")]
        public void Reorder_OutOfRange_Throws()
        {
            var list = new VsbNumberedList();
            list.AddItem(new VisualElement());
            list.AddItem(new VisualElement());

            Assert.That(() => list.Reorder(-1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => list.Reorder(0, 5), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // ---------- 観測可能な完了状態: 3 要素追加→並び替え→削除 ----------

        [Test]
        [Description("3 要素追加→並び替え→削除のシナリオでイベント順序と最終状態が期待通りになる（tasks.md 7.3 観測可能な完了状態）")]
        public void Scenario_AddThreeReorderRemove_FiresEventsInOrder_AndKeepsAutoNumbering()
        {
            var list = new VsbNumberedList();
            var sequence = new List<string>();
            list.ItemAdded += (i, _) => sequence.Add($"add:{i}");
            list.ItemReordered += (f, t) => sequence.Add($"reorder:{f}->{t}");
            list.ItemRemoved += i => sequence.Add($"remove:{i}");

            var a = new VisualElement { name = "a" };
            var b = new VisualElement { name = "b" };
            var c = new VisualElement { name = "c" };

            list.AddItem(a);
            list.AddItem(b);
            list.AddItem(c);
            list.Reorder(2, 0);
            list.RemoveAt(1);

            Assert.That(sequence, Is.EqualTo(new[]
            {
                "add:0",
                "add:1",
                "add:2",
                "reorder:2->0",
                "remove:1",
            }));

            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list.ItemAt(0), Is.SameAs(c),
                "並び替えで先頭に出した c が削除後も先頭に残る");
            Assert.That(list.ItemAt(1), Is.SameAs(b),
                "中間にあった a が削除されて b が後続に残る");
            var labels = list.Query<Label>(className: "vsb-numbered-list__index").ToList();
            Assert.That(labels[0].text, Is.EqualTo("1"));
            Assert.That(labels[1].text, Is.EqualTo("2"));
        }
    }
}
