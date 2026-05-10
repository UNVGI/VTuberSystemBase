#nullable enable
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the JSON wire format for the Stage family of DTOs (Task 2.2, Requirements
    /// 3.1, 3.4, 3.5, 3.6, 3.8).
    /// </summary>
    [TestFixture]
    public sealed class StageDtosJsonRoundtripTests
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        [Test]
        public void StageCatalogEntryDto_RoundtripsThroughJson()
        {
            var original = new StageCatalogEntryDto(
                AddressableKey: "stages/concert-hall",
                DisplayName: "Concert Hall",
                ThumbnailAddressableKey: "stages/concert-hall.thumb");

            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<StageCatalogEntryDto>(json, Options);

            Assert.That(roundtripped, Is.EqualTo(original));
        }

        [Test]
        public void StageCatalogEntryDto_RoundtripsWithNullThumbnail()
        {
            var original = new StageCatalogEntryDto("k", "K", null);
            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<StageCatalogEntryDto>(json, Options);
            Assert.That(roundtripped.ThumbnailAddressableKey, Is.Null);
        }

        [Test]
        public void StageCatalogDto_RoundtripsItemsArray()
        {
            var original = new StageCatalogDto(new[]
            {
                new StageCatalogEntryDto("a", "A", null),
                new StageCatalogEntryDto("b", "B", "thumb-b"),
            });

            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<StageCatalogDto>(json, Options);

            Assert.That(roundtripped.Items, Has.Count.EqualTo(2));
            Assert.That(roundtripped.Items[1].ThumbnailAddressableKey, Is.EqualTo("thumb-b"));
        }

        [Test]
        public void StageCurrentDto_RoundtripsLoadedAndUnloaded()
        {
            var loaded = new StageCurrentDto("stages/concert-hall");
            var unloaded = new StageCurrentDto((string?)null);

            var rtLoaded = JsonSerializer.Deserialize<StageCurrentDto>(
                JsonSerializer.Serialize(loaded, Options), Options);
            var rtUnloaded = JsonSerializer.Deserialize<StageCurrentDto>(
                JsonSerializer.Serialize(unloaded, Options), Options);

            Assert.That(rtLoaded.AddressableKey, Is.EqualTo("stages/concert-hall"));
            Assert.That(rtUnloaded.AddressableKey, Is.Null);
        }

        [Test]
        public void StageCommandDto_LoadVariant_HasAddressableKey()
        {
            var loadCmd = new StageCommandDto("load", "stages/x");
            var json = JsonSerializer.Serialize(loadCmd, Options);
            var roundtripped = JsonSerializer.Deserialize<StageCommandDto>(json, Options);

            Assert.That(roundtripped.Op, Is.EqualTo("load"));
            Assert.That(roundtripped.AddressableKey, Is.EqualTo("stages/x"));
        }

        [Test]
        public void StageCommandDto_UnloadVariant_HasNullAddressableKey()
        {
            var unloadCmd = new StageCommandDto("unload", null);
            var json = JsonSerializer.Serialize(unloadCmd, Options);
            var roundtripped = JsonSerializer.Deserialize<StageCommandDto>(json, Options);

            Assert.That(roundtripped.Op, Is.EqualTo("unload"));
            Assert.That(roundtripped.AddressableKey, Is.Null);
        }

        [Test]
        public void StageLoadFailedDto_RoundtripsThroughJson()
        {
            var original = new StageLoadFailedDto(
                AddressableKey: "stages/missing",
                ErrorCode: "not_found",
                Message: "no such addressable");

            var json = JsonSerializer.Serialize(original, Options);
            var roundtripped = JsonSerializer.Deserialize<StageLoadFailedDto>(json, Options);

            Assert.That(roundtripped, Is.EqualTo(original));
        }
    }
}
