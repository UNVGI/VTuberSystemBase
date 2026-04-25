#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 7.2: <see cref="VsbColorPicker"/> contract tests. Pin
    /// <see cref="VsbControlBase"/> の <c>vsb-</c> プレフィクス登録、
    /// <see cref="VsbColorPicker.mode"/> による RGB / HSV 切替、
    /// <see cref="VsbColorPicker.value"/> 設定時の <see cref="VsbColorPicker.ValueChanged"/> 発火、
    /// <see cref="VsbColorPicker.Commit"/> による <see cref="VsbColorPicker.Committed"/> 発火、
    /// 必須 USS セレクタ（<c>vsb-color-picker</c>, <c>vsb-color-picker__channel</c>,
    /// <c>vsb-color-picker__preview</c>）の付与（design.md §CommonUi §VsbColorPicker;
    /// Requirement 7.1, 7.2, 7.3, 7.4, 7.7）。
    /// </summary>
    [TestFixture]
    public sealed class VsbColorPickerTests
    {
        // ---------- Class prefix / required selectors (Requirement 6.2 / 7.3) ----------

        [Test]
        [Description("インスタンス化直後にブロッククラス vsb-color-picker が付与されている（vsb- プレフィクス契約）")]
        public void Constructor_AppliesVsbColorPickerClass()
        {
            var picker = new VsbColorPicker();

            Assert.That(picker.ClassListContains("vsb-color-picker"), Is.True);
        }

        [Test]
        [Description("内部要素にプレビューと 3 チャンネルが存在し、それぞれ BEM 風の必須クラスを持つ（design.md USS セレクタ規約）")]
        public void Constructor_HasPreviewAndThreeChannelsWithBemClasses()
        {
            var picker = new VsbColorPicker();

            var preview = picker.Q<VisualElement>(className: "vsb-color-picker__preview");
            Assert.That(preview, Is.Not.Null, "vsb-color-picker__preview 要素が見つからない");

            var channels = picker.Query<VisualElement>(className: "vsb-color-picker__channel").ToList();
            Assert.That(channels.Count, Is.EqualTo(3),
                "RGB / HSV いずれのモードでも 3 チャンネル分の要素が常設される");
        }

        // ---------- Default values & UxmlAttribute defaults ----------

        [Test]
        [Description("既定モードは RGB で、value は黒色（design.md UxmlAttribute 既定）")]
        public void DefaultValues_AreRgbAndBlack()
        {
            var picker = new VsbColorPicker();

            Assert.That(picker.mode, Is.EqualTo(VsbColorPickerMode.Rgb));
            Assert.That(picker.value, Is.EqualTo(Color.black));
        }

        // ---------- DiagnosticsLogger DI（VsbControlBase 共通契約） ----------

        [Test]
        [Description("コンストラクタで IDiagnosticsLogger を受け取れる（VsbControlBase 共通の DI 集約点）")]
        public void DiagnosticsLogger_CanBeInjectedViaConstructor()
        {
            var logger = new RecordingDiagnosticsLogger();

            var picker = new VsbColorPicker(logger);

            Assert.That(picker.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        [Test]
        [Description("UXML から default ctor で生成された後でも SetDiagnosticsLogger で注入できる（UxmlFactory 経路の DI）")]
        public void SetDiagnosticsLogger_AfterDefaultCtor_AssignsLogger()
        {
            var picker = new VsbColorPicker();
            var logger = new RecordingDiagnosticsLogger();

            picker.SetDiagnosticsLogger(logger);

            Assert.That(picker.DiagnosticsLoggerForTests, Is.SameAs(logger));
        }

        // ---------- value setter → ValueChanged ----------

        [Test]
        [Description("value を変更すると ValueChanged が新しい色で 1 回発火する（Requirement 7.4）")]
        public void Value_SetToNewColor_FiresValueChangedOnce()
        {
            var picker = new VsbColorPicker();
            var observed = new List<Color>();
            picker.ValueChanged += observed.Add;

            var next = new Color(0.25f, 0.5f, 0.75f, 1f);
            picker.value = next;

            Assert.That(picker.value, Is.EqualTo(next));
            Assert.That(observed, Is.EqualTo(new[] { next }));
        }

        [Test]
        [Description("同じ色を再代入しても ValueChanged は再発火しない（idempotency 契約）")]
        public void Value_SetUnchanged_DoesNotFireValueChanged()
        {
            var picker = new VsbColorPicker();
            var color = new Color(0.1f, 0.2f, 0.3f, 1f);
            picker.value = color;
            var observed = new List<Color>();
            picker.ValueChanged += observed.Add;

            picker.value = color;

            Assert.That(observed, Is.Empty);
        }

        [Test]
        [Description("RGB 各成分は [0,1] にクランプされ、ValueChanged はクランプ後の値で発火する（値域違反時の挙動）")]
        public void Value_OutOfRangeRgb_IsClampedAndFires()
        {
            var picker = new VsbColorPicker();
            var observed = new List<Color>();
            picker.ValueChanged += observed.Add;

            picker.value = new Color(2f, -1f, 0.5f, 5f);

            Assert.That(picker.value.r, Is.EqualTo(1f));
            Assert.That(picker.value.g, Is.EqualTo(0f));
            Assert.That(picker.value.b, Is.EqualTo(0.5f));
            Assert.That(picker.value.a, Is.EqualTo(1f));
            Assert.That(observed.Count, Is.EqualTo(1));
        }

        // ---------- Committed ----------

        [Test]
        [Description("Commit() を呼ぶと Committed イベントが現在値で発火する（Requirement 7.4）")]
        public void Commit_RaisesCommittedEvent_WithCurrentValue()
        {
            var picker = new VsbColorPicker();
            var color = new Color(0.4f, 0.5f, 0.6f, 1f);
            picker.value = color;
            var observed = new List<Color>();
            picker.Committed += observed.Add;

            picker.Commit();

            Assert.That(observed, Is.EqualTo(new[] { color }));
        }

        [Test]
        [Description("value を変えるだけでは Committed は発火しない（Committed は明示的な確定操作のみ）")]
        public void ValueChange_AloneDoesNotRaiseCommitted()
        {
            var picker = new VsbColorPicker();
            var observed = new List<Color>();
            picker.Committed += observed.Add;

            picker.value = new Color(0.4f, 0.5f, 0.6f, 1f);

            Assert.That(observed, Is.Empty);
        }

        // ---------- mode 切替 ----------

        [Test]
        [Description("mode を Hsv に切り替えると vsb-color-picker--hsv モディファイアが付与され、Rgb モディファイアは外れる")]
        public void Mode_SetToHsv_UpdatesModifierClasses()
        {
            var picker = new VsbColorPicker();
            Assume.That(picker.ClassListContains("vsb-color-picker--rgb"), Is.True,
                "既定モードでは vsb-color-picker--rgb モディファイアが付与される");

            picker.mode = VsbColorPickerMode.Hsv;

            Assert.That(picker.ClassListContains("vsb-color-picker--hsv"), Is.True,
                "HSV モードに切り替えると vsb-color-picker--hsv が付与される");
            Assert.That(picker.ClassListContains("vsb-color-picker--rgb"), Is.False,
                "HSV モードでは vsb-color-picker--rgb は外れる");
        }

        [Test]
        [Description("mode を切り替えても保持中の色 (value) は不変（モードはチャンネルの解釈のみを変える）")]
        public void Mode_Toggle_PreservesValue()
        {
            var picker = new VsbColorPicker();
            var color = new Color(0.2f, 0.4f, 0.8f, 1f);
            picker.value = color;

            picker.mode = VsbColorPickerMode.Hsv;

            Assert.That(picker.value, Is.EqualTo(color));
        }

        [Test]
        [Description("同じ mode を再代入しても ValueChanged は発火しない（モード自身は色変更ではない）")]
        public void Mode_SetUnchanged_DoesNotFireValueChanged()
        {
            var picker = new VsbColorPicker();
            picker.value = new Color(0.4f, 0.5f, 0.6f, 1f);
            var observed = new List<Color>();
            picker.ValueChanged += observed.Add;

            picker.mode = VsbColorPickerMode.Rgb;

            Assert.That(observed, Is.Empty);
        }
    }
}
