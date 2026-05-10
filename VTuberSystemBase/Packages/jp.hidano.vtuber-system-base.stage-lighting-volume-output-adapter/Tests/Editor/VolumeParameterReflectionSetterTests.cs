#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class VolumeParameterReflectionSetterTests
    {
        private Bloom _bloom = null!;
        private Tonemapping _tonemap = null!;
        private ColorAdjustments _color = null!;

        [SetUp]
        public void SetUp()
        {
            _bloom = ScriptableObject.CreateInstance<Bloom>();
            _tonemap = ScriptableObject.CreateInstance<Tonemapping>();
            _color = ScriptableObject.CreateInstance<ColorAdjustments>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bloom);
            Object.DestroyImmediate(_tonemap);
            Object.DestroyImmediate(_color);
        }

        [Test]
        public void ApplyValue_FloatIntensity_SetsValueAndOverrideState()
        {
            var dto = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1.5f, null, null, null);
            var ok = VolumeParameterReflectionSetter.ApplyValue(_bloom, "intensity", dto);
            Assert.That(ok, Is.True);
            Assert.That(_bloom.intensity.value, Is.EqualTo(1.5f));
            Assert.That(_bloom.intensity.overrideState, Is.True);
        }

        [Test]
        public void ApplyValue_ColorFilter_SetsColorValue()
        {
            var dto = new VolumeOverrideParamValueDto(ParamKind.Color, null, null, null,
                new ColorDto(0.5f, 0.25f, 0.125f, 1f), null, null);
            var ok = VolumeParameterReflectionSetter.ApplyValue(_color, "colorFilter", dto);
            Assert.That(ok, Is.True);
            Assert.That(_color.colorFilter.value.r, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(_color.colorFilter.value.b, Is.EqualTo(0.125f).Within(1e-6f));
        }

        [Test]
        public void ApplyValue_TonemappingMode_Enum_SetsMode()
        {
            // TonemappingMode.Neutral or ACES is a valid enum name.
            var dto = new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "ACES");
            var ok = VolumeParameterReflectionSetter.ApplyValue(_tonemap, "mode", dto);
            Assert.That(ok, Is.True);
            Assert.That(_tonemap.mode.value, Is.EqualTo(TonemappingMode.ACES));
        }

        [Test]
        public void ApplyValue_UnknownField_ReturnsFalse()
        {
            var dto = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1f, null, null, null);
            var ok = VolumeParameterReflectionSetter.ApplyValue(_bloom, "doesNotExist", dto);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void ApplyValue_KindMismatch_ReturnsFalse()
        {
            // intensity is Float, but we provide Color value.
            var dto = new VolumeOverrideParamValueDto(ParamKind.Color, null, null, null,
                new ColorDto(1, 0, 0, 1), null, null);
            var ok = VolumeParameterReflectionSetter.ApplyValue(_bloom, "intensity", dto);
            Assert.That(ok, Is.False);
        }
    }
}
