#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.CommonUi.Controls
{
    /// <summary>
    /// 可変長整列リスト (Requirement 7.1, 7.2, 7.3, 7.4, 7.7;
    /// design.md §CommonUi §VsbNumberedList).
    /// <para>
    /// UXML カスタムコントロール + USS + C# ロジックの 3 点セットの C# 部分。
    /// UXML からは <c>&lt;VsbNumberedList /&gt;</c> で参照し、利用側が
    /// <see cref="AddItem(VisualElement)"/> で動的に子要素を追加する
    /// （design.md §CommonUi §Event Contract: 動的生成される子要素は利用側が
    /// <c>AddItem(VisualElement)</c> で追加）。利用者プロジェクトのスキン USS は
    /// <c>vsb-numbered-list</c> / <c>vsb-numbered-list__item</c> /
    /// <c>vsb-numbered-list__index</c> / <c>vsb-numbered-list__content</c>
    /// セレクタを起点に上書きできる（design.md §Skin USS セレクタ命名規約）。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 通知契約 (design.md §CommonUi §Event Contract):
    /// <list type="bullet">
    /// <item><description><see cref="ItemAdded"/> は <see cref="AddItem(VisualElement)"/>
    /// 完了直後に <c>(index, item)</c> で発火する。</description></item>
    /// <item><description><see cref="ItemRemoved"/> は <see cref="RemoveAt(int)"/>
    /// 完了直後に削除前の <c>index</c> で発火する。</description></item>
    /// <item><description><see cref="ItemReordered"/> は <see cref="Reorder(int, int)"/>
    /// 完了直後に <c>(fromIndex, toIndex)</c> で発火する（同一 index 指定時は
    /// 発火しない）。</description></item>
    /// </list>
    /// 自動採番: 各アイテムは行コンテナ <c>vsb-numbered-list__item</c> に
    /// 1 始まりの番号バッジ <c>vsb-numbered-list__index</c> を備え、
    /// Add/Remove/Reorder のたびに先頭から再採番する（O(n)、メインスレッドの
    /// レイアウト再計算 1 パスに収まる）。メインスレッドブロッキング処理（同期 I/O
    /// ・重い再レイアウト）は本クラス内で行わない（Requirement 7.7）。
    /// </remarks>
    public sealed class VsbNumberedList : VsbControlBase
    {
        public const string BlockName = "numbered-list";
        public const string ItemClass = "vsb-numbered-list__item";
        public const string IndexClass = "vsb-numbered-list__index";
        public const string ContentClass = "vsb-numbered-list__content";

        private readonly List<ItemRow> _rows = new List<ItemRow>();

        /// <summary>新しい要素が末尾に追加された直後に発火する。</summary>
        public event Action<int, VisualElement>? ItemAdded;

        /// <summary>要素が削除された直後に削除前 index で発火する。</summary>
        public event Action<int>? ItemRemoved;

        /// <summary>要素が並び替えられた直後に <c>(fromIndex, toIndex)</c> で発火する。</summary>
        public event Action<int, int>? ItemReordered;

        /// <summary>UXML 経由の生成で呼ばれる既定コンストラクタ。</summary>
        public VsbNumberedList() : this(null) { }

        /// <summary>
        /// 直接生成または DI 経路で <see cref="IDiagnosticsLogger"/> を渡す。
        /// </summary>
        public VsbNumberedList(IDiagnosticsLogger? diagnosticsLogger)
            : base(BlockName, diagnosticsLogger)
        {
        }

        /// <summary>現在の要素数。</summary>
        public int Count => _rows.Count;

        /// <summary>
        /// 指定 index のユーザ要素（<see cref="AddItem(VisualElement)"/> で渡された
        /// オリジナルの <see cref="VisualElement"/>）を返す。
        /// </summary>
        public VisualElement ItemAt(int index)
        {
            if (index < 0 || index >= _rows.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index,
                    $"Index out of range [0, {_rows.Count}).");
            }
            return _rows[index].UserItem;
        }

        /// <summary>
        /// 末尾に新しい要素を追加する。番号バッジは内部で自動採番される。
        /// </summary>
        public int AddItem(VisualElement item)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var row = new ItemRow(item);
            _rows.Add(row);
            hierarchy.Add(row.Container);
            RenumberFrom(_rows.Count - 1);

            var index = _rows.Count - 1;
            ItemAdded?.Invoke(index, item);
            return index;
        }

        /// <summary>
        /// 指定 index の要素を削除する。後続要素の番号バッジは自動再採番される。
        /// </summary>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _rows.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index,
                    $"Index out of range [0, {_rows.Count}).");
            }

            var row = _rows[index];
            _rows.RemoveAt(index);
            hierarchy.Remove(row.Container);
            RenumberFrom(index);

            ItemRemoved?.Invoke(index);
        }

        /// <summary>
        /// 要素を <paramref name="fromIndex"/> から <paramref name="toIndex"/> へ
        /// 移動する。両端の番号バッジは自動再採番される。
        /// 同一 index 指定時はイベントを発火しない。
        /// </summary>
        public void Reorder(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _rows.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fromIndex), fromIndex,
                    $"fromIndex out of range [0, {_rows.Count}).");
            }
            if (toIndex < 0 || toIndex >= _rows.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(toIndex), toIndex,
                    $"toIndex out of range [0, {_rows.Count}).");
            }
            if (fromIndex == toIndex)
            {
                return;
            }

            var row = _rows[fromIndex];
            _rows.RemoveAt(fromIndex);
            _rows.Insert(toIndex, row);

            // hierarchy を _rows と同じ順序に同期する。子要素の再アタッチは
            // VisualElement.Insert が既存親から自動で外して挿入し直すため
            // インデックス計算が一段だけで済む。
            hierarchy.Remove(row.Container);
            hierarchy.Insert(toIndex, row.Container);

            RenumberFrom(Math.Min(fromIndex, toIndex));

            ItemReordered?.Invoke(fromIndex, toIndex);
        }

        // ---------- 内部ロジック ----------

        private void RenumberFrom(int startIndex)
        {
            for (var i = startIndex; i < _rows.Count; i++)
            {
                _rows[i].IndexLabel.text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private sealed class ItemRow
        {
            public readonly VisualElement Container;
            public readonly Label IndexLabel;
            public readonly VisualElement UserItem;

            public ItemRow(VisualElement userItem)
            {
                UserItem = userItem;

                Container = new VisualElement();
                Container.AddToClassList(ItemClass);

                IndexLabel = new Label();
                IndexLabel.AddToClassList(IndexClass);
                Container.Add(IndexLabel);

                var content = new VisualElement();
                content.AddToClassList(ContentClass);
                content.Add(userItem);
                Container.Add(content);
            }
        }

        // ---------- UxmlFactory / UxmlTraits ----------

        /// <summary>UXML から <c>&lt;VsbNumberedList /&gt;</c> として参照可能にする (Requirement 7.2)。</summary>
        public new class UxmlFactory : UxmlFactory<VsbNumberedList, UxmlTraits> { }

        /// <summary>
        /// UXML 属性は本コントロールでは用いない（要素は実行時に
        /// <see cref="AddItem(VisualElement)"/> で動的追加する）。基底の Trait
        /// 経由で標準属性（<c>name</c>, <c>class</c>, <c>style</c> 等）のみ受け付ける。
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
        }
    }
}
