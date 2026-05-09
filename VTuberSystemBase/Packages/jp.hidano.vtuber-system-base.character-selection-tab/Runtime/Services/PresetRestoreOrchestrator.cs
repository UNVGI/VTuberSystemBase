#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.Ipc;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    public sealed class RestoreProgressEvent
    {
        public int TotalSlots { get; init; }
        public int CompletedSlots { get; init; }
        public int FailedSlots { get; init; }
        public IReadOnlyList<string> UnresolvedAvatarKeys { get; init; } = Array.Empty<string>();
    }

    public interface IPresetRestoreOrchestrator : IDisposable
    {
        Task ReplayActivePresetAsync(CancellationToken cancellationToken);
        event Action<RestoreProgressEvent> OnProgress;
    }

    /// <summary>
    /// Replays the active preset's per-slot assignments and settings via the
    /// normal state path (<see cref="ICharacterTabIpcBinder.PublishAssignment"/>
    /// + <c>PublishSettingValue</c>) on connection establishment. Unresolved
    /// avatar keys (i.e. not in the current avatar catalog) cause the
    /// corresponding slot to be cleared (assignment=null) with a diagnostic.
    /// (task 3.2.)
    /// </summary>
    public sealed class PresetRestoreOrchestrator : IPresetRestoreOrchestrator
    {
        private readonly IPresetStoreLogic _presets;
        private readonly ICharacterTabIpcBinder _binder;
        private readonly ICharacterTabStateStore _store;
        private readonly IConnectionStatus _connection;
        private readonly IDiagnosticsLogger? _log;
        private bool _started;
        private bool _disposed;

        public event Action<RestoreProgressEvent>? OnProgress;

        public PresetRestoreOrchestrator(
            IPresetStoreLogic presets,
            ICharacterTabIpcBinder binder,
            ICharacterTabStateStore store,
            IConnectionStatus connection,
            IDiagnosticsLogger? logger = null)
        {
            _presets = presets ?? throw new ArgumentNullException(nameof(presets));
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _log = logger;
            _connection.OnStatusChanged += OnConnectionChanged;
        }

        public Task ReplayActivePresetAsync(CancellationToken cancellationToken)
        {
            if (_disposed) return Task.CompletedTask;
            var active = _presets.GetActivePreset();
            if (active is null)
            {
                _log?.Log(LogLevel.Info, LogCategory.TabSpec, "Restore.Start no-active-preset");
                OnProgress?.Invoke(new RestoreProgressEvent());
                return Task.CompletedTask;
            }

            _log?.Log(LogLevel.Info, LogCategory.TabSpec, $"Restore.Start preset={active.Header.PresetId}");
            var avatarKeys = new HashSet<string>(_store.AvatarCatalog.Select(e => e.AvatarKey), StringComparer.Ordinal);
            int total = active.Assignments.Count;
            int completed = 0;
            int failed = 0;
            var unresolved = new List<string>();

            foreach (var kv in active.Assignments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slotId = kv.Key;
                var avatarKey = kv.Value;
                if (avatarKey is not null && avatarKeys.Count > 0 && !avatarKeys.Contains(avatarKey))
                {
                    unresolved.Add(avatarKey);
                    _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                        $"Restore.Unresolved slot={slotId} avatar={avatarKey} -> empty");
                    var r1 = _binder.PublishAssignment(slotId, null);
                    if (!r1.Success) { failed++; continue; }
                    completed++;
                    continue;
                }
                var sendResult = _binder.PublishAssignment(slotId, avatarKey);
                if (!sendResult.Success)
                {
                    failed++;
                    _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                        $"Restore.Failed slot={slotId} error={sendResult.Error?.Code}");
                    continue;
                }
                if (active.Settings.TryGetValue(slotId, out var slotSettings))
                {
                    foreach (var sv in slotSettings)
                    {
                        var sr = _binder.PublishSettingValue(slotId, sv.Key, sv.Value);
                        if (!sr.Success)
                        {
                            // Single-setting failure does not abort the slot.
                            _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                                $"Restore.SettingSendFailed slot={slotId} key={sv.Key} error={sr.Error?.Code}");
                        }
                    }
                }
                completed++;
            }

            OnProgress?.Invoke(new RestoreProgressEvent
            {
                TotalSlots = total,
                CompletedSlots = completed,
                FailedSlots = failed,
                UnresolvedAvatarKeys = unresolved,
            });
            _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                $"Restore.Complete total={total} failed={failed} unresolved={unresolved.Count}");
            _store.SetActivePreset(active.Header.PresetId);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection.OnStatusChanged -= OnConnectionChanged;
        }

        private void OnConnectionChanged(ConnectionStatusEvent e)
        {
            if (e.To == ConnectionStatusCode.Connected && !_started)
            {
                _started = true;
                _ = ReplayActivePresetAsync(CancellationToken.None);
            }
            else if (e.To == ConnectionStatusCode.Connected && _started)
            {
                // Reconnect: re-replay to bring main output side back in sync.
                _ = ReplayActivePresetAsync(CancellationToken.None);
            }
        }
    }
}
