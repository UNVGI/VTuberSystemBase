#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Task 1.2 acceptance test: every payload DTO survives a System.Text.Json
    /// serialise / deserialise roundtrip without losing fields. Also asserts
    /// forward-compatible defaults: optional / null fields keep null after the
    /// round-trip and unknown JSON keys are ignored on the way in.
    /// </summary>
    [TestFixture]
    public sealed class PayloadRoundtripTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = false,
        };

        // ---- Camera management ----

        [Test]
        public void CameraCommandPayload_AddRoundtrips()
        {
            var src = new CameraCommandPayload
            {
                Op = CameraCommandOps.Add,
                ClientRequestId = "req-1",
                Type = CameraTypeNames.Perspective,
                DisplayName = "Cam 1",
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<CameraCommandPayload>(json, Options);
            Assert.AreEqual("add", back.Op);
            Assert.AreEqual("req-1", back.ClientRequestId);
            Assert.AreEqual("Perspective", back.Type);
            Assert.AreEqual("Cam 1", back.DisplayName);
            Assert.IsNull(back.CameraId);
        }

        [Test]
        public void CameraCommandPayload_DeleteOmitsTypeAndDisplayName()
        {
            var src = new CameraCommandPayload
            {
                Op = CameraCommandOps.Delete,
                ClientRequestId = "req-2",
                CameraId = "cam-a",
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<CameraCommandPayload>(json, Options);
            Assert.AreEqual("delete", back.Op);
            Assert.AreEqual("cam-a", back.CameraId);
            Assert.IsNull(back.Type);
            Assert.IsNull(back.DisplayName);
        }

        [Test]
        public void CamerasListPayload_Roundtrips()
        {
            var src = new CamerasListPayload
            {
                UpdatedAtUnixMs = 1_700_000_000_000L,
                Cameras = new List<CameraListEntry>
                {
                    new CameraListEntry
                    {
                        CameraId = "cam-a",
                        DisplayName = "Cam A",
                        Type = CameraTypeNames.Perspective,
                        DefaultTransform = new CameraDefaultTransform
                        {
                            Position = new float[] { 0f, 1f, -2f },
                            Rotation = new float[] { 0f, 0f, 0f, 1f },
                            FocalLengthMm = 35f,
                        },
                    },
                },
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<CamerasListPayload>(json, Options);
            Assert.AreEqual(1_700_000_000_000L, back.UpdatedAtUnixMs);
            Assert.AreEqual(1, back.Cameras.Count);
            Assert.AreEqual("cam-a", back.Cameras[0].CameraId);
            Assert.AreEqual(35f, back.Cameras[0].DefaultTransform.FocalLengthMm);
            Assert.AreEqual(3, back.Cameras[0].DefaultTransform.Position.Length);
            Assert.AreEqual(4, back.Cameras[0].DefaultTransform.Rotation.Length);
        }

        [Test]
        public void CamerasActiveStatePayload_NullActiveIsAllowed()
        {
            var src = new CamerasActiveStatePayload { ActiveCameraId = null, UpdatedAtUnixMs = 12345L };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<CamerasActiveStatePayload>(json, Options);
            Assert.IsNull(back.ActiveCameraId);
            Assert.AreEqual(12345L, back.UpdatedAtUnixMs);
        }

        [Test]
        public void CameraCreatedEventPayload_CarriesMetadata()
        {
            var src = new CameraCreatedEventPayload
            {
                ClientRequestId = "req-1",
                CameraId = "cam-a",
                Metadata = new CameraListEntry
                {
                    CameraId = "cam-a",
                    DisplayName = "A",
                    Type = CameraTypeNames.Perspective,
                    DefaultTransform = new CameraDefaultTransform
                    {
                        Position = new float[] { 0f, 0f, 0f },
                        Rotation = new float[] { 0f, 0f, 0f, 1f },
                        FocalLengthMm = 50f,
                    },
                },
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<CameraCreatedEventPayload>(json, Options);
            Assert.AreEqual("req-1", back.ClientRequestId);
            Assert.AreEqual("cam-a", back.Metadata.CameraId);
        }

        [Test]
        public void CameraErrorEventPayload_AllFieldsOptionalExceptOpAndReason()
        {
            var src = new CameraErrorEventPayload
            {
                Op = CameraCommandOps.Add,
                Reason = CameraErrorReasons.ResourceExhausted,
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<CameraErrorEventPayload>(json, Options);
            Assert.AreEqual("add", back.Op);
            Assert.AreEqual("ResourceExhausted", back.Reason);
            Assert.IsNull(back.ClientRequestId);
            Assert.IsNull(back.CameraId);
            Assert.IsNull(back.Detail);
        }

        [Test]
        public void CameraErrorEventPayload_UnknownReasonPreserved()
        {
            // Forward-compatible: unknown wire reason MUST roundtrip as the literal string.
            var src = new CameraErrorEventPayload { Op = "add", Reason = "FutureReasonCode" };
            var back = JsonSerializer.Deserialize<CameraErrorEventPayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual("FutureReasonCode", back.Reason);
        }

        [Test]
        public void CameraMetadataStatePayload_CarriesJsonElement()
        {
            using var doc = JsonDocument.Parse("\"Display Name 1\"");
            var src = new CameraMetadataStatePayload { Value = doc.RootElement.Clone() };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<CameraMetadataStatePayload>(json, Options);
            Assert.AreEqual(JsonValueKind.String, back.Value.ValueKind);
            Assert.AreEqual("Display Name 1", back.Value.GetString());
        }

        // ---- Volume ----

        [Test]
        public void VolumeCommandPayload_Roundtrips()
        {
            var src = new VolumeCommandPayload
            {
                Op = VolumeCommandOps.OverrideAdd,
                OverrideType = "Bloom",
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<VolumeCommandPayload>(json, Options);
            Assert.AreEqual("override-add", back.Op);
            Assert.AreEqual("Bloom", back.OverrideType);
        }

        [Test]
        public void VolumeEnabledStatePayload_BoolRoundtrips()
        {
            var src = new VolumeEnabledStatePayload { Enabled = true };
            var back = JsonSerializer.Deserialize<VolumeEnabledStatePayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.IsTrue(back.Enabled);
        }

        [Test]
        public void VolumeOverrideEnabledStatePayload_BoolRoundtrips()
        {
            var src = new VolumeOverrideEnabledStatePayload { Enabled = false };
            var back = JsonSerializer.Deserialize<VolumeOverrideEnabledStatePayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.IsFalse(back.Enabled);
        }

        [Test]
        public void VolumeOverrideParamStatePayload_FloatRoundtrips()
        {
            using var doc = JsonDocument.Parse("0.75");
            var src = new VolumeOverrideParamStatePayload { Value = doc.RootElement.Clone() };
            var back = JsonSerializer.Deserialize<VolumeOverrideParamStatePayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual(JsonValueKind.Number, back.Value.ValueKind);
            Assert.AreEqual(0.75f, back.Value.GetSingle());
        }

        [Test]
        public void VolumeOverridesStatePayload_Roundtrips()
        {
            var src = new VolumeOverridesStatePayload
            {
                Overrides = new List<VolumeOverrideEntry>
                {
                    new VolumeOverrideEntry { Type = "Bloom", Enabled = true },
                    new VolumeOverrideEntry { Type = "Tonemapping", Enabled = false },
                },
            };
            var back = JsonSerializer.Deserialize<VolumeOverridesStatePayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual(2, back.Overrides.Count);
            Assert.AreEqual("Bloom", back.Overrides[0].Type);
            Assert.IsFalse(back.Overrides[1].Enabled);
        }

        [Test]
        public void VolumeMetadataResponse_Roundtrips()
        {
            using var defDoc = JsonDocument.Parse("0.5");
            var src = new VolumeMetadataResponse
            {
                Overrides = new List<VolumeOverrideSchema>
                {
                    new VolumeOverrideSchema
                    {
                        Type = "Bloom",
                        DisplayName = "Bloom",
                        Params = new List<VolumeParamSchema>
                        {
                            new VolumeParamSchema
                            {
                                Name = "intensity",
                                TypeTag = "float",
                                DisplayName = "Intensity",
                                Default = defDoc.RootElement.Clone(),
                                Unit = null,
                                EnumValues = null,
                            },
                        },
                    },
                },
            };
            var back = JsonSerializer.Deserialize<VolumeMetadataResponse>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual(1, back.Overrides.Count);
            Assert.AreEqual("Bloom", back.Overrides[0].Type);
            Assert.AreEqual(1, back.Overrides[0].Params.Count);
            Assert.AreEqual("intensity", back.Overrides[0].Params[0].Name);
            Assert.AreEqual(0.5f, back.Overrides[0].Params[0].Default.GetSingle());
            Assert.IsNull(back.Overrides[0].Params[0].EnumValues);
        }

        // ---- Preset ----

        [Test]
        public void PresetCommandPayload_RenameRoundtrips()
        {
            var src = new PresetCommandPayload
            {
                Op = PresetCommandOps.Rename,
                Name = "old",
                NewName = "new",
            };
            var back = JsonSerializer.Deserialize<PresetCommandPayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual("rename", back.Op);
            Assert.AreEqual("old", back.Name);
            Assert.AreEqual("new", back.NewName);
            Assert.IsNull(back.SourceName);
        }

        [Test]
        public void PresetListStatePayload_Roundtrips()
        {
            var src = new PresetListStatePayload
            {
                Names = new[] { "a", "b", "c" },
            };
            var back = JsonSerializer.Deserialize<PresetListStatePayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual(3, back.Names.Count);
            Assert.AreEqual("b", back.Names[1]);
        }

        [Test]
        public void PresetActiveStatePayload_NullActiveAllowed()
        {
            var src = new PresetActiveStatePayload { ActiveName = null };
            var back = JsonSerializer.Deserialize<PresetActiveStatePayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.IsNull(back.ActiveName);
        }

        // ---- Preview ----

        [Test]
        public void PreviewCommandPayload_AttachRoundtrips()
        {
            var src = new PreviewCommandPayload
            {
                Op = PreviewCommandOps.Attach,
                CameraIds = new[] { "cam-a", "cam-b" },
                Size = new[] { 192, 108 },
                Fps = 15,
            };
            var back = JsonSerializer.Deserialize<PreviewCommandPayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual("attach", back.Op);
            Assert.AreEqual(2, back.CameraIds.Count);
            Assert.AreEqual(192, back.Size![0]);
            Assert.AreEqual(15, back.Fps);
        }

        [Test]
        public void PreviewCommandPayload_DetachOmitsSizeAndFps()
        {
            var src = new PreviewCommandPayload
            {
                Op = PreviewCommandOps.Detach,
                CameraIds = new[] { "cam-a" },
            };
            var back = JsonSerializer.Deserialize<PreviewCommandPayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual("detach", back.Op);
            Assert.AreEqual(1, back.CameraIds.Count);
            Assert.IsNull(back.Size);
            Assert.IsNull(back.Fps);
        }

        [Test]
        public void PreviewHandleStatePayload_Roundtrips()
        {
            var src = new PreviewHandleStatePayload
            {
                TextureKey = "preview/cam-a",
                Size = new[] { 640, 360 },
                Fps = 60,
            };
            var back = JsonSerializer.Deserialize<PreviewHandleStatePayload>(
                JsonSerializer.Serialize(src, Options), Options);
            Assert.AreEqual("preview/cam-a", back.TextureKey);
            Assert.AreEqual(640, back.Size[0]);
            Assert.AreEqual(60, back.Fps);
        }

        // ---- Forward compatibility ----

        [Test]
        public void UnknownFields_AreIgnored()
        {
            // A future server adds 'newField' — the existing DTO must still parse.
            const string json =
                "{\"Op\":\"add\",\"ClientRequestId\":\"r1\",\"newField\":42,\"DisplayName\":\"X\"}";
            var back = JsonSerializer.Deserialize<CameraCommandPayload>(json, Options);
            Assert.AreEqual("add", back.Op);
            Assert.AreEqual("X", back.DisplayName);
        }
    }
}
