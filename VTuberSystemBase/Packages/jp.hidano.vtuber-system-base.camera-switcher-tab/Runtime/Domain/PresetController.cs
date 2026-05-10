#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Owns the preset model in memory, debounces persistence, and orchestrates
    /// the **delete → add → metadata → volume → active-set** dispatch order
    /// when the user activates a different preset (Requirement 11.x).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Single-threaded: every public method runs on the Unity main thread (D-3).
    /// The save debounce timer is created via <see cref="ITimeProvider"/> so
    /// tests can advance time deterministically.
    /// </para>
    /// <para>
    /// Switching is serialised through a <see cref="SemaphoreSlim"/>: a second
    /// activation request waits for the in-flight one to finish so the UI never
    /// observes a half-applied switch.
    /// </para>
    /// </remarks>
    public sealed class PresetController : IDisposable
    {
        public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);

        private readonly IPresetStore _store;
        private readonly IUiCommandClient _commands;
        private readonly ITimeProvider _time;
        private readonly FailureAggregator _failures;
        private readonly TimeSpan _debounce;

        private readonly object _modelLock = new object();
        private readonly Dictionary<string, PresetPayload> _presets = new Dictionary<string, PresetPayload>(StringComparer.Ordinal);
        private readonly List<string> _presetOrder = new List<string>();
        private string? _activeName;

        private readonly IDebounceTimer _saveTimer;
        private readonly SemaphoreSlim _activateGate = new SemaphoreSlim(1, 1);
        private bool _disposed;
        private bool _saveScheduled;
        private DateTimeOffset? _lastSavedAt;

        public event Action<PresetIoResult>? OnIoResult;
        public event Action? OnPresetListChanged;
        public event Action<string?>? OnActivePresetChanged;

        public PresetController(
            IPresetStore store,
            IUiCommandClient commands,
            ITimeProvider time,
            FailureAggregator failures,
            TimeSpan? debounce = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            _debounce = debounce ?? DefaultDebounce;
            _saveTimer = _time.CreateDebounce(_debounce, FlushFireAndForget);
        }

        public string? ActivePresetName
        {
            get { lock (_modelLock) return _activeName; }
        }

        public IReadOnlyList<string> PresetNames
        {
            get { lock (_modelLock) return _presetOrder.ToArray(); }
        }

        public DateTimeOffset? LastSavedAt
        {
            get { lock (_modelLock) return _lastSavedAt; }
        }

        public bool TryGet(string name, out PresetPayload payload)
        {
            lock (_modelLock)
            {
                if (name != null && _presets.TryGetValue(name, out var p))
                {
                    payload = p;
                    return true;
                }
                payload = null!;
                return false;
            }
        }

        // ---- CRUD ----

        public PresetIoResult CreatePreset(string name, PresetPayload? seed = null)
        {
            if (string.IsNullOrEmpty(name))
                return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, "name is empty");
            lock (_modelLock)
            {
                if (_presets.ContainsKey(name))
                    return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, $"duplicate name: {name}");
                var payload = seed ?? new PresetPayload
                {
                    Name = name,
                    Cameras = Array.Empty<PresetCameraEntry>(),
                    VolumeConfigs = new Dictionary<string, VolumeConfig>(),
                };
                if (!string.Equals(payload.Name, name, StringComparison.Ordinal))
                {
                    payload = new PresetPayload
                    {
                        Name = name,
                        Cameras = payload.Cameras,
                        VolumeConfigs = payload.VolumeConfigs,
                        ActiveCameraLogicalId = payload.ActiveCameraLogicalId,
                    };
                }
                _presets[name] = payload;
                _presetOrder.Add(name);
            }
            PublishPresetCommand(PresetCommandOps.Create, name);
            OnPresetListChanged?.Invoke();
            NotifyStateMutation();
            return PresetIoResult.Ok();
        }

        public PresetIoResult RenamePreset(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, "name is empty");
            lock (_modelLock)
            {
                if (!_presets.TryGetValue(oldName, out var existing))
                    return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, $"unknown: {oldName}");
                if (_presets.ContainsKey(newName))
                    return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, $"duplicate: {newName}");

                var renamed = new PresetPayload
                {
                    Name = newName,
                    Cameras = existing.Cameras,
                    VolumeConfigs = existing.VolumeConfigs,
                    ActiveCameraLogicalId = existing.ActiveCameraLogicalId,
                };
                _presets.Remove(oldName);
                _presets[newName] = renamed;
                var idx = _presetOrder.IndexOf(oldName);
                if (idx >= 0) _presetOrder[idx] = newName;
                if (_activeName == oldName) _activeName = newName;
            }
            PublishPresetCommand(PresetCommandOps.Rename, oldName, newName: newName);
            OnPresetListChanged?.Invoke();
            NotifyStateMutation();
            return PresetIoResult.Ok();
        }

        public PresetIoResult DuplicatePreset(string sourceName, string newName)
        {
            if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(newName))
                return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, "name is empty");
            lock (_modelLock)
            {
                if (!_presets.TryGetValue(sourceName, out var source))
                    return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, $"unknown: {sourceName}");
                if (_presets.ContainsKey(newName))
                    return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, $"duplicate: {newName}");

                var copy = new PresetPayload
                {
                    Name = newName,
                    Cameras = source.Cameras.ToArray(),
                    VolumeConfigs = new Dictionary<string, VolumeConfig>(source.VolumeConfigs),
                    ActiveCameraLogicalId = source.ActiveCameraLogicalId,
                };
                _presets[newName] = copy;
                _presetOrder.Add(newName);
            }
            PublishPresetCommand(PresetCommandOps.Duplicate, newName, sourceName: sourceName);
            OnPresetListChanged?.Invoke();
            NotifyStateMutation();
            return PresetIoResult.Ok();
        }

        public PresetIoResult DeletePreset(string name)
        {
            if (string.IsNullOrEmpty(name))
                return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, "name is empty");
            bool wasActive = false;
            lock (_modelLock)
            {
                if (!_presets.Remove(name))
                    return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, $"unknown: {name}");
                _presetOrder.Remove(name);
                if (_activeName == name)
                {
                    _activeName = null;
                    wasActive = true;
                }
            }
            PublishPresetCommand(PresetCommandOps.Delete, name);
            OnPresetListChanged?.Invoke();
            if (wasActive) OnActivePresetChanged?.Invoke(null);
            NotifyStateMutation();
            return PresetIoResult.Ok();
        }

        // ---- Activate ----

        /// <summary>
        /// Switch to <paramref name="targetName"/>: compute diff against the
        /// current logical layout described by <paramref name="currentSnapshot"/>
        /// and dispatch <c>delete → add → metadata → volume → active-set</c>
        /// commands in order. Subsequent activate calls wait for the first to
        /// finish (Requirement 11.5).
        /// </summary>
        public async Task<PresetIoResult> ActivatePresetAsync(
            string targetName,
            PresetPayload currentSnapshot,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(targetName))
                return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, "name is empty");
            PresetPayload target;
            lock (_modelLock)
            {
                if (!_presets.TryGetValue(targetName, out target!))
                    return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, $"unknown: {targetName}");
            }

            await _activateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DispatchSwitchCommands(currentSnapshot, target);
                lock (_modelLock) _activeName = targetName;
                PublishPresetCommand(PresetCommandOps.Activate, targetName);
                OnActivePresetChanged?.Invoke(targetName);
                NotifyStateMutation();
                return PresetIoResult.Ok();
            }
            finally
            {
                _activateGate.Release();
            }
        }

        /// <summary>
        /// Dispatches the diff in fixed order. Failures are recorded via
        /// <see cref="FailureAggregator"/>; the routine continues so a single
        /// hiccup does not abort the whole switch (Requirement 11.5).
        /// </summary>
        public void DispatchSwitchCommands(PresetPayload current, PresetPayload target)
        {
            // 1. delete (cameras present in current but not in target).
            var targetIds = new HashSet<string>(target.Cameras.Select(c => c.LogicalId), StringComparer.Ordinal);
            foreach (var c in current.Cameras)
            {
                if (!targetIds.Contains(c.LogicalId))
                {
                    Try(() => _commands.PublishEvent(CameraIpcTopics.CameraCommand, new CameraCommandPayload
                    {
                        Op = CameraCommandOps.Delete,
                        ClientRequestId = NewClientRequestId(),
                        CameraId = c.LogicalId,
                    }), $"delete {c.LogicalId}");
                }
            }
            // 2. add (cameras in target not in current).
            var currentIds = new HashSet<string>(current.Cameras.Select(c => c.LogicalId), StringComparer.Ordinal);
            foreach (var c in target.Cameras)
            {
                if (!currentIds.Contains(c.LogicalId))
                {
                    Try(() => _commands.PublishEvent(CameraIpcTopics.CameraCommand, new CameraCommandPayload
                    {
                        Op = CameraCommandOps.Add,
                        ClientRequestId = NewClientRequestId(),
                        Type = CameraTypeNames.ToWire(c.Type),
                        DisplayName = c.DisplayName,
                    }), $"add {c.LogicalId}");
                }
            }
            // 3. metadata (per-camera display name + default transform — type rarely changes).
            foreach (var c in target.Cameras)
            {
                if (CameraId.TryCreate(c.LogicalId, out var id))
                {
                    Try(() => _commands.PublishState(CameraIpcTopics.CameraMetadata(id, CameraMetadataKeys.DisplayName),
                        new CameraMetadataStatePayload
                        {
                            Value = JsonSerializer.SerializeToElement(c.DisplayName),
                        }), $"metadata displayName {c.LogicalId}");
                }
            }
            // 4. volume (per-camera enabled flag + override entries).
            foreach (var kv in target.VolumeConfigs)
            {
                if (!CameraId.TryCreate(kv.Key, out var id)) continue;
                Try(() => _commands.PublishState(CameraIpcTopics.VolumeEnabled(id),
                    new VolumeEnabledStatePayload { Enabled = kv.Value.Enabled }),
                    $"volume enabled {kv.Key}");
                foreach (var ov in kv.Value.Overrides)
                {
                    Try(() => _commands.PublishState(CameraIpcTopics.VolumeOverrideEnabled(id, ov.Type),
                        new VolumeOverrideEnabledStatePayload { Enabled = ov.Enabled }),
                        $"override enabled {kv.Key}/{ov.Type}");
                    foreach (var pv in ov.ParamValues)
                    {
                        Try(() => _commands.PublishState(CameraIpcTopics.VolumeOverrideParam(id, ov.Type, pv.Key),
                            new VolumeOverrideParamStatePayload { Value = pv.Value }),
                            $"param {kv.Key}/{ov.Type}/{pv.Key}");
                    }
                }
            }
            // 5. active-set.
            if (!string.IsNullOrEmpty(target.ActiveCameraLogicalId))
            {
                Try(() => _commands.PublishEvent(CameraIpcTopics.CameraCommand, new CameraCommandPayload
                {
                    Op = CameraCommandOps.ActiveSet,
                    ClientRequestId = NewClientRequestId(),
                    CameraId = target.ActiveCameraLogicalId,
                }), $"active-set {target.ActiveCameraLogicalId}");
            }
        }

        // ---- Persistence ----

        /// <summary>
        /// Bump the debounce — the next save fires <see cref="DefaultDebounce"/>
        /// after the most recent <see cref="NotifyStateMutation"/> call.
        /// </summary>
        public void NotifyStateMutation()
        {
            if (_disposed) return;
            _saveScheduled = true;
            _saveTimer.Bump();
        }

        /// <summary>
        /// Force a synchronous flush of any pending save (e.g. on tab dispose).
        /// </summary>
        public Task FlushPendingAsync(CancellationToken cancellationToken = default)
        {
            if (!_saveScheduled) return Task.CompletedTask;
            _saveTimer.Flush(); // synchronously enqueues SaveAllAsync
            return Task.CompletedTask;
        }

        /// <summary>
        /// Load presets from <see cref="IPresetStore"/>. Failures are aggregated
        /// but do not throw; corrupted-file backup paths are reported via
        /// <see cref="OnIoResult"/>.
        /// </summary>
        public async Task<PresetIoResult> RestoreOnStartAsync(CancellationToken cancellationToken = default)
        {
            var outcome = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            OnIoResult?.Invoke(outcome.Result);
            if (!outcome.Result.Success)
            {
                if (outcome.Result.FailureKind == PresetIoFailureKind.FileNotFound)
                {
                    // Cold start; nothing to restore. Treat as a soft success
                    // for the caller's perspective (Requirement 11.6).
                    return PresetIoResult.Ok();
                }
                _failures.Record(FailureKind.PresetIoFailure,
                    $"load failed: {outcome.Result.FailureKind} {outcome.Result.FailureDetail}",
                    _time.UtcNow);
                return outcome.Result;
            }

            lock (_modelLock)
            {
                _presets.Clear();
                _presetOrder.Clear();
                foreach (var p in outcome.Presets)
                {
                    if (string.IsNullOrEmpty(p.Name)) continue;
                    if (_presets.ContainsKey(p.Name)) continue;
                    _presets[p.Name] = p;
                    _presetOrder.Add(p.Name);
                }
                _activeName = outcome.ActivePresetName;
            }
            OnPresetListChanged?.Invoke();
            OnActivePresetChanged?.Invoke(outcome.ActivePresetName);
            return PresetIoResult.Ok();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _saveTimer.Flush(); } catch { /* defensive */ }
            try { _saveTimer.Dispose(); } catch { }
            try { _activateGate.Dispose(); } catch { }
        }

        // ---- private ----

        private void Try(Action act, string label)
        {
            try
            {
                act();
            }
            catch (Exception ex)
            {
                _failures.Record(FailureKind.IpcSendFailure,
                    $"preset switch step failed ({label}): {ex.Message}", _time.UtcNow);
            }
        }

        private static string NewClientRequestId() => Guid.NewGuid().ToString("N");

        private void PublishPresetCommand(string op, string name, string? newName = null, string? sourceName = null)
        {
            try
            {
                _commands.PublishEvent(CameraIpcTopics.PresetCommand, new PresetCommandPayload
                {
                    Op = op,
                    Name = name,
                    NewName = newName,
                    SourceName = sourceName,
                });
            }
            catch (Exception ex)
            {
                _failures.Record(FailureKind.IpcSendFailure,
                    $"preset/command failed ({op} {name}): {ex.Message}", _time.UtcNow);
            }
        }

        private void FlushFireAndForget()
        {
            // Capture a snapshot under lock; perform IO outside the lock.
            List<PresetPayload> snapshot;
            string? activeName;
            lock (_modelLock)
            {
                snapshot = _presetOrder.Select(n => _presets[n]).ToList();
                activeName = _activeName;
                _saveScheduled = false;
            }
            _ = SaveAsyncCore(snapshot, activeName);
        }

        private async Task SaveAsyncCore(IReadOnlyList<PresetPayload> snapshot, string? activeName)
        {
            try
            {
                var result = await _store.SaveAllAsync(snapshot, activeName).ConfigureAwait(false);
                if (result.Success)
                {
                    lock (_modelLock) _lastSavedAt = _time.UtcNow;
                }
                else
                {
                    _failures.Record(FailureKind.PresetIoFailure,
                        $"save failed: {result.FailureKind} {result.FailureDetail}", _time.UtcNow);
                }
                OnIoResult?.Invoke(result);
            }
            catch (Exception ex)
            {
                _failures.Record(FailureKind.PresetIoFailure, $"save threw: {ex.Message}", _time.UtcNow);
                OnIoResult?.Invoke(PresetIoResult.Fail(PresetIoFailureKind.WriteFailed, ex.Message, ex));
            }
        }
    }
}
