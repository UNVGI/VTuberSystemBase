#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Diagnostics
{
    /// <summary>
    /// Aggregates diagnostic logs and exposes a synchronous <see cref="GetSnapshot"/> API
    /// for the stage-lighting-volume tab. Every log call routes through the shared
    /// <see cref="IDiagnosticsLogger"/> from <c>ui-toolkit-shell</c>; that logger is
    /// constructed so it can never reach the main-output surface (Display 2+), satisfying
    /// Requirement 10.6 by construction (see ui-toolkit-shell DiagnosticsLogger contract).
    /// See design.md §Diagnostics §StageTabDiagnostics (Requirements 10.1-10.8).
    /// </summary>
    public sealed class StageTabDiagnostics
    {
        private readonly IDiagnosticsLogger _logger;
        private readonly object _gate = new object();

        // Snapshot fields. Mutated through Set/Record helpers; read by GetSnapshot.
        private string? _activePresetName;
        private string? _currentStageKey;
        private int _lightCount;
        private int _lightsInErrorState;
        private int _volumeOverridesEnabled;
        private int _pendingAsyncLoads;
        private DateTimeOffset? _lastPersistenceSaveAt;
        private bool _ipcConnected;

        public StageTabDiagnostics(IDiagnosticsLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ---------- Log methods ----------

        public void LogInitializationPhase(string phase, bool success, string? error = null)
        {
            var level = success ? LogLevel.Info : LogLevel.Error;
            var msg = success
                ? $"StageTab.Init phase='{phase}' status=ok"
                : $"StageTab.Init phase='{phase}' status=failed error='{error}'";
            _logger.Log(level, LogCategory.TabSpec, msg, new { phase, success, error });
        }

        public void LogCommandSent(string topic, string kind)
        {
            _logger.Log(LogLevel.Debug, LogCategory.TabSpec,
                $"StageTab.Send topic='{topic}' kind={kind}",
                new { topic, kind });
        }

        public void LogEventReceived(string topic, string? correlationId)
        {
            _logger.Log(LogLevel.Debug, LogCategory.TabSpec,
                $"StageTab.Recv topic='{topic}' correlationId='{correlationId ?? "<none>"}'",
                new { topic, correlationId });
        }

        public void LogAssetLoadFailure(string addressableKey, string reason)
        {
            _logger.Log(LogLevel.Warning, LogCategory.TabSpec,
                $"StageTab.AssetLoadFailed key='{addressableKey}' reason='{reason}'",
                new { addressableKey, reason });
        }

        public void LogPersistenceFailure(string operation, string reason)
        {
            _logger.Log(LogLevel.Error, LogCategory.TabSpec,
                $"StageTab.Persistence operation='{operation}' status=failed reason='{reason}'",
                new { operation, reason });
        }

        // ---------- Snapshot setters (mutators called from ViewModel layer) ----------

        public void SetActivePresetName(string? name)
        {
            lock (_gate) _activePresetName = name;
        }

        public void SetCurrentStageKey(string? key)
        {
            lock (_gate) _currentStageKey = key;
        }

        public void SetLightCount(int count)
        {
            lock (_gate) _lightCount = count;
        }

        public void SetLightsInErrorState(int count)
        {
            lock (_gate) _lightsInErrorState = count;
        }

        public void SetVolumeOverridesEnabled(int count)
        {
            lock (_gate) _volumeOverridesEnabled = count;
        }

        public void SetPendingAsyncLoads(int count)
        {
            lock (_gate) _pendingAsyncLoads = count;
        }

        public void RecordPersistenceSave(DateTimeOffset at)
        {
            lock (_gate) _lastPersistenceSaveAt = at;
        }

        public void SetIpcConnected(bool connected)
        {
            lock (_gate) _ipcConnected = connected;
        }

        public StageTabDiagnosticsSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return new StageTabDiagnosticsSnapshot
                {
                    ActivePresetName = _activePresetName,
                    CurrentStageKey = _currentStageKey,
                    LightCount = _lightCount,
                    LightsInErrorState = _lightsInErrorState,
                    VolumeOverridesEnabled = _volumeOverridesEnabled,
                    PendingAsyncLoads = _pendingAsyncLoads,
                    LastPersistenceSaveAt = _lastPersistenceSaveAt,
                    IpcConnected = _ipcConnected,
                };
            }
        }
    }

    /// <summary>External diagnostic snapshot exposed by <see cref="StageTabDiagnostics"/> (Req 10.8).</summary>
    public readonly struct StageTabDiagnosticsSnapshot
    {
        public string? ActivePresetName { get; init; }
        public string? CurrentStageKey { get; init; }
        public int LightCount { get; init; }
        public int LightsInErrorState { get; init; }
        public int VolumeOverridesEnabled { get; init; }
        public int PendingAsyncLoads { get; init; }
        public DateTimeOffset? LastPersistenceSaveAt { get; init; }
        public bool IpcConnected { get; init; }
    }
}
