#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IPresetStore"/>. Records every save invocation so
    /// tests can pin debounce semantics, and supports forced corruption /
    /// write-failure scenarios.
    /// </summary>
    public sealed class FakePresetStore : IPresetStore
    {
        private readonly List<PresetPayload> _presets = new List<PresetPayload>();
        private string? _activeName;

        public bool ForceCorruptOnLoad { get; set; }
        public PresetIoFailureKind? ForceLoadFailure { get; set; }
        public PresetIoFailureKind? ForceSaveFailure { get; set; }

        public int LoadCallCount { get; private set; }
        public int SaveCallCount { get; private set; }
        public string? BackupPath { get; set; }

        public IReadOnlyList<PresetPayload> Presets => _presets;
        public string? ActiveName => _activeName;

        public Task<PresetLoadOutcome> LoadAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCallCount++;
            if (ForceLoadFailure is { } kind)
            {
                return Task.FromResult(new PresetLoadOutcome
                {
                    Result = PresetIoResult.Fail(kind),
                    Presets = Array.Empty<PresetPayload>(),
                    ActivePresetName = null,
                    BackupPath = ForceCorruptOnLoad ? BackupPath : null,
                });
            }
            return Task.FromResult(new PresetLoadOutcome
            {
                Result = PresetIoResult.Ok(),
                Presets = _presets.ToArray(),
                ActivePresetName = _activeName,
                BackupPath = ForceCorruptOnLoad ? BackupPath : null,
            });
        }

        public Task<PresetIoResult> SaveAllAsync(
            IReadOnlyList<PresetPayload> presets,
            string? activePresetName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            if (ForceSaveFailure is { } kind)
            {
                return Task.FromResult(PresetIoResult.Fail(kind));
            }
            _presets.Clear();
            _presets.AddRange(presets);
            _activeName = activePresetName;
            return Task.FromResult(PresetIoResult.Ok());
        }

        /// <summary>Test helper: seed the store with a presets snapshot.</summary>
        public void Seed(IEnumerable<PresetPayload> presets, string? activeName = null)
        {
            _presets.Clear();
            _presets.AddRange(presets);
            _activeName = activeName;
        }
    }
}
