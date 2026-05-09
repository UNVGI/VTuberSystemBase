#nullable enable
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// Test-substitutable abstraction for the stage-lighting-volume-tab preset file. The
    /// production implementation is <c>JsonPresetStorage</c> (writes
    /// <c>Application.persistentDataPath/vtuber-system-base/stage-lighting-volume-tab.json</c>
    /// atomically); tests use the in-memory <c>FakePresetStorage</c> double.
    /// See design.md §Services §JsonPresetStorage (Requirements 8.1, 8.3, 8.4, 8.5, 8.7,
    /// 8.9, 8.10, 10.5).
    /// </summary>
    public interface IPresetStorage
    {
        /// <summary>
        /// Loads the preset file. Returns <see cref="PresetLoadResult.Success"/> = true
        /// with <see cref="PresetLoadResult.Data"/> = null on first run (file missing).
        /// On parse failure, the corrupted file is moved to <c>.corrupted-{unixMs}</c>
        /// and the result reports first-run semantics with
        /// <see cref="PresetLoadResult.CorruptedBackupPath"/> populated.
        /// </summary>
        Task<PresetLoadResult> LoadAsync(CancellationToken ct = default);

        /// <summary>
        /// Saves <paramref name="root"/> atomically (temp file + rename). On IO failure
        /// the existing file is left untouched.
        /// </summary>
        Task<SaveResult> SaveAsync(PresetFileRoot root, CancellationToken ct = default);

        /// <summary>
        /// Drains pending in-flight saves. Called on Bootstrapper.Dispose with a short
        /// timeout per Requirement 8.4 / 11.3. Idempotent and safe to call when no save
        /// is in progress (returns Success immediately).
        /// </summary>
        Task<SaveResult> FlushAsync(CancellationToken ct = default);
    }

    public readonly struct PresetLoadResult
    {
        public bool Success { get; init; }
        public PresetFileRoot? Data { get; init; }
        public PresetLoadError? Error { get; init; }
        public string? CorruptedBackupPath { get; init; }
    }

    public enum PresetLoadError
    {
        IOError,
        ParseError,
    }

    public readonly struct SaveResult
    {
        public bool Success { get; init; }
        public PresetSaveError? Error { get; init; }

        public static SaveResult Ok() => new SaveResult { Success = true };
        public static SaveResult Fail(PresetSaveError err) => new SaveResult { Success = false, Error = err };
    }

    public enum PresetSaveError
    {
        IOError,
        DiskFull,
        PermissionDenied,
    }
}
