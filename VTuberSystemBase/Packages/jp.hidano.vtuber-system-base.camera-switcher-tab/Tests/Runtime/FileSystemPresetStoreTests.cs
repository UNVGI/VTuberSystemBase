#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Persistence;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Round-trip + corruption-quarantine + write-failure scenarios for
    /// <see cref="FileSystemPresetStore"/>. Uses a temp directory under
    /// <c>Path.GetTempPath()</c> so the test does not pollute the user's
    /// <c>persistentDataPath</c>.
    /// </summary>
    [TestFixture]
    public sealed class FileSystemPresetStoreTests
    {
        private string _dir = "";
        private string _file = "";

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vsb-camera-switcher-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "presets.json");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            }
            catch { /* best-effort */ }
        }

        [Test]
        public async Task LoadAllAsync_FileMissing_ReturnsFileNotFound()
        {
            var sut = new FileSystemPresetStore(_file);
            var outcome = await sut.LoadAllAsync();
            Assert.IsFalse(outcome.Result.Success);
            Assert.AreEqual(PresetIoFailureKind.FileNotFound, outcome.Result.FailureKind);
            Assert.AreEqual(0, outcome.Presets.Count);
            Assert.IsNull(outcome.ActivePresetName);
        }

        [Test]
        public async Task SaveAllAsync_ThenLoadAllAsync_RoundTrips()
        {
            var sut = new FileSystemPresetStore(_file);
            var preset = new PresetPayload
            {
                Name = "alpha",
                Cameras = new List<PresetCameraEntry>
                {
                    new PresetCameraEntry
                    {
                        LogicalId = "logical-1",
                        DisplayName = "Cam One",
                        Type = CameraType.Perspective,
                        DefaultTransform = new CameraDefaultTransform
                        {
                            Position = new float[] { 1f, 2f, 3f },
                            Rotation = new float[] { 0f, 0f, 0f, 1f },
                            FocalLengthMm = 35f,
                        },
                    },
                },
                VolumeConfigs = new Dictionary<string, VolumeConfig>
                {
                    ["logical-1"] = new VolumeConfig
                    {
                        Enabled = true,
                        Overrides = new List<VolumeOverride>(),
                    },
                },
                ActiveCameraLogicalId = "logical-1",
            };

            var save = await sut.SaveAllAsync(new[] { preset }, "alpha");
            Assert.IsTrue(save.Success, save.FailureDetail);

            var loaded = await sut.LoadAllAsync();
            Assert.IsTrue(loaded.Result.Success);
            Assert.AreEqual(1, loaded.Presets.Count);
            Assert.AreEqual("alpha", loaded.ActivePresetName);
            var first = loaded.Presets[0];
            Assert.AreEqual("alpha", first.Name);
            Assert.AreEqual("logical-1", first.Cameras[0].LogicalId);
            Assert.AreEqual(CameraType.Perspective, first.Cameras[0].Type);
            Assert.AreEqual(35f, first.Cameras[0].DefaultTransform.FocalLengthMm);
            Assert.IsTrue(first.VolumeConfigs.ContainsKey("logical-1"));
        }

        [Test]
        public async Task LoadAllAsync_CorruptedJson_QuarantinesAndReturnsCorrupted()
        {
            File.WriteAllText(_file, "{ this-is-not-json :::");
            var sut = new FileSystemPresetStore(_file);
            var outcome = await sut.LoadAllAsync();

            Assert.IsFalse(outcome.Result.Success);
            Assert.AreEqual(PresetIoFailureKind.Corrupted, outcome.Result.FailureKind);
            Assert.IsNotNull(outcome.BackupPath);
            Assert.IsTrue(File.Exists(outcome.BackupPath));
            Assert.IsFalse(File.Exists(_file)); // moved away
            StringAssert.Contains(".bak.", outcome.BackupPath);
        }

        [Test]
        public async Task SaveAllAsync_AtomicWrite_DoesNotLeaveTempFile()
        {
            var sut = new FileSystemPresetStore(_file);
            var preset = new PresetPayload
            {
                Name = "beta",
                Cameras = new List<PresetCameraEntry>(),
                VolumeConfigs = new Dictionary<string, VolumeConfig>(),
            };
            await sut.SaveAllAsync(new[] { preset }, null);

            // No leftover .tmp file.
            var tmp = _file + ".tmp";
            Assert.IsFalse(File.Exists(tmp), "Temp file should have been renamed away");
            Assert.IsTrue(File.Exists(_file));
        }

        [Test]
        public async Task SaveAllAsync_WriteFailure_ReturnsWriteFailed()
        {
            // Point to a path inside a file (not a dir) — write will fail with IOException.
            var conflictDir = Path.Combine(_dir, "blocked");
            File.WriteAllText(conflictDir, "not-a-dir");
            var blockedFile = Path.Combine(conflictDir, "presets.json");
            var sut = new FileSystemPresetStore(blockedFile);

            var preset = new PresetPayload
            {
                Name = "x",
                Cameras = new List<PresetCameraEntry>(),
                VolumeConfigs = new Dictionary<string, VolumeConfig>(),
            };
            var save = await sut.SaveAllAsync(new[] { preset }, null);

            Assert.IsFalse(save.Success);
            Assert.AreEqual(PresetIoFailureKind.WriteFailed, save.FailureKind);
        }

        [Test]
        public async Task SaveAllAsync_OverwritesExistingFile()
        {
            var sut = new FileSystemPresetStore(_file);
            var p1 = new PresetPayload { Name = "v1", Cameras = new List<PresetCameraEntry>(), VolumeConfigs = new Dictionary<string, VolumeConfig>() };
            var p2 = new PresetPayload { Name = "v2", Cameras = new List<PresetCameraEntry>(), VolumeConfigs = new Dictionary<string, VolumeConfig>() };

            await sut.SaveAllAsync(new[] { p1 }, "v1");
            await sut.SaveAllAsync(new[] { p2 }, "v2");

            var loaded = await sut.LoadAllAsync();
            Assert.IsTrue(loaded.Result.Success);
            Assert.AreEqual(1, loaded.Presets.Count);
            Assert.AreEqual("v2", loaded.Presets[0].Name);
            Assert.AreEqual("v2", loaded.ActivePresetName);
        }
    }
}
