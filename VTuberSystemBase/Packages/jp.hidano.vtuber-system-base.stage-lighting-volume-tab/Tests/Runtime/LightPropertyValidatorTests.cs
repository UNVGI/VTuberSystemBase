#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Validation;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Boundary-value tests for <see cref="LightPropertyValidator"/> (Task 3.4,
    /// Requirements 5.7, 6.7, 9.3).
    /// </summary>
    [TestFixture]
    public sealed class LightPropertyValidatorTests
    {
        // ---------- Intensity ----------

        [Test]
        public void Intensity_Zero_IsValid()
        {
            var r = LightPropertyValidator.ValidateIntensity(0f);
            Assert.That(r.IsValid, Is.True);
        }

        [Test]
        public void Intensity_Positive_IsValid()
        {
            Assert.That(LightPropertyValidator.ValidateIntensity(1f).IsValid, Is.True);
            Assert.That(LightPropertyValidator.ValidateIntensity(100f).IsValid, Is.True);
        }

        [Test]
        public void Intensity_Negative_IsInvalid()
        {
            var r = LightPropertyValidator.ValidateIntensity(-0.0001f);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_min"));
        }

        [Test]
        public void Intensity_NaNorInfinity_IsInvalid()
        {
            Assert.That(LightPropertyValidator.ValidateIntensity(float.NaN).IsValid, Is.False);
            Assert.That(LightPropertyValidator.ValidateIntensity(float.PositiveInfinity).IsValid, Is.False);
        }

        // ---------- Range ----------

        [Test]
        public void Range_Zero_IsValid() =>
            Assert.That(LightPropertyValidator.ValidateRange(0f).IsValid, Is.True);

        [Test]
        public void Range_Negative_IsInvalid()
        {
            var r = LightPropertyValidator.ValidateRange(-1f);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_min"));
        }

        // ---------- Spot Angle ----------

        [Test]
        public void SpotAngle_LowerBoundary_IsValidAt1()
        {
            Assert.That(LightPropertyValidator.ValidateSpotAngle(1f).IsValid, Is.True);
        }

        [Test]
        public void SpotAngle_BelowLowerBoundary_IsInvalid()
        {
            var r = LightPropertyValidator.ValidateSpotAngle(0.99f);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_min"));
        }

        [Test]
        public void SpotAngle_NegativeValue_IsInvalid()
        {
            var r = LightPropertyValidator.ValidateSpotAngle(-30f);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_min"));
        }

        [Test]
        public void SpotAngle_UpperBoundary_IsValidAt179()
        {
            Assert.That(LightPropertyValidator.ValidateSpotAngle(179f).IsValid, Is.True);
        }

        [Test]
        public void SpotAngle_AboveUpperBoundary_IsInvalid()
        {
            var r = LightPropertyValidator.ValidateSpotAngle(179.01f);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_max"));
        }

        // ---------- Color ----------

        [Test]
        public void Color_AllChannelsZero_IsValid()
        {
            Assert.That(LightPropertyValidator.ValidateColor(new ColorDto(0, 0, 0, 0)).IsValid, Is.True);
        }

        [Test]
        public void Color_AllChannelsOne_IsValid()
        {
            Assert.That(LightPropertyValidator.ValidateColor(new ColorDto(1, 1, 1, 1)).IsValid, Is.True);
        }

        [Test]
        public void Color_HdrAboveOne_IsValid()
        {
            // HDR allowed (Req 5.7 + ColorDto doc): bare HDR value above 1 must not be rejected.
            Assert.That(LightPropertyValidator.ValidateColor(new ColorDto(2, 2, 2, 1)).IsValid, Is.True);
        }

        [Test]
        public void Color_NegativeChannel_IsInvalid()
        {
            var r = LightPropertyValidator.ValidateColor(new ColorDto(-0.1f, 0, 0, 1));
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_min"));
        }

        [Test]
        public void Color_NaNChannel_IsInvalid()
        {
            var r = LightPropertyValidator.ValidateColor(new ColorDto(float.NaN, 1, 1, 1));
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("invalid_number"));
        }

        // ---------- Volume Param: Float kind with FloatMin/Max ----------

        [Test]
        public void VolumeParam_Float_WithinRange_IsValid()
        {
            var schema = new VolumeOverrideParamDto(
                "intensity", ParamKind.Float, "Intensity",
                DefaultValue: new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1f, null, null, null),
                Range: new VolumeOverrideParamRangeDto(0f, 10f, null, null, null));

            var inside = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 5f, null, null, null);
            Assert.That(LightPropertyValidator.ValidateVolumeParam(schema, inside).IsValid, Is.True);
        }

        [Test]
        public void VolumeParam_Float_BelowFloatMin_IsInvalid()
        {
            var schema = new VolumeOverrideParamDto(
                "intensity", ParamKind.Float, "Intensity",
                new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1f, null, null, null),
                new VolumeOverrideParamRangeDto(0f, 10f, null, null, null));

            var below = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, -0.01f, null, null, null);
            var r = LightPropertyValidator.ValidateVolumeParam(schema, below);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_min"));
        }

        [Test]
        public void VolumeParam_Float_AboveFloatMax_IsInvalid()
        {
            var schema = new VolumeOverrideParamDto(
                "intensity", ParamKind.Float, "Intensity",
                new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1f, null, null, null),
                new VolumeOverrideParamRangeDto(0f, 10f, null, null, null));

            var above = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 10.0001f, null, null, null);
            var r = LightPropertyValidator.ValidateVolumeParam(schema, above);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("out_of_range_max"));
        }

        [Test]
        public void VolumeParam_Int_OutOfRange_ReturnsCorrectErrorCode()
        {
            var schema = new VolumeOverrideParamDto(
                "samples", ParamKind.Int, "Samples",
                new VolumeOverrideParamValueDto(ParamKind.Int, null, 4, null, null, null, null),
                new VolumeOverrideParamRangeDto(null, null, 1, 16, null));

            var below = new VolumeOverrideParamValueDto(ParamKind.Int, null, 0, null, null, null, null);
            var above = new VolumeOverrideParamValueDto(ParamKind.Int, null, 17, null, null, null, null);

            Assert.That(LightPropertyValidator.ValidateVolumeParam(schema, below).ErrorCode,
                Is.EqualTo("out_of_range_min"));
            Assert.That(LightPropertyValidator.ValidateVolumeParam(schema, above).ErrorCode,
                Is.EqualTo("out_of_range_max"));
        }

        [Test]
        public void VolumeParam_Enum_NotInEnumValues_IsInvalidEnum()
        {
            var schema = new VolumeOverrideParamDto(
                "tonemap", ParamKind.Enum, "Tonemap",
                new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "ACES"),
                new VolumeOverrideParamRangeDto(null, null, null, null, new[] { "ACES", "Neutral" }));

            var bad = new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "BAD");
            var r = LightPropertyValidator.ValidateVolumeParam(schema, bad);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("invalid_enum"));
        }

        [Test]
        public void VolumeParam_Enum_InEnumValues_IsValid()
        {
            var schema = new VolumeOverrideParamDto(
                "tonemap", ParamKind.Enum, "Tonemap",
                new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "ACES"),
                new VolumeOverrideParamRangeDto(null, null, null, null, new[] { "ACES", "Neutral" }));

            var ok = new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "Neutral");
            Assert.That(LightPropertyValidator.ValidateVolumeParam(schema, ok).IsValid, Is.True);
        }

        [Test]
        public void VolumeParam_Bool_RoundtripsThroughValidator()
        {
            var schema = new VolumeOverrideParamDto(
                "enabled", ParamKind.Bool, "Enabled",
                new VolumeOverrideParamValueDto(ParamKind.Bool, true, null, null, null, null, null),
                Range: null);

            var ok = new VolumeOverrideParamValueDto(ParamKind.Bool, false, null, null, null, null, null);
            Assert.That(LightPropertyValidator.ValidateVolumeParam(schema, ok).IsValid, Is.True);

            var missing = new VolumeOverrideParamValueDto(ParamKind.Bool, null, null, null, null, null, null);
            Assert.That(LightPropertyValidator.ValidateVolumeParam(schema, missing).ErrorCode,
                Is.EqualTo("missing_value"));
        }

        [Test]
        public void VolumeParam_KindMismatch_IsInvalid()
        {
            var schema = new VolumeOverrideParamDto(
                "intensity", ParamKind.Float, "Intensity",
                new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1f, null, null, null),
                null);

            var mismatched = new VolumeOverrideParamValueDto(ParamKind.Bool, true, null, null, null, null, null);
            var r = LightPropertyValidator.ValidateVolumeParam(schema, mismatched);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("kind_mismatch"));
        }

        [Test]
        public void VolumeParam_Unknown_FlagsForSkip()
        {
            var schema = new VolumeOverrideParamDto(
                "future", ParamKind.Unknown, "Future",
                new VolumeOverrideParamValueDto(ParamKind.Unknown, null, null, null, null, null, null),
                null);

            var v = new VolumeOverrideParamValueDto(ParamKind.Unknown, null, null, null, null, null, null);
            var r = LightPropertyValidator.ValidateVolumeParam(schema, v);
            Assert.That(r.IsValid, Is.False);
            Assert.That(r.ErrorCode, Is.EqualTo("unknown_kind"));
        }
    }
}
