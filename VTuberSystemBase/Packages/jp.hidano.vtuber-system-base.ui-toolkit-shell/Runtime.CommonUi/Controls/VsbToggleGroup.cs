#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.CommonUi.Controls
{
    /// <summary>
    /// 排他選択トグルグループ (Requirement 7.1, 7.2, 7.3, 7.4, 7.7;
    /// design.md §CommonUi §VsbToggleGroup).
    /// <para>
    /// UXML カスタムコントロール + USS + C# ロジックの 3 点セットの C# 部分。
    /// UXML からは <c>&lt;VsbToggleGroup keys="a,b,c" /&gt;</c> で参照し、
    /// 利用者プロジェクトのスキン USS は <c>vsb-toggle-group</c> /
    /// <c>vsb-toggle-group__option</c> /
    /// <c>vsb-toggle-group__option--selected</c> セレクタを起点に上書きできる
    /// （design.md §Skin USS セレクタ命名規約）。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 通知契約 (design.md §CommonUi §Event Contract):
    /// <list type="bullet">
    /// <item><description><see cref="SelectionChanged"/> は
    /// <see cref="Select(string)"/> によって選択 key が変化した時に
    /// 1 回だけ排他発火する（同一 key の再選択はイベントを発火しない）。</description></item>
    /// </list>
    /// 排他選択契約: 任意のタイミングで <see cref="selectedKey"/> に該当する
    /// 1 個の option 要素のみが <c>vsb-toggle-group__option--selected</c>
    /// モディファイアを保持する。<see cref="keys"/> 再設定で option 要素は
    /// 一度だけ再構築され、以前の選択が新しい keys に含まれない場合は
    /// イベントを発火せずに選択を解除する（UXML 駆動の初期化が
    /// 偽陽性の SelectionChanged を出さない）。メインスレッド
    /// ブロッキング処理は本クラス内で行わない（Requirement 7.7）。
    /// </remarks>
    public sealed class VsbToggleGroup : VsbControlBase
    {
        public const string BlockName = "toggle-group";
        public const string OptionClass = "vsb-toggle-group__option";
        public const string OptionSelectedModifier = "vsb-toggle-group__option--selected";

        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, VisualElement> _options =
            new Dictionary<string, VisualElement>(StringComparer.Ordinal);

        private string? _selectedKey;

        /// <summary>選択 key が変化した時に新しい key で 1 回発火する。</summary>
        public event Action<string>? SelectionChanged;

        /// <summary>UXML 経由の生成で呼ばれる既定コンストラクタ。</summary>
        public VsbToggleGroup() : this(null) { }

        /// <summary>
        /// 直接生成または DI 経路で <see cref="IDiagnosticsLogger"/> を渡す。
        /// </summary>
        public VsbToggleGroup(IDiagnosticsLogger? diagnosticsLogger)
            : base(BlockName, diagnosticsLogger)
        {
        }

        // ---------- UxmlAttribute プロパティ ----------

        /// <summary>
        /// カンマ区切りの key 列。代入時に option 要素を再構築する。
        /// 空白のみの要素は無視され、重複は最初の出現が採用される。
        /// </summary>
        public string keys
        {
            get => string.Join(",", _keys);
            set => SetKeys(value);
        }

        /// <summary>パース済み key 列の読み取り専用ビュー。</summary>
        public IReadOnlyList<string> Keys => _keys;

        /// <summary>現在選択中の key、未選択時は <c>null</c>。</summary>
        public string? selectedKey => _selectedKey;

        /// <summary>
        /// 指定 key を排他選択する。同一 key の再選択は副作用なし
        /// （イベントも発火しない）。未登録 key は <see cref="ArgumentException"/>
        /// を投げ UI クラッシュではなく明示的に失敗する。
        /// </summary>
        public void Select(string key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (!_options.TryGetValue(key, out var nextOption))
            {
                throw new ArgumentException(
                    $"Key '{key}' is not registered in this VsbToggleGroup. Known keys: [{string.Join(",", _keys)}].",
                    nameof(key));
            }

            if (string.Equals(_selectedKey, key, StringComparison.Ordinal))
            {
                return;
            }

            if (_selectedKey is not null
                && _options.TryGetValue(_selectedKey, out var prevOption))
            {
                prevOption.RemoveFromClassList(OptionSelectedModifier);
            }

            nextOption.AddToClassList(OptionSelectedModifier);
            _selectedKey = key;

            SelectionChanged?.Invoke(key);
        }

        // ---------- 内部ロジック ----------

        private void SetKeys(string? raw)
        {
            var nextKeys = ParseKeys(raw);

            if (KeysEqual(_keys, nextKeys))
            {
                return;
            }

            ClearOptions();
            _keys.AddRange(nextKeys);
            for (var i = 0; i < _keys.Count; i++)
            {
                AppendOption(_keys[i]);
            }

            // 以前の選択 key が新しい keys に残っていれば視覚状態を再適用、
            // 残っていなければ選択を解除する。UXML 駆動の初期化経路で
            // 偽陽性の SelectionChanged を出さないために、ここでは
            // SelectionChanged を発火しない。
            if (_selectedKey is not null
                && _options.TryGetValue(_selectedKey, out var stillSelected))
            {
                stillSelected.AddToClassList(OptionSelectedModifier);
            }
            else
            {
                _selectedKey = null;
            }
        }

        private void ClearOptions()
        {
            for (var i = 0; i < _keys.Count; i++)
            {
                if (_options.TryGetValue(_keys[i], out var option))
                {
                    hierarchy.Remove(option);
                }
            }
            _keys.Clear();
            _options.Clear();
        }

        private void AppendOption(string key)
        {
            var option = new VisualElement { name = key };
            option.AddToClassList(OptionClass);
            option.RegisterCallback<ClickEvent>(OnOptionClicked);
            hierarchy.Add(option);
            _options[key] = option;
        }

        private void OnOptionClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement element
                && !string.IsNullOrEmpty(element.name)
                && _options.ContainsKey(element.name))
            {
                Select(element.name);
            }
        }

        private static List<string> ParseKeys(string? raw)
        {
            var parsed = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return parsed;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var parts = raw!.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var trimmed = parts[i].Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (seen.Add(trimmed))
                {
                    parsed.Add(trimmed);
                }
            }
            return parsed;
        }

        private static bool KeysEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (var i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        // ---------- UxmlFactory / UxmlTraits ----------

        /// <summary>UXML から <c>&lt;VsbToggleGroup /&gt;</c> として参照可能にする (Requirement 7.2)。</summary>
        public new class UxmlFactory : UxmlFactory<VsbToggleGroup, UxmlTraits> { }

        /// <summary>
        /// UXML 属性から <c>keys</c>（カンマ区切り）を読み取る。
        /// <see cref="UxmlStringAttributeDescription"/> は安定 API
        /// （design.md Risks 参照）。
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlStringAttributeDescription _keys =
                new UxmlStringAttributeDescription { name = "keys", defaultValue = string.Empty };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var group = (VsbToggleGroup)ve;
                group.keys = _keys.GetValueFromBag(bag, cc);
            }
        }
    }
}
