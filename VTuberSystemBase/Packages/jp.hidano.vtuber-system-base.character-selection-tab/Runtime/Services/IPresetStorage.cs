#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Test-substitutable abstraction for preset persistence. Production:
    /// <c>JsonPresetStorage</c>. Tests: <c>InMemoryPresetStorage</c>.
    /// (task 2.5, design.md §Services §IPresetStorage).
    /// </summary>
    public interface IPresetStorage
    {
        Task<IReadOnlyList<PresetRecord>> LoadAllAsync(CancellationToken cancellationToken);
        Task<string?> LoadActivePresetIdAsync(CancellationToken cancellationToken);
        Task SaveAsync(PresetRecord record, CancellationToken cancellationToken);
        Task DeleteAsync(string presetId, CancellationToken cancellationToken);
        Task SetActiveAsync(string? presetId, CancellationToken cancellationToken);
        Task<StorageHealthReport> CheckHealthAsync(CancellationToken cancellationToken);
    }

    public sealed class StorageHealthReport
    {
        public int LoadedCount { get; init; }
        public int CorruptedCount { get; init; }
        public IReadOnlyList<string> BackedUpFiles { get; init; } = System.Array.Empty<string>();
    }
}
