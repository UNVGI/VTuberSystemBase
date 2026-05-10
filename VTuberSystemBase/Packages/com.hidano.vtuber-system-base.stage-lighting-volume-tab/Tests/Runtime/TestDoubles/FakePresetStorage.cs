#nullable enable
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Services;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IPresetStorage"/> double. Tracks the most recently saved
    /// <see cref="PresetFileRoot"/>, the load result the next call should return, and
    /// counts saves/flushes so tests can verify behaviour without touching the disk.
    /// (Task 1.2, Requirements 8.1, 8.7, 12.1, 12.3)
    /// </summary>
    public sealed class FakePresetStorage : IPresetStorage
    {
        public PresetFileRoot? Stored { get; private set; }
        public bool SimulateCorruptOnLoad { get; set; }
        public bool SimulateMissingOnLoad { get; set; } = true;
        public bool ForceSaveFailure { get; set; }
        public PresetSaveError? SaveFailureError { get; set; }

        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public int FlushCount { get; private set; }

        public Task<PresetLoadResult> LoadAsync(CancellationToken ct = default)
        {
            LoadCount++;
            if (SimulateCorruptOnLoad)
            {
                return Task.FromResult(new PresetLoadResult
                {
                    Success = true,
                    Data = null,
                    CorruptedBackupPath = "fake.corrupted-12345",
                });
            }

            if (SimulateMissingOnLoad || Stored is null)
            {
                return Task.FromResult(new PresetLoadResult
                {
                    Success = true,
                    Data = null,
                });
            }

            return Task.FromResult(new PresetLoadResult
            {
                Success = true,
                Data = Stored,
            });
        }

        public Task<SaveResult> SaveAsync(PresetFileRoot root, CancellationToken ct = default)
        {
            SaveCount++;
            if (ForceSaveFailure)
            {
                return Task.FromResult(SaveResult.Fail(SaveFailureError ?? PresetSaveError.IOError));
            }
            Stored = root;
            return Task.FromResult(SaveResult.Ok());
        }

        public Task<SaveResult> FlushAsync(CancellationToken ct = default)
        {
            FlushCount++;
            if (ForceSaveFailure)
            {
                return Task.FromResult(SaveResult.Fail(SaveFailureError ?? PresetSaveError.IOError));
            }
            return Task.FromResult(SaveResult.Ok());
        }
    }
}
