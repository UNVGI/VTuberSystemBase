#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// Subscribes to <see cref="StageLightingTopics.StageCatalog"/> state messages and
    /// keeps the latest catalog in memory. Raises <see cref="Changed"/> after each
    /// update so the ViewModel / View layer can refresh the stage selection UI.
    /// See design.md §Services §StageCatalogState (Requirements 3.1, 3.9, 3.10, 8.8).
    /// </summary>
    public sealed class StageCatalogState : IDisposable
    {
        private readonly IUiSubscriptionClient _subscriptionClient;
        private readonly IDiagnosticsLogger? _log;
        private readonly List<StageCatalogEntryDto> _entries = new List<StageCatalogEntryDto>();

        private ISubscriptionToken? _token;
        private bool _disposed;

        public StageCatalogState(IUiSubscriptionClient subscriptionClient, IDiagnosticsLogger? logger = null)
        {
            _subscriptionClient = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
            _log = logger;
        }

        public IReadOnlyList<StageCatalogEntryDto> Entries => _entries;

        /// <summary>True once at least one catalog state message has been observed.</summary>
        public bool IsLoaded { get; private set; }

        public event Action? Changed;

        public void StartSubscribing()
        {
            if (_disposed) return;
            if (_token is not null) return;
            _token = _subscriptionClient.Subscribe<StageCatalogDto>(
                StageLightingTopics.StageCatalog,
                MessageKind.State,
                OnEnvelope);
        }

        public void StopSubscribing()
        {
            _token?.Dispose();
            _token = null;
        }

        public bool TryFind(string? addressableKey, out StageCatalogEntryDto entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(addressableKey)) return false;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].AddressableKey, addressableKey, StringComparison.Ordinal))
                {
                    entry = _entries[i];
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopSubscribing();
        }

        // --------------------------------------------------------------------

        private void OnEnvelope(MessageEnvelope<StageCatalogDto> env)
        {
            _entries.Clear();
            if (env.Payload.Items is not null)
            {
                _entries.AddRange(env.Payload.Items);
            }
            IsLoaded = true;
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"StageCatalogState.Changed handler threw: {ex.Message}");
            }
        }
    }
}
