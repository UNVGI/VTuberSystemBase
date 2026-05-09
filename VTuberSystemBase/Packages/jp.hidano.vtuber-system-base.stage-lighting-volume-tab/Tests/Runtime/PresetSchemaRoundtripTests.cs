#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the preset JSON schema (Task 2.5, Requirements 7.1, 7.8, 7.9, 8.1, 8.2).
    /// Tests cover stage-with-key/no-key, multiple lights, multiple Volume Overrides,
    /// unknown JSON fields being silently ignored, and detection of an unsupported
    /// <see cref="PresetFileRoot.SchemaVersion"/> so a future migrator can hook in.
    /// </summary>
    [TestFixture]
    public sealed class PresetSchemaRoundtripTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        [Test]
        public void PresetFileRoot_DefaultsToSchemaVersionOne()
        {
            var root = new PresetFileRoot();
            Assert.That(root.SchemaVersion, Is.EqualTo(1));
            Assert.That(root.ActivePresetName, Is.Null);
            Assert.That(root.Presets, Is.Empty);
        }

        [Test]
        public void PresetFileRoot_RoundtripsWithStageKeyAndMultiplePresetEntries()
        {
            var root = new PresetFileRoot
            {
                SchemaVersion = 1,
                ActivePresetName = "Daylight",
                Presets = new List<PresetDto>
                {
                    new PresetDto
                    {
                        Name = "Daylight",
                        StageAddressableKey = "stages/concert-hall",
                        Lights = new List<LightConfigDto>
                        {
                            new LightConfigDto
                            {
                                DisplayName = "Sun",
                                Type = LightTypeDto.Directional,
                                Rotation = new Vector3Dto(50f, -30f, 0f),
                                Color = new ColorDto(1f, 1f, 0.95f, 1f),
                                Intensity = 1.2f,
                                Range = 0f,
                                SpotAngle = 30f,
                            },
                            new LightConfigDto
                            {
                                DisplayName = "Fill",
                                Type = LightTypeDto.Point,
                                Rotation = new Vector3Dto(0, 0, 0),
                                Color = new ColorDto(0.9f, 0.95f, 1f, 1f),
                                Intensity = 0.5f,
                                Range = 8f,
                                SpotAngle = 30f,
                            },
                        },
                        VolumeOverrides = new List<VolumeOverrideConfigDto>
                        {
                            new VolumeOverrideConfigDto
                            {
                                TypeFullName = "UnityEngine.Rendering.Universal.Bloom",
                                Enabled = true,
                                Params = new Dictionary<string, VolumeOverrideParamValueDto>
                                {
                                    ["intensity"] = new VolumeOverrideParamValueDto(
                                        ParamKind.Float, null, null, 0.5f, null, null, null),
                                },
                            },
                        },
                    },
                    new PresetDto
                    {
                        Name = "NoStage",
                        StageAddressableKey = null,
                        Lights = new List<LightConfigDto>(),
                        VolumeOverrides = new List<VolumeOverrideConfigDto>(),
                    },
                },
            };

            var json = JsonSerializer.Serialize(root, Options);
            var rt = JsonSerializer.Deserialize<PresetFileRoot>(json, Options)!;

            Assert.That(rt.SchemaVersion, Is.EqualTo(1));
            Assert.That(rt.ActivePresetName, Is.EqualTo("Daylight"));
            Assert.That(rt.Presets, Has.Count.EqualTo(2));
            Assert.That(rt.Presets[0].Name, Is.EqualTo("Daylight"));
            Assert.That(rt.Presets[0].StageAddressableKey, Is.EqualTo("stages/concert-hall"));
            Assert.That(rt.Presets[0].Lights, Has.Count.EqualTo(2));
            Assert.That(rt.Presets[0].Lights[0].DisplayName, Is.EqualTo("Sun"));
            Assert.That(rt.Presets[0].VolumeOverrides, Has.Count.EqualTo(1));
            Assert.That(rt.Presets[0].VolumeOverrides[0].Enabled, Is.True);
            Assert.That(rt.Presets[0].VolumeOverrides[0].Params["intensity"].FloatValue, Is.EqualTo(0.5f));
            Assert.That(rt.Presets[1].StageAddressableKey, Is.Null);
        }

        [Test]
        public void LightConfigDto_DoesNotPersistLightId()
        {
            // SL-8 / Requirement 7.8: lightId is reissued on restore, never persisted.
            var entry = new LightConfigDto { DisplayName = "X" };
            var props = entry.GetType().GetProperties();
            foreach (var p in props)
            {
                Assert.That(p.Name, Is.Not.EqualTo("LightId"),
                    "LightConfigDto must not expose LightId (SL-8 / Req 7.8).");
                Assert.That(p.Name, Is.Not.EqualTo("Id"),
                    "LightConfigDto must not expose any persistent identifier (SL-8 / Req 7.8).");
            }
        }

        [Test]
        public void PresetFileRoot_IgnoresUnknownJsonFieldsOnLoad()
        {
            // Forward compatibility: a future-version field must not crash the parser.
            const string jsonWithUnknownField =
                "{ \"schemaVersion\": 1, \"activePresetName\": null, \"presets\": [], \"futureField\": 42 }";

            var rt = JsonSerializer.Deserialize<PresetFileRoot>(jsonWithUnknownField, Options)!;

            Assert.That(rt.SchemaVersion, Is.EqualTo(1));
            Assert.That(rt.Presets, Is.Empty);
        }

        [Test]
        public void PresetFileRoot_LoadingFutureSchemaVersion_IsObservable()
        {
            // The migrator hook is out of scope for this spec, but the parsed
            // SchemaVersion must round-trip so a future migrator can detect and
            // upgrade it on load.
            const string futureJson = "{ \"schemaVersion\": 99, \"presets\": [] }";

            var rt = JsonSerializer.Deserialize<PresetFileRoot>(futureJson, Options)!;

            Assert.That(rt.SchemaVersion, Is.EqualTo(99),
                "An unknown SchemaVersion must be observable so the loader can detect it.");
        }
    }
}
