#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.CommonUi.Controls
{
    /// <summary>
    /// チャンネル解釈モード。<see cref="VsbColorPicker"/> が公開するチャンネル UI を
    /// RGB / HSV のどちらで描画するかを切り替える。<c>value</c> として保持する
    /// <see cref="UnityEngine.Color"/> 自身は常に sRGB の RGBA 表現で、モードは
    /// あくまで「ユーザに見せるチャンネルの意味」だけを変える（design.md
    /// §CommonUi §VsbColorPicker §Event Contract）。
    /// </summary>
    public enum VsbColorPickerMode
    {
        Rgb = 0,
        Hsv = 1,
    }

    /// <summary>
    /// RGB / HSV 切替式の色選択コントロール (Requirement 7.1, 7.2, 7.3, 7.4, 7.7;
    /// design.md §CommonUi §VsbColorPicker)。
    /// <para>
    /// UXML カスタムコントロール + USS + C# ロジックの 3 点セットの C# 部分。
    /// UXML からは <c>&lt;VsbColorPicker mode="Rgb" /&gt;</c> で参照し、利用者プロジェクトの
    /// スキン USS は <c>vsb-color-picker</c> / <c>vsb-color-picker__preview</c> /
    /// <c>vsb-color-picker__channel</c> / <c>vsb-color-picker--rgb</c> /
    /// <c>vsb-color-picker--hsv</c> セレクタを起点に上書きできる
    /// （design.md §Skin USS セレクタ命名規約）。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 値変更通知契約:
    /// <list type="bullet">
    /// <item><description><see cref="ValueChanged"/> は <see cref="value"/> 設定で
    /// 値が変化した時にクランプ後の色で発火する。</description></item>
    /// <item><description><see cref="Committed"/> は明示的な確定
    /// （<see cref="Commit"/> 呼び出し）でのみ発火する。</description></item>
    /// </list>
    /// メインスレッドブロッキング処理（同期 I/O・重い再レイアウト）は本クラス内で
    /// 行わない（Requirement 7.7）。チャンネル要素生成はコンストラクタで一度きり、
    /// モード切替はクラス付け替えのみで子要素の差し替えは行わない。
    /// </remarks>
    public sealed class VsbColorPicker : VsbControlBase
    {
        public const string BlockName = "color-picker";
        public const string PreviewClass = "vsb-color-picker__preview";
        public const string ChannelClass = "vsb-color-picker__channel";
        public const string RgbModifier = "vsb-color-picker--rgb";
        public const string HsvModifier = "vsb-color-picker--hsv";

        private const int ChannelCount = 3;

        private readonly VisualElement _preview;
        private readonly VisualElement[] _channels;

        private VsbColorPickerMode _mode = VsbColorPickerMode.Rgb;
        private Color _value = Color.black;

        /// <summary>連続的な値変更で発火する。引数はクランプ後の色。</summary>
        public event Action<Color>? ValueChanged;

        /// <summary>確定操作（<see cref="Commit"/>）で発火する。</summary>
        public event Action<Color>? Committed;

        /// <summary>UXML 経由の生成で呼ばれる既定コンストラクタ。</summary>
        public VsbColorPicker() : this(null) { }

        /// <summary>
        /// 直接生成または DI 経路で <see cref="IDiagnosticsLogger"/> を渡す。
        /// </summary>
        public VsbColorPicker(IDiagnosticsLogger? diagnosticsLogger)
            : base(BlockName, diagnosticsLogger)
        {
            _preview = new VisualElement();
            _preview.AddToClassList(PreviewClass);
            hierarchy.Add(_preview);

            _channels = new VisualElement[ChannelCount];
            for (var i = 0; i < ChannelCount; i++)
            {
                var channel = new VisualElement();
                channel.AddToClassList(ChannelClass);
                hierarchy.Add(channel);
                _channels[i] = channel;
            }

            ApplyModeClass();
            ApplyPreviewBackground();
        }

        // ---------- UxmlAttribute プロパティ ----------

        public VsbColorPickerMode mode
        {
            get => _mode;
            set => SetMode(value);
        }

        public Color value
        {
            get => _value;
            set => SetValue(value);
        }

        /// <summary>
        /// 現在値を確定値として通知する。<see cref="Committed"/> のみ発火し
        /// <see cref="ValueChanged"/> は発火しない。
        /// </summary>
        public void Commit()
        {
            Committed?.Invoke(_value);
        }

        // ---------- 内部ロジック ----------

        private void SetMode(VsbColorPickerMode requested)
        {
            if (_mode == requested)
            {
                return;
            }
            _mode = requested;
            ApplyModeClass();
        }

        private void ApplyModeClass()
        {
            // 旧モディファイアを外し、新モディファイアだけが付与された状態にする。
            RemoveFromClassList(RgbModifier);
            RemoveFromClassList(HsvModifier);
            AddToClassList(_mode == VsbColorPickerMode.Hsv ? HsvModifier : RgbModifier);
        }

        private void SetValue(Color requested)
        {
            var next = ClampColor(requested);
            if (ColorsEqual(next, _value))
            {
                return;
            }
            _value = next;
            ApplyPreviewBackground();
            ValueChanged?.Invoke(_value);
        }

        private void ApplyPreviewBackground()
        {
            _preview.style.backgroundColor = new StyleColor(_value);
        }

        private static Color ClampColor(Color c)
        {
            return new Color(
                Mathf.Clamp01(c.r),
                Mathf.Clamp01(c.g),
                Mathf.Clamp01(c.b),
                Mathf.Clamp01(c.a));
        }

        private static bool ColorsEqual(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                && Mathf.Approximately(a.g, b.g)
                && Mathf.Approximately(a.b, b.b)
                && Mathf.Approximately(a.a, b.a);
        }

        // ---------- UxmlFactory / UxmlTraits ----------

        /// <summary>UXML から <c>&lt;VsbColorPicker /&gt;</c> として参照可能にする (Requirement 7.2)。</summary>
        public new class UxmlFactory : UxmlFactory<VsbColorPicker, UxmlTraits> { }

        /// <summary>
        /// UXML 属性から <see cref="mode"/> を読み取る。<see cref="UxmlEnumAttributeDescription{TEnum}"/>
        /// は安定 API（design.md Risks 参照）。<see cref="value"/> は UXML から
        /// 直接渡せないため（<see cref="UnityEngine.Color"/> 用の標準
        /// UxmlAttributeDescription が存在しない）コード経路でのみ設定する。
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlEnumAttributeDescription<VsbColorPickerMode> _mode =
                new UxmlEnumAttributeDescription<VsbColorPickerMode>
                {
                    name = "mode",
                    defaultValue = VsbColorPickerMode.Rgb,
                };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var picker = (VsbColorPicker)ve;
                picker.mode = _mode.GetValueFromBag(bag, cc);
            }
        }
    }
}
