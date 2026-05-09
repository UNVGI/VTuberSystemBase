#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// Subscribes to <see cref="StageLightingTopics.LightsList"/> state messages and
    /// keeps an in-memory snapshot of the current lights, raising a diff event whenever
    /// the list changes. Stable order: existing entries keep their slots, removed
    /// entries are dropped, new entries are appended in arrival order.
    /// See design.md §Services §LightListState (Requirements 4.1, 4.4, 4.8, 7.8).
    /// </summary>
    public sealed class LightListState : IDisposable
    {
        private readonly IUiSubscriptionClient _subscriptionClient;
        private readonly IDiagnosticsLogger? _log;
        private readonly List<LightListItemDto> _current = new List<LightListItemDto>();

        private ISubscriptionToken? _token;
        private bool _disposed;

        public LightListState(IUiSubscriptionClient subscriptionClient, IDiagnosticsLogger? logger = null)
        {
            _subscriptionClient = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
            _log = logger;
        }

        /// <summary>Most recent normalized list snapshot. Read-only view, do not mutate.</summary>
        public IReadOnlyList<LightListItemDto> CurrentList => _current;

        /// <summary>Raised whenever the list snapshot changes. Carries the added/removed diff.</summary>
        public event Action<LightListChangeEvent>? Changed;

        public void StartSubscribing()
        {
            if (_disposed) return;
            if (_token is not null) return; // Idempotent.
            _token = _subscriptionClient.Subscribe<LightListDto>(
                StageLightingTopics.LightsList,
                MessageKind.State,
                OnEnvelope);
        }

        public void StopSubscribing()
        {
            _token?.Dispose();
            _token = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopSubscribing();
        }

        // --------------------------------------------------------------------

        private void OnEnvelope(MessageEnvelope<LightListDto> env)
        {
            ApplySnapshot(env.Payload);
        }

        internal void ApplySnapshot(LightListDto snapshot)
        {
            // Build incoming map, preserving first occurrence per design (duplicate lightId =
            // first wins + warning, Req 4.4 / SL-3 invariants).
            var incoming = new Dictionary<string, LightListItemDto>(StringComparer.Ordinal);
            var incomingOrder = new List<string>();
            if (snapshot.Items is not null)
            {
                for (int i = 0; i < snapshot.Items.Count; i++)
                {
                    var item = snapshot.Items[i];
                    if (string.IsNullOrEmpty(item.LightId)) continue;
                    if (incoming.ContainsKey(item.LightId))
                    {
                        _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                            $"LightListState duplicate lightId='{item.LightId}' kept first occurrence",
                            new { lightId = item.LightId });
                        continue;
                    }
                    incoming[item.LightId] = item;
                    incomingOrder.Add(item.LightId);
                }
            }

            // Removed: in current but not in incoming.
            var removed = new List<string>();
            for (int i = 0; i < _current.Count; i++)
            {
                if (!incoming.ContainsKey(_current[i].LightId))
                    removed.Add(_current[i].LightId);
            }

            // Added: in incoming but not in current.
            var existing = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _current.Count; i++) existing.Add(_current[i].LightId);
            var added = new List<LightListItemDto>();
            for (int i = 0; i < incomingOrder.Count; i++)
            {
                if (!existing.Contains(incomingOrder[i]))
                    added.Add(incoming[incomingOrder[i]]);
            }

            // No change → don't fire.
            if (added.Count == 0 && removed.Count == 0)
            {
                // Refresh current data even when ids match (display name / type may change).
                bool changedInPlace = false;
                for (int i = 0; i < _current.Count; i++)
                {
                    if (incoming.TryGetValue(_current[i].LightId, out var fresh)
                        && !fresh.Equals(_current[i]))
                    {
                        _current[i] = fresh;
                        changedInPlace = true;
                    }
                }
                if (!changedInPlace) return;
            }
            else
            {
                // Rebuild list: keep existing-still-present items in order, then appended new
                // items in incoming order.
                var preserved = new List<LightListItemDto>();
                for (int i = 0; i < _current.Count; i++)
                {
                    if (incoming.TryGetValue(_current[i].LightId, out var fresh))
                        preserved.Add(fresh);
                }
                preserved.AddRange(added);
                _current.Clear();
                _current.AddRange(preserved);
            }

            Changed?.Invoke(new LightListChangeEvent
            {
                Added = added,
                Removed = removed,
            });
        }
    }

    /// <summary>Diff payload raised by <see cref="LightListState.Changed"/>.</summary>
    public readonly struct LightListChangeEvent
    {
        public IReadOnlyList<LightListItemDto> Added { get; init; }
        public IReadOnlyList<string> Removed { get; init; }
    }
}
