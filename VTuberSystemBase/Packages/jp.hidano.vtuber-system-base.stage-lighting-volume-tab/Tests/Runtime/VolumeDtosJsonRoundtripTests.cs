#nullable enable
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the JSON wire format for the Volume Override family of DTOs (Task 2.3,
    /// Requirements 6.1, 6.2, 6.10, 6.11). The discriminated union value DTO has one
    /// nullable payload field per <see cref="ParamKind"/> so the format is portable
    /// without requiring a custom <c>JsonConverter</c>.
    /// </summary>
    [TestFixture]
    public sealed class VolumeDtosJsonRoundtripTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        [Test]
        public void VolumeOverrideParamValueDto_BoolKind_Roundtrips()
        {
            var v = new VolumeOverrideParamValueDto(
                Kind: ParamKind.Bool,
                BoolValue: true,
                IntValue: null, FloatValue: null, ColorValue: null, VectorValue: null, EnumValue: null);
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamValueDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.Kind, Is.EqualTo(ParamKind.Bool));
            Assert.That(rt.BoolValue, Is.True);
        }

        [Test]
        public void VolumeOverrideParamValueDto_IntKind_Roundtrips()
        {
            var v = new VolumeOverrideParamValueDto(
                ParamKind.Int, null, 7, null, null, null, null);
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamValueDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.IntValue, Is.EqualTo(7));
        }

        [Test]
        public void VolumeOverrideParamValueDto_FloatKind_Roundtrips()
        {
            var v = new VolumeOverrideParamValueDto(
                ParamKind.Float, null, null, 3.14f, null, null, null);
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamValueDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.FloatValue, Is.EqualTo(3.14f));
        }

        [Test]
        public void VolumeOverrideParamValueDto_ClampedFloatKind_Roundtrips()
        {
            var v = new VolumeOverrideParamValueDto(
                ParamKind.ClampedFloat, null, null, 0.42f, null, null, null);
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamValueDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.Kind, Is.EqualTo(ParamKind.ClampedFloat));
            Assert.That(rt.FloatValue, Is.EqualTo(0.42f));
        }

        [Test]
        public void VolumeOverrideParamValueDto_ColorKind_Roundtrips()
        {
            var v = new VolumeOverrideParamValueDto(
                ParamKind.Color, null, null, null, new ColorDto(0.1f, 0.2f, 0.3f, 1f), null, null);
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamValueDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.ColorValue, Is.Not.Null);
            Assert.That(rt.ColorValue!.Value.R, Is.EqualTo(0.1f));
        }

        [Test]
        public void VolumeOverrideParamValueDto_Vector3Kind_RoundtripsViaVector4Carrier()
        {
            var v = new VolumeOverrideParamValueDto(
                ParamKind.Vector3, null, null, null, null,
                new Vector4Dto(1f, 2f, 3f, 0f), null);
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamValueDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.VectorValue, Is.Not.Null);
            Assert.That(rt.VectorValue!.Value.X, Is.EqualTo(1f));
            Assert.That(rt.VectorValue!.Value.Z, Is.EqualTo(3f));
        }

        [Test]
        public void VolumeOverrideParamValueDto_EnumKind_Roundtrips()
        {
            var v = new VolumeOverrideParamValueDto(
                ParamKind.Enum, null, null, null, null, null, "ACES");
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamValueDto>(
                JsonSerializer.Serialize(v, Options), Options);
            Assert.That(rt.EnumValue, Is.EqualTo("ACES"));
        }

        [Test]
        public void VolumeOverrideParamRangeDto_FloatRange_Roundtrips()
        {
            var r = new VolumeOverrideParamRangeDto(0f, 10f, null, null, null);
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamRangeDto>(
                JsonSerializer.Serialize(r, Options), Options);
            Assert.That(rt.FloatMin, Is.EqualTo(0f));
            Assert.That(rt.FloatMax, Is.EqualTo(10f));
            Assert.That(rt.EnumValues, Is.Null);
        }

        [Test]
        public void VolumeOverrideParamRangeDto_EnumValues_Roundtrips()
        {
            var r = new VolumeOverrideParamRangeDto(null, null, null, null, new[] { "ACES", "Neutral" });
            var rt = JsonSerializer.Deserialize<VolumeOverrideParamRangeDto>(
                JsonSerializer.Serialize(r, Options), Options);
            Assert.That(rt.EnumValues, Is.Not.Null);
            Assert.That(rt.EnumValues, Has.Count.EqualTo(2));
            Assert.That(rt.EnumValues![0], Is.EqualTo("ACES"));
        }

        [Test]
        public void VolumeOverrideSchemaDto_RoundtripsWithUnknownKind()
        {
            // Unknown kind must round-trip so the UI can later skip + log (Req 6.10).
            var schema = new VolumeOverrideSchemaDto(
                SchemaVersion: 1,
                Types: new[]
                {
                    new VolumeOverrideTypeDto(
                        TypeFullName: "UnityEngine.Rendering.Universal.Bloom",
                        DisplayName: "Bloom",
                        Params: new[]
                        {
                            new VolumeOverrideParamDto(
                                ParamName: "intensity",
                                Kind: ParamKind.Float,
                                DisplayName: "Intensity",
                                DefaultValue: new VolumeOverrideParamValueDto(
                                    ParamKind.Float, null, null, 1f, null, null, null),
                                Range: new VolumeOverrideParamRangeDto(0f, 10f, null, null, null)),
                            new VolumeOverrideParamDto(
                                ParamName: "futureField",
                                Kind: ParamKind.Unknown,
                                DisplayName: "Future Field",
                                DefaultValue: new VolumeOverrideParamValueDto(
                                    ParamKind.Unknown, null, null, null, null, null, null),
                                Range: null),
                        }),
                });

            var json = JsonSerializer.Serialize(schema, Options);
            var rt = JsonSerializer.Deserialize<VolumeOverrideSchemaDto>(json, Options);

            Assert.That(rt.SchemaVersion, Is.EqualTo(1));
            Assert.That(rt.Types, Has.Count.EqualTo(1));
            Assert.That(rt.Types[0].Params, Has.Count.EqualTo(2));
            Assert.That(rt.Types[0].Params[1].Kind, Is.EqualTo(ParamKind.Unknown));
        }
    }
}
