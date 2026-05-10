#nullable enable
using System;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using ShellConn = VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CharacterSelectionTab.Diagnostics
{
    /// <summary>
    /// Generates a <see cref="TabDiagnosticsSnapshot"/> on demand. (task 5.6 / 7.3.)
    /// Pure read; safe to call from any non-mutating context including tests.
    /// </summary>
    public interface ICharacterTabDiagnostics
    {
        TabDiagnosticsSnapshot Capture();
    }

    public sealed class CharacterTabDiagnostics : ICharacterTabDiagnostics
    {
        private readonly ICharacterTabStateStore _store;
        private readonly IPresetStoreLogic _presets;
        private readonly ShellConn.IConnectionStatus _conn;
        private readonly Func<DateTimeOffset> _now;

        public int CorruptedPresetBackupCount { get; set; }

        public CharacterTabDiagnostics(
            ICharacterTabStateStore store,
            IPresetStoreLogic presets,
            ShellConn.IConnectionStatus conn,
            Func<DateTimeOffset>? now = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _presets = presets ?? throw new ArgumentNullException(nameof(presets));
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
            _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        public TabDiagnosticsSnapshot Capture()
        {
            var slots = _store.ListSlots();
            int assigned = 0;
            int error = 0;
            int inflight = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].AssignedAvatarKey is not null) assigned++;
                if (slots[i].Status == SlotStatus.Error) error++;
                if (slots[i].InFlight is not null) inflight++;
            }
            return new TabDiagnosticsSnapshot(
                totalSlotCount: slots.Count,
                assignedSlotCount: assigned,
                errorSlotCount: error,
                inFlightOperationCount: inflight,
                lastSavedAt: _presets.LastSavedAt,
                connectionStatus: MapConnection(_conn.CurrentStatus),
                activePresetId: _presets.ActivePresetId,
                corruptedPresetBackupCount: CorruptedPresetBackupCount,
                capturedAt: _now());
        }

        private static ConnectionStatusCode MapConnection(ShellConn.ConnectionStatusCode shell) => shell switch
        {
            ShellConn.ConnectionStatusCode.Initializing => ConnectionStatusCode.Initializing,
            ShellConn.ConnectionStatusCode.Connecting => ConnectionStatusCode.Connecting,
            ShellConn.ConnectionStatusCode.Connected => ConnectionStatusCode.Connected,
            ShellConn.ConnectionStatusCode.Disconnected => ConnectionStatusCode.Disconnected,
            ShellConn.ConnectionStatusCode.Reconnecting => ConnectionStatusCode.Reconnecting,
            ShellConn.ConnectionStatusCode.FailedPermanently => ConnectionStatusCode.FailedPermanently,
            _ => ConnectionStatusCode.Initializing,
        };
    }
}
