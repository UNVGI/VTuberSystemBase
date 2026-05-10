#nullable enable
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    [TestFixture]
    public sealed class JsonPresetStorageTests
    {
        private string _tempDir = "";

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "vsb-character-tab-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void Teardown()
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        [Test]
        public async Task SaveLoad_Roundtrips()
        {
            var s = new JsonPresetStorage(_tempDir);
            var record = new PresetRecord
            {
                Header = new PresetHeader { PresetId = "p1", Name = "Morning", LastModifiedAt = DateTimeOffset.UtcNow },
                Assignments = new Dictionary<string, string?> { { "slot-01", "avatars/alice" } },
                Settings = new Dictionary<string, IReadOnlyDictionary<string, SettingValue>>
                {
                    {
                        "slot-01",
                        new Dictionary<string, SettingValue> { { "smile", SettingValue.Float(0.5f) } }
                    },
                },
            };
            await s.SaveAsync(record, default);
            var all = await s.LoadAllAsync(default);
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("Morning", all[0].Header.Name);
            Assert.AreEqual("avatars/alice", all[0].Assignments["slot-01"]);
            Assert.AreEqual(0.5f, all[0].Settings["slot-01"]["smile"].FloatValue);
        }

        [Test]
        public async Task ActiveFile_RoundtripsThroughSetAndLoad()
        {
            var s = new JsonPresetStorage(_tempDir);
            await s.SetActiveAsync("p1", default);
            Assert.AreEqual("p1", await s.LoadActivePresetIdAsync(default));
            await s.SetActiveAsync(null, default);
            Assert.IsNull(await s.LoadActivePresetIdAsync(default));
        }

        [Test]
        public async Task CorruptedFile_BackedUpAndCounted()
        {
            File.WriteAllText(Path.Combine(_tempDir, "broken.json"), "{ this is not json }");
            var s = new JsonPresetStorage(_tempDir);
            var report = await s.CheckHealthAsync(default);
            Assert.AreEqual(1, report.CorruptedCount);
            Assert.AreEqual(1, report.BackedUpFiles.Count);
            Assert.IsTrue(report.BackedUpFiles[0].Contains(".bak."));
        }

        [Test]
        public async Task Delete_RemovesFile()
        {
            var s = new JsonPresetStorage(_tempDir);
            await s.SaveAsync(new PresetRecord { Header = new PresetHeader { PresetId = "p2", Name = "x" } }, default);
            Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "p2.json")));
            await s.DeleteAsync("p2", default);
            Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "p2.json")));
        }
    }
}
