#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using System.Text.Json;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 1.3 acceptance test: every payload DTO survives a JSON serialise /
    /// deserialise roundtrip without losing fields. Also asserts forward
    /// compatibility: unknown JSON fields are ignored on the way in.
    /// </summary>
    [TestFixture]
    public sealed class PayloadRoundtripTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = false,
        };

        [Test]
        public void SlotCatalogPayload_Roundtrips()
        {
            var src = new SlotCatalogPayload
            {
                Slots = new List<SlotCatalogEntry>
                {
                    new SlotCatalogEntry { SlotId = "slot-01", DisplayName = "P1", OrderHint = 0 },
                    new SlotCatalogEntry { SlotId = "slot-02", DisplayName = null, OrderHint = 1 },
                },
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<SlotCatalogPayload>(json, Options)!;
            Assert.AreEqual(2, back.Slots.Count);
            Assert.AreEqual("slot-01", back.Slots[0].SlotId);
            Assert.AreEqual("P1", back.Slots[0].DisplayName);
            Assert.IsNull(back.Slots[1].DisplayName);
        }

        [Test]
        public void AvatarCatalogPayload_Roundtrips()
        {
            var src = new AvatarCatalogPayload
            {
                Avatars = new List<AvatarCatalogEntry>
                {
                    new AvatarCatalogEntry { AvatarKey = "avatars/alice", DisplayName = "Alice" },
                },
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<AvatarCatalogPayload>(json, Options)!;
            Assert.AreEqual(1, back.Avatars.Count);
            Assert.AreEqual("avatars/alice", back.Avatars[0].AvatarKey);
        }

        [Test]
        public void SlotAssignmentPayload_NullAvatarKeyIsEmptyState()
        {
            var src = new SlotAssignmentPayload { AvatarKey = null };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<SlotAssignmentPayload>(json, Options)!;
            Assert.IsNull(back.AvatarKey);
        }

        [Test]
        public void SlotStatusPayload_StringStatusUnchanged()
        {
            var src = new SlotStatusPayload { Status = "Assigned", Detail = "ok" };
            var back = JsonSerializer.Deserialize<SlotStatusPayload>(JsonSerializer.Serialize(src, Options), Options)!;
            Assert.AreEqual("Assigned", back.Status);
            Assert.AreEqual("ok", back.Detail);
        }

        [Test]
        public void SlotCommandPayload_DefaultIsReset()
        {
            var src = new SlotCommandPayload();
            Assert.AreEqual("Reset", src.Kind);
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<SlotCommandPayload>(json, Options)!;
            Assert.AreEqual("Reset", back.Kind);
            Assert.IsNull(back.Argument);
        }

        [Test]
        public void SlotErrorPayload_UnknownErrorCodeAccepted()
        {
            // Forward-compatible: a future ErrorCode must roundtrip as the literal string,
            // never as a hard parse failure.
            var src = new SlotErrorPayload { ErrorCode = "FutureCategory", Detail = "n/a" };
            var back = JsonSerializer.Deserialize<SlotErrorPayload>(JsonSerializer.Serialize(src, Options), Options)!;
            Assert.AreEqual("FutureCategory", back.ErrorCode);
        }

        [Test]
        public void AvatarSettingsSchemaPayload_Roundtrips()
        {
            var src = new AvatarSettingsSchemaPayload
            {
                AvatarKey = "avatars/alice",
                Settings = new List<SettingSchemaEntry>
                {
                    new SettingSchemaEntry
                    {
                        Key = "expression.smile",
                        Label = "Smile",
                        Type = SettingType.Float,
                        Step = 0.01f,
                        Unit = null,
                    },
                },
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<AvatarSettingsSchemaPayload>(json, Options)!;
            Assert.AreEqual("avatars/alice", back.AvatarKey);
            Assert.AreEqual(1, back.Settings.Count);
            Assert.AreEqual(SettingType.Float, back.Settings[0].Type);
            Assert.AreEqual(0.01f, back.Settings[0].Step);
        }

        [Test]
        public void SlotSettingValuePayload_CarriesJsonElement()
        {
            using var doc = JsonDocument.Parse("0.5");
            var src = new SlotSettingValuePayload
            {
                SettingKey = "expression.smile",
                Type = SettingType.Float,
                Value = doc.RootElement.Clone(),
            };
            var json = JsonSerializer.Serialize(src, Options);
            var back = JsonSerializer.Deserialize<SlotSettingValuePayload>(json, Options)!;
            Assert.AreEqual(SettingType.Float, back.Type);
            Assert.AreEqual(JsonValueKind.Number, back.Value.ValueKind);
            Assert.AreEqual(0.5f, back.Value.GetSingle());
        }

        [Test]
        public void UnknownFields_AreIgnored()
        {
            // Forward-compat: a future server adds 'newField' — the existing DTO must still parse.
            const string json = "{\"AvatarKey\":\"k\",\"newField\":42,\"DisplayName\":\"k\"}";
            var back = JsonSerializer.Deserialize<AvatarCatalogEntry>(json, Options)!;
            Assert.AreEqual("k", back.AvatarKey);
        }
    }
}
