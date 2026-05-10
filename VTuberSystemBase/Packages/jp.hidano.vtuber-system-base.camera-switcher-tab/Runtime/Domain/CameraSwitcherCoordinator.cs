#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// State-machine facade for the camera switcher tab. Composes the lower-level
    /// services (registry / tracker / OSC stream / volume / preset / preview)
    /// and surfaces a single observable façade to the View layer.
    /// </summary>
    /// <remarks>
    /// Every UI-side <c>Request*</c> / <c>Set*</c> method MUST NOT throw.
    /// Failures funnel into <see cref="FailureAggregator"/> and the diagnostics
    /// logger. Subscriptions to inbound IPC topics are wired up by the
    /// Composition Root via <see cref="HandleCamerasList"/> /
    /// <see cref="HandleCamerasActive"/> etc — this class does NOT subscribe on
    /// its own so it stays unit-testable without Unity.
    /// </remarks>
    public sealed class CameraSwitcherCoordinator : ICameraSwitcherCoordinator
    {
        private readonly IUiCommandClient _commands;
        private readonly IUiSubscriptionClient _subs;
        private readonly IConnectionStatus _connection;
        private readonly ITimeProvider _time;
        private readonly IDiagnosticsLogger? _log;

        private readonly CameraRegistry _registry;
        private readonly ActiveCameraTracker _tracker;
        private readonly TimeoutTracker _timeouts;
        private readonly FailureAggregator _failures;
        private readonly OscStreamController _oscStream;
        private readonly VolumeUiStateManager _volumeUi;
        private readonly PresetController _presets;
        private readonly PreviewSubscriptionController _preview;

        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private readonly object _stateLock = new object();
        private TabStatus _status = TabStatus.Initializing;
        private bool _activated;
        private bool _disposed;

        public event Action? OnStateChanged;

        public CameraSwitcherCoordinator(
            IUiCommandClient commands,
            IUiSubscriptionClient subs,
            IConnectionStatus connection,
            ITimeProvider time,
            CameraRegistry registry,
            ActiveCameraTracker tracker,
            TimeoutTracker timeouts,
            FailureAggregator failures,
            OscStreamController oscStream,
            VolumeUiStateManager volumeUi,
            PresetController presets,
            PreviewSubscriptionController preview,
            IDiagnosticsLogger? logger = null)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _subs = subs ?? throw new ArgumentNullException(nameof(subs));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _timeouts = timeouts ?? throw new ArgumentNullException(nameof(timeouts));
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            _oscStream = oscStream ?? throw new ArgumentNullException(nameof(oscStream));
            _volumeUi = volumeUi ?? throw new ArgumentNullException(nameof(volumeUi));
            _presets = presets ?? throw new ArgumentNullException(nameof(presets));
            _preview = preview ?? throw new ArgumentNullException(nameof(preview));
            _log = logger;

            _connection.OnStatusChanged += OnConnectionStatusChanged;
            _timeouts.OnTimeout += OnTimeoutFired;
            _tracker.OnEditingChanged += OnEditingTargetChanged;
            _tracker.OnActiveChanged += _ => OnStateChanged?.Invoke();
            _failures.OnFailureRecorded += _ => OnStateChanged?.Invoke();
        }

        public TabStatus Status
        {
            get { lock (_stateLock) return _status; }
        }

        public CameraId EditingCameraId => _tracker.EditingCameraId;
        public CameraId ActiveCameraId => _tracker.ActiveCameraId;
        public IReadOnlyList<CameraMetadata> Cameras => _registry.Enumerate();
        public FailureAggregator Failures => _failures;
        public bool IsConnected => _connection.IsConnected;

        // ---- Subscription wiring (called by Composition Root) ----

        /// <summary>
        /// Attach inbound subscriptions for every <c>cameras/*</c>, <c>camera/*</c>,
        /// and <c>camera/preset/*</c> topic the tab cares about. Returns
        /// disposables collected by the caller (typically passed to
        /// <c>ITabLifecycleHandle.Track</c>).
        /// </summary>
        public IReadOnlyList<IDisposable> SubscribeAll()
        {
            // cameras/list
            _subscriptions.Add(_subs.Subscribe<CamerasListPayload>(
                CameraIpcTopics.CamerasList, MessageKind.State,
                env => HandleCamerasList(env.Payload)));
            // cameras/active
            _subscriptions.Add(_subs.Subscribe<CamerasActiveStatePayload>(
                CameraIpcTopics.CamerasActive, MessageKind.State,
                env => HandleCamerasActive(env.Payload)));
            // camera/created
            _subscriptions.Add(_subs.Subscribe<CameraCreatedEventPayload>(
                CameraIpcTopics.CameraCreated, MessageKind.Event,
                env => HandleCameraCreated(env.Payload)));
            // camera/error
            _subscriptions.Add(_subs.Subscribe<CameraErrorEventPayload>(
                CameraIpcTopics.CameraError, MessageKind.Event,
                env => HandleCameraError(env.Payload)));
            return _subscriptions;
        }

        // ---- Inbound handlers (also exposed for tests) ----

        public void HandleCamerasList(CamerasListPayload payload)
        {
            if (payload.Cameras is null) return;
            _registry.Clear();
            foreach (var entry in payload.Cameras)
            {
                _registry.Upsert(CameraMetadata.FromListEntry(entry));
            }
            // Re-anchor editing target if it was deleted.
            if (_tracker.EditingCameraId.HasValue && !_registry.Contains(_tracker.EditingCameraId))
            {
                var deleted = _tracker.EditingCameraId;
                _tracker.SetEditing(default);
                _oscStream.OnCameraDeleted(deleted);
                _preview.DetachOne(deleted);
            }
            TransitionWhenInitialized();
            OnStateChanged?.Invoke();
        }

        public void HandleCamerasActive(CamerasActiveStatePayload payload)
        {
            if (CameraId.TryCreate(payload.ActiveCameraId, out var id))
            {
                _tracker.SetActive(id);
            }
            else
            {
                _tracker.SetActive(default);
            }
            OnStateChanged?.Invoke();
        }

        public void HandleCameraCreated(CameraCreatedEventPayload payload)
        {
            if (!string.IsNullOrEmpty(payload.ClientRequestId))
            {
                _timeouts.Cancel(payload.ClientRequestId);
            }
            if (CameraId.TryCreate(payload.CameraId, out var newId))
            {
                _registry.Upsert(CameraMetadata.FromListEntry(payload.Metadata));
                _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                    $"Camera.Created clientRequestId={payload.ClientRequestId} cameraId={payload.CameraId}");
            }
            OnStateChanged?.Invoke();
        }

        public void HandleCameraError(CameraErrorEventPayload payload)
        {
            _failures.Record(FailureKind.CameraError,
                $"{payload.Op} {payload.Reason}: {payload.Detail}", _time.UtcNow,
                context: payload);
            if (!string.IsNullOrEmpty(payload.ClientRequestId))
            {
                _timeouts.Cancel(payload.ClientRequestId);
            }
            _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                $"Camera.Error op={payload.Op} reason={payload.Reason} detail={payload.Detail}");
        }

        // ---- Lifecycle ----

        public void OnTabActivated()
        {
            if (_activated) return;
            _activated = true;
            _log?.Log(LogLevel.Debug, LogCategory.TabSpec, "Tab.Activated");
            OnStateChanged?.Invoke();
        }

        public void OnTabDeactivated()
        {
            if (!_activated) return;
            _activated = false;
            _log?.Log(LogLevel.Debug, LogCategory.TabSpec, "Tab.Deactivated");
            // Stop pushing OSC; preview detach is handled by Composition Root if needed.
            _oscStream.SetTarget(null);
            OnStateChanged?.Invoke();
        }

        public void FrameTick(in CameraSnapshot? editingCameraSnapshot)
        {
            if (_disposed || !_activated) return;
            _oscStream.FrameTick(editingCameraSnapshot);
        }

        // ---- UI-driven CRUD ----

        public void RequestAddCamera(CameraType type, string? displayName)
        {
            var clientRequestId = NewClientRequestId();
            _timeouts.Arm(clientRequestId);
            SafeSendEvent(CameraIpcTopics.CameraCommand, new CameraCommandPayload
            {
                Op = CameraCommandOps.Add,
                ClientRequestId = clientRequestId,
                Type = CameraTypeNames.ToWire(type),
                DisplayName = displayName,
            }, "add");
        }

        public void RequestDeleteCamera(CameraId cameraId)
        {
            if (!cameraId.HasValue) return;
            var clientRequestId = NewClientRequestId();
            _timeouts.Arm(clientRequestId);
            SafeSendEvent(CameraIpcTopics.CameraCommand, new CameraCommandPayload
            {
                Op = CameraCommandOps.Delete,
                ClientRequestId = clientRequestId,
                CameraId = cameraId.Value,
            }, $"delete {cameraId.Value}");
            // Local optimistic cleanup.
            _oscStream.OnCameraDeleted(cameraId);
            _preview.DetachOne(cameraId);
            if (_tracker.EditingCameraId.HasValue
                && string.Equals(_tracker.EditingCameraId.Value, cameraId.Value, StringComparison.Ordinal))
            {
                _tracker.SetEditing(default);
            }
        }

        public void ActivateCamera(CameraId cameraId)
        {
            if (!cameraId.HasValue) return;
            var clientRequestId = NewClientRequestId();
            SafeSendEvent(CameraIpcTopics.CameraCommand, new CameraCommandPayload
            {
                Op = CameraCommandOps.ActiveSet,
                ClientRequestId = clientRequestId,
                CameraId = cameraId.Value,
            }, $"active-set {cameraId.Value}");
        }

        public void SelectEditTarget(CameraId cameraId)
        {
            _tracker.SetEditing(cameraId);
        }

        public void UpdateCameraMetadata(CameraId cameraId, string key, string value)
        {
            if (!cameraId.HasValue || string.IsNullOrEmpty(key)) return;
            var topic = CameraIpcTopics.CameraMetadata(cameraId, key);
            SafeSendState(topic, new CameraMetadataStatePayload
            {
                Value = JsonSerializer.SerializeToElement(value),
            }, $"metadata {cameraId.Value}/{key}");
        }

        // ---- Volume ----

        public void AddVolumeOverride(CameraId cameraId, string overrideType)
        {
            if (!cameraId.HasValue || string.IsNullOrEmpty(overrideType)) return;
            SafeSendEvent(CameraIpcTopics.VolumeCommand(cameraId), new VolumeCommandPayload
            {
                Op = VolumeCommandOps.OverrideAdd,
                OverrideType = overrideType,
            }, $"volume add {cameraId.Value}/{overrideType}");
        }

        public void RemoveVolumeOverride(CameraId cameraId, string overrideType)
        {
            if (!cameraId.HasValue || string.IsNullOrEmpty(overrideType)) return;
            SafeSendEvent(CameraIpcTopics.VolumeCommand(cameraId), new VolumeCommandPayload
            {
                Op = VolumeCommandOps.OverrideRemove,
                OverrideType = overrideType,
            }, $"volume remove {cameraId.Value}/{overrideType}");
        }

        public void SetVolumeOverrideEnabled(CameraId cameraId, string overrideType, bool enabled)
        {
            if (!cameraId.HasValue || string.IsNullOrEmpty(overrideType)) return;
            SafeSendState(CameraIpcTopics.VolumeOverrideEnabled(cameraId, overrideType),
                new VolumeOverrideEnabledStatePayload { Enabled = enabled },
                $"override enabled {cameraId.Value}/{overrideType}");
        }

        public void SetVolumeOverrideParam(CameraId cameraId, string overrideType, string param, JsonElement value)
        {
            if (!cameraId.HasValue || string.IsNullOrEmpty(overrideType) || string.IsNullOrEmpty(param)) return;
            SafeSendState(CameraIpcTopics.VolumeOverrideParam(cameraId, overrideType, param),
                new VolumeOverrideParamStatePayload { Value = value },
                $"param {cameraId.Value}/{overrideType}/{param}");
        }

        public void SetVolumeEnabled(CameraId cameraId, bool enabled)
        {
            if (!cameraId.HasValue) return;
            SafeSendState(CameraIpcTopics.VolumeEnabled(cameraId),
                new VolumeEnabledStatePayload { Enabled = enabled },
                $"volume enabled {cameraId.Value}");
        }

        // ---- Preset ----

        public void CreatePreset(string name) => _presets.CreatePreset(name);
        public void RenamePreset(string oldName, string newName) => _presets.RenamePreset(oldName, newName);
        public void DuplicatePreset(string sourceName, string newName) => _presets.DuplicatePreset(sourceName, newName);
        public void DeletePreset(string name) => _presets.DeletePreset(name);

        public void ActivatePreset(string name)
        {
            // Build "current" snapshot from registry + volume cache so the preset
            // controller can compute the diff. The volume cache is best-effort —
            // missing entries simply won't be diff'd.
            var current = BuildCurrentSnapshot();
            _ = _presets.ActivatePresetAsync(name, current);
        }

        public PresetController PresetsForTesting => _presets;
        public VolumeUiStateManager VolumeUiForTesting => _volumeUi;
        public PreviewSubscriptionController PreviewForTesting => _preview;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_stateLock) _status = TabStatus.Disposing;
            _connection.OnStatusChanged -= OnConnectionStatusChanged;
            _timeouts.OnTimeout -= OnTimeoutFired;
            foreach (var t in _subscriptions)
            {
                try { t.Dispose(); } catch { }
            }
            _subscriptions.Clear();
            _timeouts.DisposeAll();
            _oscStream.Dispose();
            _preview.Dispose();
            _presets.Dispose();
        }

        // ---- Private ----

        private PresetPayload BuildCurrentSnapshot()
        {
            var cameras = new List<PresetCameraEntry>();
            foreach (var c in _registry.Enumerate())
            {
                cameras.Add(new PresetCameraEntry
                {
                    LogicalId = c.Id.Value,
                    DisplayName = c.DisplayName,
                    Type = c.Type,
                    DefaultTransform = c.DefaultTransform,
                });
            }
            return new PresetPayload
            {
                Name = "<current>",
                Cameras = cameras,
                VolumeConfigs = new Dictionary<string, VolumeConfig>(),
                ActiveCameraLogicalId = _tracker.ActiveCameraId.HasValue ? _tracker.ActiveCameraId.Value : null,
            };
        }

        private void TransitionWhenInitialized()
        {
            lock (_stateLock)
            {
                if (_status is TabStatus.Initializing or TabStatus.ConnectionPending)
                {
                    _status = _connection.IsConnected ? TabStatus.Ready : TabStatus.ConnectionPending;
                }
            }
        }

        private void OnConnectionStatusChanged(ConnectionStatusEvent ev)
        {
            lock (_stateLock)
            {
                if (_status == TabStatus.Disposing) return;
                if (ev.To == ConnectionStatusCode.Connected)
                {
                    _status = TabStatus.Ready;
                }
                else if (ev.To is ConnectionStatusCode.Disconnected
                         or ConnectionStatusCode.FailedPermanently)
                {
                    _status = TabStatus.Suspended;
                }
            }
            _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                $"Connection.Changed {ev.From} -> {ev.To}");
            OnStateChanged?.Invoke();
        }

        private void OnEditingTargetChanged(CameraId next)
        {
            _oscStream.SetTarget(next.HasValue ? (CameraId?)next : null);
            // Fire-and-forget: schema fetch + UI rebind happens asynchronously.
            if (next.HasValue) _ = _volumeUi.OnEditTargetChangedAsync(next);
            OnStateChanged?.Invoke();
        }

        private void OnTimeoutFired(string clientRequestId)
        {
            _failures.Record(FailureKind.IpcSendFailure,
                $"timeout: clientRequestId={clientRequestId}", _time.UtcNow);
            _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                $"Command.Timeout clientRequestId={clientRequestId}");
            OnStateChanged?.Invoke();
        }

        private static string NewClientRequestId() => Guid.NewGuid().ToString("N");

        private void SafeSendEvent<TPayload>(string topic, TPayload payload, string label)
        {
            try
            {
                var result = _commands.PublishEvent(topic, payload);
                if (!result.Success && result.Error is { } err)
                {
                    _failures.Record(FailureKind.IpcSendFailure,
                        $"{label} send failed: {err.Code} {err.Detail}", _time.UtcNow);
                }
            }
            catch (Exception ex)
            {
                _failures.Record(FailureKind.IpcSendFailure, $"{label} threw: {ex.Message}", _time.UtcNow);
            }
        }

        private void SafeSendState<TPayload>(string topic, TPayload payload, string label)
        {
            try
            {
                var result = _commands.PublishState(topic, payload);
                if (!result.Success && result.Error is { } err)
                {
                    _failures.Record(FailureKind.IpcSendFailure,
                        $"{label} send failed: {err.Code} {err.Detail}", _time.UtcNow);
                }
            }
            catch (Exception ex)
            {
                _failures.Record(FailureKind.IpcSendFailure, $"{label} threw: {ex.Message}", _time.UtcNow);
            }
        }
    }
}
