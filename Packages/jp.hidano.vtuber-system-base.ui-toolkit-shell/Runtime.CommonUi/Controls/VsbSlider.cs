#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.CommonUi.Controls
{
    /// <summary>
    /// 数値スライダー (Requirement 7.1, 7.2, 7.3, 7.4, 7.7;
    /// design.md §CommonUi §VsbSlider).
    /// <para>
    /// UXML カスタムコントロール + USS + C# ロジックの 3 点セットの C# 部分。
    /// UXML からは <c>&lt;VsbSlider min="..." max="..." step="..." value="..." /&gt;</c>
    /// で参照し、利用者プロジェクトのスキン USS は <c>vsb-slider</c> /
    /// <c>vsb-slider__track</c> / <c>vsb-slider__handle</c> /
    /// <c>vsb-slider__track--disabled</c> セレクタを起点に上書きできる
    /// （design.md §Skin USS セレクタ命名規約）。
    /// </para>
    /// </summary>
    /// <remarks>
    /// 値変更通知契約:
    /// <list type="bullet">
    /// <item><description><see cref="ValueChanged"/> は連続的な値変更
    /// （ドラッグ中・プログラム変更）で発火する。</description></item>
    /// <item><description><see cref="Committed"/> は明示的な確定
    /// （PointerUp / <see cref="Commit"/> 呼び出し）でのみ発火する。</description></item>
    /// </list>
    /// メインスレッドブロッキング処理（同期 I/O・重い再レイアウト）は本クラス内で
    /// 行わない（Requirement 7.7）。値クランプ・ステップ丸めは O(1) の算術のみ。
    /// </remarks>
    public sealed class VsbSlider : VsbControlBase
    {
        public const string BlockName = "slider";
        public const string TrackClass = "vsb-slider__track";
        public const string HandleClass = "vsb-slider__handle";
        public const string TrackDisabledModifier = "vsb-slider__track--disabled";

        private readonly VisualElement _track;
        private readonly VisualElement _handle;

        private float _min;
        private float _max = 1f;
        private float _step;
        private float _value;

        /// <summary>連続的な値変更で発火する。引数はクランプ・丸め後の値。</summary>
        public event Action<float>? ValueChanged;

        /// <summary>確定操作（<see cref="Commit"/> / PointerUp）で発火する。</summary>
        public event Action<float>? Committed;

        /// <summary>UXML 経由の生成で呼ばれる既定コンストラクタ。</summary>
        public VsbSlider() : this(null) { }

        /// <summary>
        /// 直接生成または DI 経路で <see cref="IDiagnosticsLogger"/> を渡す。
        /// </summary>
        public VsbSlider(IDiagnosticsLogger? diagnosticsLogger)
            : base(BlockName, diagnosticsLogger)
        {
            _track = new VisualElement();
            _track.AddToClassList(TrackClass);

            _handle = new VisualElement();
            _handle.AddToClassList(HandleClass);

            _track.Add(_handle);
            hierarchy.Add(_track);

            _handle.RegisterCallback<PointerUpEvent>(OnHandlePointerUp);
        }

        // ---------- UxmlAttribute プロパティ ----------

        public float min
        {
            get => _min;
            set => SetMin(value);
        }

        public float max
        {
            get => _max;
            set => SetMax(value);
        }

        public float step
        {
            get => _step;
            set => _step = value < 0f ? 0f : value;
        }

        public float value
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

        /// <summary>
        /// 視覚上の disabled 状態を track に対して付与/解除する。USS 側から
        /// <c>vsb-slider__track--disabled</c> でフォーカスや色味を切り替える
        /// 想定（design.md USS modifier 命名規約）。Unity 標準の
        /// <see cref="VisualElement.SetEnabled"/> が pseudo state ベースで
        /// クラス更新イベントを発火しないため、shell 側で明示的にクラス操作する
        /// 経路をここで提供する。
        /// </summary>
        public void SetTrackDisabled(bool disabled)
        {
            if (disabled)
            {
                if (!_track.ClassListContains(TrackDisabledModifier))
                {
                    _track.AddToClassList(TrackDisabledModifier);
                }
            }
            else
            {
                _track.RemoveFromClassList(TrackDisabledModifier);
            }
        }

        // ---------- 内部ロジック ----------

        private void SetMin(float requested)
        {
            _min = requested;
            if (_max < _min)
            {
                DiagnosticsLogger?.Log(
                    LogLevel.Warning, LogCategory.Skin,
                    $"VsbSlider: min ({_min}) > max ({_max}) — coercing max up to min.");
                _max = _min;
            }
            ReclampOnRangeChange();
        }

        private void SetMax(float requested)
        {
            _max = requested;
            if (_max < _min)
            {
                DiagnosticsLogger?.Log(
                    LogLevel.Warning, LogCategory.Skin,
                    $"VsbSlider: max ({_max}) < min ({_min}) — coercing min down to max.");
                _min = _max;
            }
            ReclampOnRangeChange();
        }

        private void ReclampOnRangeChange()
        {
            var clamped = ClampAndQuantize(_value);
            if (!Mathf.Approximately(clamped, _value))
            {
                _value = clamped;
                ValueChanged?.Invoke(_value);
            }
        }

        private void SetValue(float requested)
        {
            var next = ClampAndQuantize(requested);
            if (Mathf.Approximately(next, _value))
            {
                return;
            }
            _value = next;
            ValueChanged?.Invoke(_value);
        }

        private float ClampAndQuantize(float requested)
        {
            var clamped = Mathf.Clamp(requested, _min, _max);
            if (_step > 0f)
            {
                var steps = Mathf.Round((clamped - _min) / _step);
                clamped = Mathf.Clamp(_min + steps * _step, _min, _max);
            }
            return clamped;
        }

        private void OnHandlePointerUp(PointerUpEvent _)
        {
            Commit();
        }

        // ---------- UxmlFactory / UxmlTraits ----------

        /// <summary>UXML から <c>&lt;VsbSlider /&gt;</c> として参照可能にする (Requirement 7.2)。</summary>
        public new class UxmlFactory : UxmlFactory<VsbSlider, UxmlTraits> { }

        /// <summary>
        /// UXML 属性から <see cref="min"/> / <see cref="max"/> / <see cref="step"/> /
        /// <see cref="value"/> を読み取る。<see cref="UxmlFloatAttributeDescription"/>
        /// は安定 API（design.md Risks 参照）。
        /// </summary>
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlFloatAttributeDescription _min =
                new UxmlFloatAttributeDescription { name = "min", defaultValue = 0f };

            private readonly UxmlFloatAttributeDescription _max =
                new UxmlFloatAttributeDescription { name = "max", defaultValue = 1f };

            private readonly UxmlFloatAttributeDescription _step =
                new UxmlFloatAttributeDescription { name = "step", defaultValue = 0f };

            private readonly UxmlFloatAttributeDescription _value =
                new UxmlFloatAttributeDescription { name = "value", defaultValue = 0f };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var slider = (VsbSlider)ve;
                slider.min = _min.GetValueFromBag(bag, cc);
                slider.max = _max.GetValueFromBag(bag, cc);
                slider.step = _step.GetValueFromBag(bag, cc);
                slider.value = _value.GetValueFromBag(bag, cc);
            }
        }
    }
}
