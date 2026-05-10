#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.Services;

namespace VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IPresetStorage"/> for tests. Records all writes; the
    /// <see cref="Corrupt"/> flag forces <see cref="LoadAllAsync"/> to surface the
    /// stored items as a single corrupted entry, exercising the
    /// <c>StorageHealthReport.CorruptedCount</c> path.
    /// </summary>
    public sealed class InMemoryPresetStorage : IPresetStorage
    {
        private readonly Dictionary<string, PresetRecord> _records =
            new Dictionary<string, PresetRecord>(StringComparer.Ordinal);

        public string? ActiveId { get; private set; }

        public bool ThrowOnSave { get; set; }
        public int Corrupt { get; set; }

        public IReadOnlyDictionary<string, PresetRecord> AllRecords => _records;
        public int SaveCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }

        public Task<IReadOnlyList<PresetRecord>> LoadAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<PresetRecord>>(_records.Values.ToArray());
        }

        public Task<string?> LoadActivePresetIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ActiveId);
        }

        public Task SaveAsync(PresetRecord record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            if (ThrowOnSave) throw new InvalidOperationException("ThrowOnSave");
            _records[record.Header.PresetId] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string presetId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCallCount++;
            _records.Remove(presetId);
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(string? presetId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActiveId = presetId;
            return Task.CompletedTask;
        }

        public Task<StorageHealthReport> CheckHealthAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = new StorageHealthReport
            {
                LoadedCount = _records.Count,
                CorruptedCount = Corrupt,
                BackedUpFiles = Corrupt > 0 ? new[] { "fake.json.bak.0" } : Array.Empty<string>(),
            };
            return Task.FromResult(report);
        }
    }
}
