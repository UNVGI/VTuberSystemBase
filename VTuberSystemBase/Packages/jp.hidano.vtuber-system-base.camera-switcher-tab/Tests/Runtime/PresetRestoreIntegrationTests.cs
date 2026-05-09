#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Persistence;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Round-trip + corruption recovery + write-failure retry integration
    /// against a real <see cref="FileSystemPresetStore"/>. The backing temp
    /// directory is removed in <see cref="TearDown"/>.
    /// </summary>
    [TestFixture]
    public sealed class PresetRestoreIntegrationTests
    {
        private string _dir = "";
        private string _file = "";
        private FileSystemPresetStore _store = null!;
        private FakeUiCommandClient _commands = null!;
        private FakeTimeProvider _time = null!;
        private FailureAggregator _failures = null!;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vsb-preset-itests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "presets.json");
            _store = new FileSystemPresetStore(_file);
            _commands = new FakeUiCommandClient();
            _time = new FakeTimeProvider();
            _failures = new FailureAggregator();
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }

        private static PresetPayload Empty(string name) => new PresetPayload
        {
            Name = name,
            Cameras = Array.Empty<PresetCameraEntry>(),
            VolumeConfigs = new Dictionary<string, VolumeConfig>(),
        };

        [Test]
        public async Task CreatePreset_DebounceFlush_ThenRestoreRoundTrips()
        {
            using (var presets = new PresetController(_store, _commands, _time, _failures))
            {
                presets.CreatePreset("alpha", Empty("alpha"));
                _time.Advance(TimeSpan.FromMilliseconds(600));
                await Task.Delay(150);
            }

            // Restore from a fresh controller.
            using var restored = new PresetController(_store, _commands, _time, _failures);
            var result = await restored.RestoreOnStartAsync();
            Assert.IsTrue(result.Success);
            Assert.Contains("alpha", System.Linq.Enumerable.ToList(restored.PresetNames));
        }

        [Test]
        public async Task CorruptedJson_OnRestore_QuarantinedAsBak()
        {
            File.WriteAllText(_file, "not-json");
            using var presets = new PresetController(_store, _commands, _time, _failures);
            var result = await presets.RestoreOnStartAsync();
            Assert.IsFalse(result.Success);
            Assert.GreaterOrEqual(_failures.CountOf(FailureKind.PresetIoFailure), 1);
            // Original file should be replaced with a .bak.{ts} file.
            var entries = Directory.GetFiles(_dir);
            bool foundBak = false;
            foreach (var e in entries)
            {
                if (e.Contains(".bak.")) { foundBak = true; break; }
            }
            Assert.IsTrue(foundBak, "Corruption must move the file to *.bak.{unixMs}");
        }

        [Test]
        public async Task WriteFailure_RecordsAndRetriesOnNextChange()
        {
            // Block writes by putting a file where the directory should be.
            var blockedDir = Path.Combine(_dir, "blocked-as-file");
            File.WriteAllText(blockedDir, "x");
            var blockedFile = Path.Combine(blockedDir, "presets.json");
            var blockedStore = new FileSystemPresetStore(blockedFile);

            using var presets = new PresetController(blockedStore, _commands, _time, _failures);
            presets.CreatePreset("alpha", Empty("alpha"));
            _time.Advance(TimeSpan.FromMilliseconds(600));
            await Task.Delay(100);

            Assert.GreaterOrEqual(_failures.CountOf(FailureKind.PresetIoFailure), 1);

            // Subsequent NotifyStateMutation triggers a fresh attempt.
            presets.NotifyStateMutation();
            _time.Advance(TimeSpan.FromMilliseconds(600));
            await Task.Delay(100);
            // Still failing (still no dir) — but we don't crash and the count keeps growing.
            Assert.GreaterOrEqual(_failures.CountOf(FailureKind.PresetIoFailure), 2);
        }
    }
}
