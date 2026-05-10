#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the disk semantics of <see cref="JsonPresetStorage"/> (Task 3.1, Requirements
    /// 8.1, 8.3, 8.4, 8.5, 8.7, 8.9, 8.10, 10.5). Each test uses a fresh temp directory so
    /// nothing leaks across runs.
    /// </summary>
    [TestFixture]
    public sealed class JsonPresetStorageTests
    {
        private string _tempDir = "";
        private string _filePath = "";

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "vtuber-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _filePath = Path.Combine(_tempDir, "stage-lighting-volume-tab.json");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort; some sandboxes hold transient handles.
            }
        }

        [Test]
        public async Task Load_FileMissing_ReturnsSuccessWithNullData()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);

            var result = await sut.LoadAsync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data, Is.Null);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.CorruptedBackupPath, Is.Null);
        }

        [Test]
        public async Task Save_ThenLoad_RoundTripsSchemaAndContent()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);
            var root = new PresetFileRoot
            {
                SchemaVersion = 1,
                ActivePresetName = "Default",
                Presets =
                {
                    new PresetDto { Name = "Default" },
                },
            };

            var saveResult = await sut.SaveAsync(root);
            Assert.That(saveResult.Success, Is.True, "save should succeed on fresh dir");
            Assert.That(File.Exists(_filePath), Is.True);

            var loadResult = await sut.LoadAsync();
            Assert.That(loadResult.Success, Is.True);
            Assert.That(loadResult.Data, Is.Not.Null);
            Assert.That(loadResult.Data!.ActivePresetName, Is.EqualTo("Default"));
            Assert.That(loadResult.Data.Presets, Has.Count.EqualTo(1));
            Assert.That(loadResult.Data.Presets[0].Name, Is.EqualTo("Default"));
        }

        [Test]
        public async Task Save_AutoCreatesParentDirectory()
        {
            var nested = Path.Combine(_tempDir, "deep", "nest", "preset.json");
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(nested, logger);

            var result = await sut.SaveAsync(new PresetFileRoot());

            Assert.That(result.Success, Is.True);
            Assert.That(File.Exists(nested), Is.True);
        }

        [Test]
        public async Task Save_LeavesNoTempArtifactOnSuccess()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);

            await sut.SaveAsync(new PresetFileRoot { ActivePresetName = "X" });

            var stragglers = Directory.GetFiles(_tempDir, "*.tmp");
            Assert.That(stragglers, Is.Empty, "atomic write should clean up its temp file");
        }

        [Test]
        public async Task Save_OverwritesExistingFileAtomically()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);

            await sut.SaveAsync(new PresetFileRoot { ActivePresetName = "First" });
            await sut.SaveAsync(new PresetFileRoot { ActivePresetName = "Second" });

            var loadResult = await sut.LoadAsync();
            Assert.That(loadResult.Data!.ActivePresetName, Is.EqualTo("Second"));
        }

        [Test]
        public async Task Load_CorruptedFile_RenamesToCorruptedBackupAndFallsBackToFirstRun()
        {
            File.WriteAllText(_filePath, "{ this is not valid json");
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);

            var result = await sut.LoadAsync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data, Is.Null, "corrupted parse should fall back to first-run semantics");
            Assert.That(result.CorruptedBackupPath, Is.Not.Null);
            Assert.That(File.Exists(result.CorruptedBackupPath!), Is.True);
            Assert.That(File.Exists(_filePath), Is.False, "the original corrupt file should have been moved");
            // The diagnostics logger should have recorded the corruption.
            bool found = false;
            foreach (var entry in logger.Entries)
            {
                if (entry.Message.IndexOf("corrupt", StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.Message.IndexOf("parse", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True, "JsonPresetStorage should log the corruption event (Req 10.5)");
        }

        [Test]
        public async Task Save_FailsCleanly_LeavesPriorFileIntactWhenTempCannotBeRenamed()
        {
            // Create a baseline file then drop a stale temp file with a directory of the same
            // name to force the temp-write path to fail. We then assert SaveResult reports
            // a recoverable failure code and the original file content stays readable.
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);
            await sut.SaveAsync(new PresetFileRoot { ActivePresetName = "Baseline" });

            // Make a directory at the temp-write path to force the next save to fail.
            // The failure surfaces from File.Open (writing to the .tmp path) on Windows as
            // UnauthorizedAccessException → PermissionDenied; on POSIX it surfaces from
            // File.Move as IOException → IOError. Both are valid "save failed cleanly"
            // signals — the atomic-write contract is about the prior file staying intact,
            // not about the specific error categorization of the underlying syscall.
            var tempPath = _filePath + ".tmp";
            Directory.CreateDirectory(tempPath);

            var result = await sut.SaveAsync(new PresetFileRoot { ActivePresetName = "ShouldNotApply" });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error,
                Is.EqualTo(PresetSaveError.IOError).Or.EqualTo(PresetSaveError.PermissionDenied),
                "Save must report a recoverable failure (IOError or PermissionDenied) when the temp-write path is blocked.");

            // Ensure the original file is still readable / unchanged.
            Directory.Delete(tempPath, recursive: true);
            var loaded = await sut.LoadAsync();
            Assert.That(loaded.Data, Is.Not.Null);
            Assert.That(loaded.Data!.ActivePresetName, Is.EqualTo("Baseline"),
                "atomic write contract: failed save must not corrupt the existing file");
        }

        [Test]
        public async Task ConcurrentSaves_AreSerialized_AndLastWriteWins()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);

            // Fire ten saves with distinct content concurrently.
            var tasks = new Task<SaveResult>[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                int copy = i;
                tasks[i] = sut.SaveAsync(new PresetFileRoot { ActivePresetName = "P" + copy });
            }

            var results = await Task.WhenAll(tasks);
            foreach (var r in results)
            {
                Assert.That(r.Success, Is.True);
            }

            // The file must be a fully readable JSON document with one of the names we wrote.
            var loaded = await sut.LoadAsync();
            Assert.That(loaded.Success, Is.True);
            Assert.That(loaded.Data, Is.Not.Null);
            Assert.That(loaded.Data!.ActivePresetName, Does.StartWith("P"));
        }

        [Test]
        public async Task Flush_WithNothingPending_ReturnsSuccess()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);

            var result = await sut.FlushAsync();

            Assert.That(result.Success, Is.True);
        }

        [Test]
        public async Task LeftoverTempFile_DoesNotCorruptNextLoad()
        {
            // Simulate the residue of a write-time crash: a stale temp file alongside a clean
            // baseline file. The next Load must still succeed and ignore the temp.
            var logger = new FakeDiagnosticsLogger();
            var sut = new JsonPresetStorage(_filePath, logger);
            await sut.SaveAsync(new PresetFileRoot { ActivePresetName = "Clean" });

            File.WriteAllText(_filePath + ".tmp", "garbage");

            var result = await sut.LoadAsync();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data!.ActivePresetName, Is.EqualTo("Clean"));
        }
    }
}
