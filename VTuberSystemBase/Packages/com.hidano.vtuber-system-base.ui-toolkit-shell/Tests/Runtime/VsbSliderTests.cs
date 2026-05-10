#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 7.1: <see cref="VsbSlider"/> contract tests. Pin
    /// <see cref="VsbControlBase"/> の <c>vsb-</c> プレフィクス登録、
    /// <see cref="VsbSlider"/> の min/max/step プロパティ反映、値域違反時の
    /// クランプ、<see cref="VsbSlider.ValueChanged"/> /
    /// <see cref="VsbSlider.Committed"/> イベントの発火契約、必須 USS セレクタ
    /// (<c>vsb-slider</c>, <c>vsb-slider__track</c>, <c>vsb-slider__handle</c>) の
    /// 付与（design.md §CommonUi §VsbSlider; Requirement 7.1, 7.2, 7.3, 7.4, 7.7）。
    /// </summary>
    [TestFixture]
    public sealed class VsbSliderTests
    {
        // ---------- Class prefix / required selectors (Requirement 6.2 / 7.3) ----------

        [Test]
        [Description("インスタンス化直後にブロッククラス vsb-slider が付与されている（vsb- プレフィクス契約）")]
        public void Constructor_AppliesVsbSliderClass()
        {
            var slider = new VsbSlider();

            Assert.That(slider.ClassListContains("vsb-slider"), Is.True);
        }

        [Test]
        [Description("内部要素にトラックとハンドルが存在し、それぞれ BEM 風の必須クラスを持つ（design.md USS セレクタ規約）")]
        public void Constructor_HasTrackAndHandleChildrenWithBemClasses()
        {
            var slider = new VsbSlider();

            var track = slider.Q<VisualElement>(className: "vsb-slider__track");
            var handle = slider.Q<VisualElement>(className: "vsb-slider__handle");
            Assert.That(track, Is.Not.Null, "vsb-slider__track 要素が見つからない");
            Assert.That(handle, Is.Not.Null, "vsb-slider__handle 要素が見つからない");
        }

        // ---------- Default values & UxmlAttribute defaults ----------

        [Test]
        [Description("既定値は min=0 / max=1 / step=0 / value=0（design.md UxmlAttribute 既定）")]
        public void DefaultValues_AreZeroToOneStepZeroValueZero()
        {
            var slider = new VsbSlider();

            Assert.That(slider.min, Is.EqualTo(0f));
            Assert.That(slider.max, Is.EqualTo(1f));
            Assert.That(slider.step, Is.EqualTo(0f));
            Assert.That(slider.value, Is.EqualTo(0f));
        }

        // ---------- DiagnosticsLogger DI（VsbControlBase 共通契約） ----------

        [Test]
        [Description("コンストラクタで IDiagnosticsLogger を受け取れる（VsbControlBase 共通の DI 集約点）")]
        public void DiagnosticsLogger_CanBeInjectedViaConstructor()
        {
            var logger = new RecordingDiagnosticsLogger();

            var slider = new VsbSlider(logger);

            Assert.That(slider.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        [Test]
        [Description("UXML から default ctor で生成された後でも SetDiagnosticsLogger で注入できる（UxmlFactory 経路の DI）")]
        public void SetDiagnosticsLogger_AfterDefaultCtor_AssignsLogger()
        {
            var slider = new VsbSlider();
            var logger = new RecordingDiagnosticsLogger();

            slider.SetDiagnosticsLogger(logger);

            Assert.That(slider.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        // ---------- value setter ----------

        [Test]
        [Description("value を範囲内で変更すると ValueChanged が新しい値で 1 回発火する（Requirement 7.4）")]
        public void Value_SetWithinRange_FiresValueChangedOnce()
        {
            var slider = new VsbSlider { min = 0f, max = 10f };
            var observed = new List<float>();
            slider.ValueChanged += observed.Add;

            slider.value = 5f;

            Assert.That(slider.value, Is.EqualTo(5f));
            Assert.That(observed, Is.EqualTo(new[] { 5f }));
        }

        [Test]
        [Description("同じ値を再代入しても ValueChanged は再発火しない（idempotency 契約）")]
        public void Value_SetUnchanged_DoesNotFireValueChanged()
        {
            var slider = new VsbSlider { min = 0f, max = 10f };
            slider.value = 4f;
            var observed = new List<float>();
            slider.ValueChanged += observed.Add;

            slider.value = 4f;

            Assert.That(observed, Is.Empty);
        }

        [Test]
        [Description("max を超える値はクランプされ、ValueChanged はクランプ後の値で発火する（値域違反時の挙動）")]
        public void Value_AboveMax_IsClampedAndFires()
        {
            var slider = new VsbSlider { min = 0f, max = 10f };
            var observed = new List<float>();
            slider.ValueChanged += observed.Add;

            slider.value = 99f;

            Assert.That(slider.value, Is.EqualTo(10f));
            Assert.That(observed, Is.EqualTo(new[] { 10f }));
        }

        [Test]
        [Description("min を下回る値はクランプされ、ValueChanged はクランプ後の値で発火する（値域違反時の挙動）")]
        public void Value_BelowMin_IsClampedAndFires()
        {
            var slider = new VsbSlider { min = 0f, max = 10f };
            slider.value = 5f;
            var observed = new List<float>();
            slider.ValueChanged += observed.Add;

            slider.value = -3f;

            Assert.That(slider.value, Is.EqualTo(0f));
            Assert.That(observed, Is.EqualTo(new[] { 0f }));
        }

        // ---------- step ----------

        [Test]
        [Description("step が指定されている場合は最近接の step 倍数に丸められる")]
        public void Value_WithStep_RoundsToNearestStepMultiple()
        {
            var slider = new VsbSlider { min = 0f, max = 10f, step = 2f };

            slider.value = 5.4f;

            Assert.That(slider.value, Is.EqualTo(6f));
        }

        [Test]
        [Description("step==0 はステップ無効として扱う（任意の値を保持できる）")]
        public void Value_WithZeroStep_DoesNotRound()
        {
            var slider = new VsbSlider { min = 0f, max = 10f, step = 0f };

            slider.value = 3.14f;

            Assert.That(slider.value, Is.EqualTo(3.14f).Within(1e-5f));
        }

        // ---------- min / max ----------

        [Test]
        [Description("max を縮めると現在値が新 max を超えていれば自動的にクランプされ ValueChanged が発火する")]
        public void Max_ReducedBelowCurrentValue_ClampsAndNotifies()
        {
            var slider = new VsbSlider { min = 0f, max = 100f };
            slider.value = 80f;
            var observed = new List<float>();
            slider.ValueChanged += observed.Add;

            slider.max = 50f;

            Assert.That(slider.value, Is.EqualTo(50f));
            Assert.That(observed, Is.EqualTo(new[] { 50f }));
        }

        [Test]
        [Description("min > max のような値域違反はログ警告 + max を min に揃える防御策で UI クラッシュを起こさない")]
        public void Min_GreaterThanMax_LogsWarningAndCoerces()
        {
            var logger = new RecordingDiagnosticsLogger();
            var slider = new VsbSlider(logger) { min = 0f, max = 10f };

            slider.min = 99f;

            Assert.That(slider.max, Is.GreaterThanOrEqualTo(slider.min),
                "min が max を超えた際に max を引き上げて整合性を維持する");
            var hasWarning = false;
            foreach (var entry in logger.Entries)
            {
                if (entry.Level == LogLevel.Warning && entry.Category == LogCategory.Skin)
                {
                    hasWarning = true;
                    break;
                }
            }
            Assert.That(hasWarning, Is.True,
                "値域違反は LogCategory.Skin の Warning として診断ログに記録される");
        }

        // ---------- Committed ----------

        [Test]
        [Description("Commit() を呼ぶと Committed イベントが現在値で発火する（Requirement 7.4）")]
        public void Commit_RaisesCommittedEvent_WithCurrentValue()
        {
            var slider = new VsbSlider { min = 0f, max = 10f };
            slider.value = 7f;
            var observed = new List<float>();
            slider.Committed += observed.Add;

            slider.Commit();

            Assert.That(observed, Is.EqualTo(new[] { 7f }));
        }

        [Test]
        [Description("value を変えるだけでは Committed は発火しない（Committed は明示的な確定操作のみ）")]
        public void ValueChange_AloneDoesNotRaiseCommitted()
        {
            var slider = new VsbSlider { min = 0f, max = 10f };
            var observed = new List<float>();
            slider.Committed += observed.Add;

            slider.value = 4f;

            Assert.That(observed, Is.Empty);
        }

        // ---------- Disabled state class ----------

        [Test]
        [Description("SetTrackDisabled(true) で track に vsb-slider__track--disabled が付き、false で外れる（design.md USS modifier）")]
        public void SetTrackDisabled_TogglesDisabledTrackClass()
        {
            var slider = new VsbSlider();
            var track = slider.Q<VisualElement>(className: "vsb-slider__track");
            Assume.That(track, Is.Not.Null);

            slider.SetTrackDisabled(true);
            Assert.That(track!.ClassListContains("vsb-slider__track--disabled"), Is.True,
                "disabled 指定で必須 modifier クラスが付与される");

            slider.SetTrackDisabled(false);
            Assert.That(track.ClassListContains("vsb-slider__track--disabled"), Is.False,
                "enabled 指定に戻すと modifier クラスが外れる");
        }
    }
}
