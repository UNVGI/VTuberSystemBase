#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Port for preset persistence. Default adapter is
    /// <c>FileSystemPresetStore</c> (JSON in <c>Application.persistentDataPath</c>);
    /// tests substitute an in-memory store.
    /// </summary>
    /// <remarks>
    /// Implementations MUST NOT throw out of <see cref="LoadAllAsync"/> /
    /// <see cref="SaveAllAsync"/>. Failures (file missing, corrupted JSON,
    /// IOException) are surfaced as <see cref="PresetIoResult"/> failures and
    /// — for corrupted JSON — accompanied by a side-effect rename of the file
    /// to <c>{path}.bak.{unixMs}</c>.
    /// </remarks>
    public interface IPresetStore
    {
        /// <summary>
        /// Load every persisted preset together with the active preset name (or
        /// null if none is active). A missing file returns
        /// <see cref="PresetIoFailureKind.FileNotFound"/> with no exception.
        /// </summary>
        Task<PresetLoadOutcome> LoadAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically replace the persisted preset set. Implementations write to
        /// a temp file and rename to enforce torn-write safety.
        /// </summary>
        Task<PresetIoResult> SaveAllAsync(
            IReadOnlyList<PresetPayload> presets,
            string? activePresetName,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Outcome of <see cref="IPresetStore.LoadAllAsync"/>.</summary>
    public sealed class PresetLoadOutcome
    {
        public PresetIoResult Result { get; init; }
        public IReadOnlyList<PresetPayload> Presets { get; init; }
            = System.Array.Empty<PresetPayload>();
        public string? ActivePresetName { get; init; }
        public string? BackupPath { get; init; }
    }
}
