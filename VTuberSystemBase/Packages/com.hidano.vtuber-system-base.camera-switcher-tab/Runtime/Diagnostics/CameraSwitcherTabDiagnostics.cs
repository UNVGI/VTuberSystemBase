#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Osc;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Diagnostics
{
    /// <summary>
    /// Snapshot returned by <see cref="CameraSwitcherTabDiagnostics.GetSnapshot"/>.
    /// Mirrors Requirement 14.9: tab status, camera count, active / editing
    /// cameraId, OSC + IPC connectivity, last preset save time, active
    /// preset name, and per-Kind failure counters.
    /// </summary>
    public sealed class CameraSwitcherTabSnapshot
    {
        public TabStatus Status { get; init; }
        public int CameraCount { get; init; }
        public string? ActiveCameraId { get; init; }
        public string? EditingCameraId { get; init; }
        public string? OscState { get; init; }
        public bool IpcConnected { get; init; }
        public DateTimeOffset? LastPresetSaveAt { get; init; }
        public string? ActivePresetName { get; init; }
        public IReadOnlyDictionary<FailureKind, int> FailureCounts { get; init; }
            = new Dictionary<FailureKind, int>();
    }

    /// <summary>
    /// Composition-root-owned diagnostics surface for the camera switcher tab.
    /// Reads observable state directly from the live components (no caching).
    /// </summary>
    public sealed class CameraSwitcherTabDiagnostics
    {
        private readonly CameraSwitcherCoordinator _coordinator;
        private readonly PresetController _presets;
        private readonly OscClientLifecycle _oscLifecycle;
        private readonly IConnectionStatus _connection;

        public CameraSwitcherTabDiagnostics(
            CameraSwitcherCoordinator coordinator,
            PresetController presets,
            OscClientLifecycle oscLifecycle,
            IConnectionStatus connection)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _presets = presets ?? throw new ArgumentNullException(nameof(presets));
            _oscLifecycle = oscLifecycle ?? throw new ArgumentNullException(nameof(oscLifecycle));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public CameraSwitcherTabSnapshot GetSnapshot()
        {
            return new CameraSwitcherTabSnapshot
            {
                Status = _coordinator.Status,
                CameraCount = _coordinator.Cameras.Count,
                ActiveCameraId = _coordinator.ActiveCameraId.HasValue ? _coordinator.ActiveCameraId.Value : null,
                EditingCameraId = _coordinator.EditingCameraId.HasValue ? _coordinator.EditingCameraId.Value : null,
                OscState = _oscLifecycle.EmitterState.ToString(),
                IpcConnected = _connection.IsConnected,
                LastPresetSaveAt = _presets.LastSavedAt,
                ActivePresetName = _presets.ActivePresetName,
                FailureCounts = _coordinator.Failures.Snapshot(),
            };
        }
    }
}
