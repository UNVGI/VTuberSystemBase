#nullable enable
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the JSON wire format for the Light family of DTOs (Task 2.1, Requirements
    /// 4.1, 4.3, 4.4, 4.5, 4.10). Round-trips through System.Text.Json so any drift
    /// between the UI side and the main-output adapter is caught at build time.
    /// </summary>
    [TestFixture]
    public sealed class LightDtosJsonRoundtripTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
        };

        [Test]
        public void LightInitialDto_RoundtripsThroughJson()
        {
            var original = new LightInitialDto(
                Type: LightTypeDto.Spot,
                Rotation: new Vector3Dto(15f, 30f, 0f),
                Color: new ColorDto(1f, 0.8f, 0.6f, 1f),
                Intensity: 2.5f,
                Range: 12.0f,
                SpotAngle: 45f,
                DisplayName: "Stage Key Light");

            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<LightInitialDto>(json, Options);

            Assert.That(roundtripped, Is.EqualTo(original));
        }

        [Test]
        public void LightListItemDto_RoundtripsThroughJson()
        {
            var original = new LightListItemDto("light-1", "Key", LightTypeDto.Directional);
            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<LightListItemDto>(json, Options);
            Assert.That(roundtripped, Is.EqualTo(original));
        }

        [Test]
        public void LightAddedDto_RoundtripsThroughJson()
        {
            var original = new LightAddedDto(
                "light-99",
                new LightInitialDto(
                    LightTypeDto.Point,
                    new Vector3Dto(0, 0, 0),
                    new ColorDto(1, 1, 1, 1),
                    1.0f, 5.0f, 30.0f, "Fill"));
            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<LightAddedDto>(json, Options);
            Assert.That(roundtripped, Is.EqualTo(original));
        }

        [Test]
        public void LightErrorDto_RoundtripsThroughJson()
        {
            var original = new LightErrorDto(
                LightId: null,
                CorrelationId: "corr-42",
                ErrorCode: "limit_exceeded",
                Message: "max lights reached");
            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<LightErrorDto>(json, Options);
            Assert.That(roundtripped, Is.EqualTo(original));
        }

        [Test]
        public void LightCommandDto_AddVariant_HasInitialAndNoLightId()
        {
            var addCmd = new LightCommandDto(
                Op: "add",
                LightId: null,
                Initial: new LightInitialDto(
                    LightTypeDto.Directional,
                    new Vector3Dto(1, 2, 3),
                    new ColorDto(1, 1, 1, 1),
                    1f, 0f, 30f, "Sun"));

            var json = JsonSerializer.Serialize(addCmd, Options);
            var roundtripped = JsonSerializer.Deserialize<LightCommandDto>(json, Options);

            Assert.That(roundtripped.Op, Is.EqualTo("add"));
            Assert.That(roundtripped.LightId, Is.Null);
            Assert.That(roundtripped.Initial, Is.Not.Null);
            Assert.That(roundtripped.Initial!.Value.DisplayName, Is.EqualTo("Sun"));
        }

        [Test]
        public void LightCommandDto_RemoveVariant_HasLightIdAndNoInitial()
        {
            var removeCmd = new LightCommandDto(
                Op: "remove",
                LightId: "light-1",
                Initial: null);

            var json = JsonSerializer.Serialize(removeCmd, Options);
            var roundtripped = JsonSerializer.Deserialize<LightCommandDto>(json, Options);

            Assert.That(roundtripped.Op, Is.EqualTo("remove"));
            Assert.That(roundtripped.LightId, Is.EqualTo("light-1"));
            Assert.That(roundtripped.Initial, Is.Null);
        }

        [Test]
        public void LightListDto_RoundtripsItemsArray()
        {
            var original = new LightListDto(new[]
            {
                new LightListItemDto("a", "A", LightTypeDto.Directional),
                new LightListItemDto("b", "B", LightTypeDto.Point),
            });

            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<LightListDto>(json, Options);

            Assert.That(roundtripped.Items, Has.Count.EqualTo(2));
            Assert.That(roundtripped.Items[0].LightId, Is.EqualTo("a"));
            Assert.That(roundtripped.Items[1].Type, Is.EqualTo(LightTypeDto.Point));
        }
    }
}
