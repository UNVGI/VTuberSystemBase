#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Preview;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Validation;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.ViewModel
{
    /// <summary>
    /// Aggregates all UI logic for the stage-lighting-volume tab. View-side code reads
    /// the observable properties + subscribes to <see cref="OnStateChanged"/> /
    /// <see cref="OnValidationError"/> / <see cref="OnOperationWarning"/>; tests drive the
    /// Command methods directly with fake IPC / storage / clock dependencies.
    /// See design.md §ViewModel §StageLightingVolumeTabViewModel
    /// (Requirements 1.5, 1.6, 3.*, 4.*, 5.*, 6.*, 7.*, 8.5, 8.11, 9.*, 10.1-10.3, 12.1).
    /// </summary>
    /// <remarks>
    /// Sub-tasks 5.1-5.8 are implemented incrementally on this single class. The
    /// observable surface is designed so the View layer can repaint via
    /// <see cref="OnStateChanged"/> and operation warnings flow through
    /// <see cref="OnOperationWarning"/> as opaque codes ("ipc_disconnected",
    /// "stage_unresolved", "light_add_timeout", etc.).
    /// </remarks>
    public sealed class StageLightingVolumeTabViewModel : IDisposable
    {
        public static readonly TimeSpan DefaultLightAddTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan DefaultDisposeFlushTimeout = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromMilliseconds(500);

        // Operation warning codes shared with View layer.
        public const string WarnIpcDisconnected = "ipc_disconnected";
        public const string WarnStageLoadFailed = "stage_load_failed";
        public const string WarnStageInProgress = "stage_in_progress";
        public const string WarnStageUnresolved = "stage_unresolved";
        public const string WarnLightAddFailed = "light_add_failed";
        public const string WarnLightAddTimeout = "light_add_timeout";
        public const string WarnVolumeSchemaFailed = "volume_schema_failed";
        public const string WarnSendFailed = "send_failed";
        public const string WarnPersistenceFailed = "persistence_failed";

        private readonly IUiCommandClient _commandClient;
        private readonly IUiSubscriptionClient _subscriptionClient;
        private readonly IConnectionStatus _connectionStatus;
        private readonly IPresetStorage _presetStorage;
        private readonly LightListState _lightListState;
        private readonly StageCatalogState _stageCatalogState;
        private readonly VolumeSchemaCache _volumeSchemaCache;
        private readonly DebounceFlusher _debounceFlusher;
        private readonly IClock _clock;
        private readonly StageTabDiagnostics? _diagnostics;
        private readonly IDiagnosticsLogger? _log;

        private readonly List<PresetDto> _presets = new List<PresetDto>();
        private readonly List<ISubscriptionToken> _ownedSubscriptions = new List<ISubscriptionToken>();
        private readonly Dictionary<string, bool> _volumeOverrideEnabled =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<(string, string), VolumeOverrideParamValueDto> _volumeParamValues =
            new Dictionary<(string, string), VolumeOverrideParamValueDto>();
        private readonly Dictionary<(string, string), bool> _draggingProperties =
            new Dictionary<(string, string), bool>();
        private readonly Dictionary<string, PendingLightAdd> _pendingLightAdds =
            new Dictionary<string, PendingLightAdd>(StringComparer.Ordinal);

        private string? _activePresetName;
        private StageCurrentDto _stageCurrent;
        private string? _selectedLightId;
        private bool _isSwitchingStage;
        private bool _isSwitchingPreset;
        private bool _isInitialized;
        private bool _activated;
        private bool _disposed;

        public StageLightingVolumeTabViewModel(
            IUiCommandClient commandClient,
            IUiSubscriptionClient subscriptionClient,
            IConnectionStatus connectionStatus,
            IPresetStorage presetStorage,
            LightListState lightListState,
            StageCatalogState stageCatalogState,
            VolumeSchemaCache volumeSchemaCache,
            DebounceFlusher debounceFlusher,
            IClock clock,
            StageTabDiagnostics? diagnostics = null,
            IDiagnosticsLogger? logger = null)
        {
            _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
            _subscriptionClient = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
            _connectionStatus = connectionStatus ?? throw new ArgumentNullException(nameof(connectionStatus));
            _presetStorage = presetStorage ?? throw new ArgumentNullException(nameof(presetStorage));
            _lightListState = lightListState ?? throw new ArgumentNullException(nameof(lightListState));
            _stageCatalogState = stageCatalogState ?? throw new ArgumentNullException(nameof(stageCatalogState));
            _volumeSchemaCache = volumeSchemaCache ?? throw new ArgumentNullException(nameof(volumeSchemaCache));
            _debounceFlusher = debounceFlusher ?? throw new ArgumentNullException(nameof(debounceFlusher));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _diagnostics = diagnostics;
            _log = logger;

            _connectionStatus.OnStatusChanged += OnConnectionStatusChanged;
            _lightListState.Changed += OnLightListChanged;
            _stageCatalogState.Changed += OnStageCatalogChanged;
        }

        // ---------- Observable properties ----------

        public IReadOnlyList<PresetDto> Presets => _presets;
        public string? ActivePresetName => _activePresetName;
        public StageCurrentDto StageCurrent => _stageCurrent;
        public IReadOnlyList<StageCatalogEntryDto> StageCatalog => _stageCatalogState.Entries;
        public IReadOnlyList<LightListItemDto> Lights => _lightListState.CurrentList;
        public string? SelectedLightId => _selectedLightId;
        public VolumeOverrideSchemaDto? VolumeSchema => _volumeSchemaCache.Schema;
        public IReadOnlyDictionary<string, bool> VolumeOverrideEnabled => _volumeOverrideEnabled;
        public IReadOnlyDictionary<(string, string), VolumeOverrideParamValueDto> VolumeParamValues => _volumeParamValues;
        public bool IsConnected => _connectionStatus.IsConnected;
        public bool IsSwitchingStage => _isSwitchingStage;
        public bool IsSwitchingPreset => _isSwitchingPreset;
        public bool VolumeSchemaIsLoaded => _volumeSchemaCache.IsLoaded;

        // ---------- Events ----------

        public event Action? OnStateChanged;
        public event Action<string>? OnValidationError;
        public event Action<string>? OnOperationWarning;

        // ---------- Lifecycle ----------

        public void OnActivated()
        {
            if (_disposed) return;
            if (_activated) return;
            _activated = true;

            _lightListState.StartSubscribing();
            _stageCatalogState.StartSubscribing();

            // Stage current state subscription so external state pushes update UI.
            TrackSubscription(_subscriptionClient.Subscribe<StageCurrentDto>(
                StageLightingTopics.StageCurrent, MessageKind.State, OnStageCurrentEnvelope));

            // Stage loaded events ack a pending switch.
            TrackSubscription(_subscriptionClient.Subscribe<StageCurrentDto>(
                StageLightingTopics.StageLoaded, MessageKind.Event, OnStageLoadedEnvelope));

            // Stage load failed events surface as warnings.
            TrackSubscription(_subscriptionClient.Subscribe<StageLoadFailedDto>(
                StageLightingTopics.StageLoadFailed, MessageKind.Event, OnStageLoadFailedEnvelope));

            // Light added events resolve pending placeholders.
            TrackSubscription(_subscriptionClient.Subscribe<LightAddedDto>(
                StageLightingTopics.LightAdded, MessageKind.Event, OnLightAddedEnvelope));

            // Light error events surface as warnings.
            TrackSubscription(_subscriptionClient.Subscribe<LightErrorDto>(
                StageLightingTopics.LightError, MessageKind.Event, OnLightErrorEnvelope));

            _diagnostics?.SetIpcConnected(_connectionStatus.IsConnected);

            if (_connectionStatus.IsConnected && !_isInitialized)
            {
                // Fire-and-forget: schema fetch + preset load. Activation must remain synchronous.
                _ = InitializeAsync();
            }

            RaiseStateChanged();
        }

        public void OnDeactivated()
        {
            if (_disposed) return;
            if (!_activated) return;
            _activated = false;

            UnsubscribeAll();
            _lightListState.StopSubscribing();
            _stageCatalogState.StopSubscribing();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _connectionStatus.OnStatusChanged -= OnConnectionStatusChanged;
                _lightListState.Changed -= OnLightListChanged;
                _stageCatalogState.Changed -= OnStageCatalogChanged;
            }
            catch { /* best effort */ }

            UnsubscribeAll();
            _lightListState.Dispose();
            _stageCatalogState.Dispose();
            _debounceFlusher.Dispose();

            // Final flush with bounded wait so a slow disk cannot hang shutdown
            // (Requirement 8.4 / 11.3).
            try
            {
                var flushTask = _debounceFlusher.FlushImmediateAsync();
                flushTask.Wait((int)DefaultDisposeFlushTimeout.TotalMilliseconds);

                var storageFlush = _presetStorage.FlushAsync();
                storageFlush.Wait((int)DefaultDisposeFlushTimeout.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"ViewModel.Dispose flush exceeded budget: {ex.Message}");
            }
        }

        // ---------- Stage commands (Task 5.2) ----------

        public void SwitchStage(string addressableKey)
        {
            if (_disposed) return;
            if (!RequireConnected()) return;
            if (string.IsNullOrEmpty(addressableKey))
            {
                RaiseValidationError("invalid_stage_key");
                return;
            }
            if (_isSwitchingStage)
            {
                RaiseOperationWarning(WarnStageInProgress);
                return;
            }

            _isSwitchingStage = true;
            var dto = new StageCommandDto("load", addressableKey);
            var result = _commandClient.PublishEvent(StageLightingTopics.StageCommand, dto);
            _diagnostics?.LogCommandSent(StageLightingTopics.StageCommand, "event");
            if (!result.Success)
            {
                _isSwitchingStage = false;
                RaiseSendFailureWarning(result.Error);
            }
            RaiseStateChanged();
        }

        public void UnloadStage()
        {
            if (_disposed) return;
            if (!RequireConnected()) return;
            if (_isSwitchingStage)
            {
                RaiseOperationWarning(WarnStageInProgress);
                return;
            }

            _isSwitchingStage = true;
            var dto = new StageCommandDto("unload", null);
            var result = _commandClient.PublishEvent(StageLightingTopics.StageCommand, dto);
            _diagnostics?.LogCommandSent(StageLightingTopics.StageCommand, "event");
            if (!result.Success)
            {
                _isSwitchingStage = false;
                RaiseSendFailureWarning(result.Error);
            }
            RaiseStateChanged();
        }

        // ---------- Light commands (Task 5.3) ----------

        public string AddLight(LightInitialDto initial)
        {
            if (_disposed) return string.Empty;
            if (!RequireConnected()) return string.Empty;

            // Validate before sending.
            var v1 = LightPropertyValidator.ValidateIntensity(initial.Intensity);
            if (!v1.IsValid) { RaiseValidationError(v1.ErrorCode!); return string.Empty; }
            var v2 = LightPropertyValidator.ValidateRange(initial.Range);
            if (!v2.IsValid) { RaiseValidationError(v2.ErrorCode!); return string.Empty; }
            var v3 = LightPropertyValidator.ValidateSpotAngle(initial.SpotAngle);
            if (!v3.IsValid) { RaiseValidationError(v3.ErrorCode!); return string.Empty; }
            var v4 = LightPropertyValidator.ValidateColor(initial.Color);
            if (!v4.IsValid) { RaiseValidationError(v4.ErrorCode!); return string.Empty; }

            var correlationId = Guid.NewGuid().ToString("N");
            var dto = new LightCommandDto("add", null, initial);
            var result = _commandClient.PublishEvent(StageLightingTopics.LightCommand, dto);
            _diagnostics?.LogCommandSent(StageLightingTopics.LightCommand, "event");
            if (!result.Success)
            {
                RaiseSendFailureWarning(result.Error);
                return string.Empty;
            }

            var pending = new PendingLightAdd
            {
                CorrelationId = correlationId,
                Initial = initial,
                StartedAt = _clock.UtcNow,
            };
            _pendingLightAdds[correlationId] = pending;

            // Fire-and-forget timeout watcher.
            _ = WatchAddTimeoutAsync(correlationId);

            DebouncePersistenceFlush();
            RaiseStateChanged();
            return correlationId;
        }

        public void RemoveLight(string lightId)
        {
            if (_disposed) return;
            if (!RequireConnected()) return;
            if (string.IsNullOrEmpty(lightId)) { RaiseValidationError("invalid_light_id"); return; }

            var dto = new LightCommandDto("remove", lightId, null);
            var result = _commandClient.PublishEvent(StageLightingTopics.LightCommand, dto);
            _diagnostics?.LogCommandSent(StageLightingTopics.LightCommand, "event");
            if (!result.Success)
            {
                RaiseSendFailureWarning(result.Error);
                return;
            }
            if (string.Equals(_selectedLightId, lightId, StringComparison.Ordinal))
            {
                _selectedLightId = null;
            }
            DebouncePersistenceFlush();
            RaiseStateChanged();
        }

        public void SelectLight(string? lightId)
        {
            if (_disposed) return;
            if (string.Equals(_selectedLightId, lightId, StringComparison.Ordinal)) return;
            _selectedLightId = lightId;
            RaiseStateChanged();
        }

        public void UpdateLightProperty(string lightId, string property, object? value)
        {
            if (_disposed) return;
            if (!RequireConnected()) return;
            if (string.IsNullOrEmpty(lightId) || string.IsNullOrEmpty(property))
            {
                RaiseValidationError("invalid_arguments");
                return;
            }

            // Validation by known property.
            switch (property)
            {
                case StageLightingTopics.PropertyIntensity:
                    {
                        if (value is not float fv) { RaiseValidationError("type_mismatch"); return; }
                        var r = LightPropertyValidator.ValidateIntensity(fv);
                        if (!r.IsValid) { RaiseValidationError(r.ErrorCode!); return; }
                        Send(StageLightingTopics.LightProperty(lightId, property), fv);
                        break;
                    }
                case StageLightingTopics.PropertyRange:
                    {
                        if (value is not float fv) { RaiseValidationError("type_mismatch"); return; }
                        var r = LightPropertyValidator.ValidateRange(fv);
                        if (!r.IsValid) { RaiseValidationError(r.ErrorCode!); return; }
                        Send(StageLightingTopics.LightProperty(lightId, property), fv);
                        break;
                    }
                case StageLightingTopics.PropertySpotAngle:
                    {
                        if (value is not float fv) { RaiseValidationError("type_mismatch"); return; }
                        var r = LightPropertyValidator.ValidateSpotAngle(fv);
                        if (!r.IsValid) { RaiseValidationError(r.ErrorCode!); return; }
                        Send(StageLightingTopics.LightProperty(lightId, property), fv);
                        break;
                    }
                case StageLightingTopics.PropertyColor:
                    {
                        if (value is not ColorDto cv) { RaiseValidationError("type_mismatch"); return; }
                        var r = LightPropertyValidator.ValidateColor(cv);
                        if (!r.IsValid) { RaiseValidationError(r.ErrorCode!); return; }
                        Send(StageLightingTopics.LightProperty(lightId, property), cv);
                        break;
                    }
                case StageLightingTopics.PropertyRotation:
                    {
                        if (value is not Vector3Dto vv) { RaiseValidationError("type_mismatch"); return; }
                        Send(StageLightingTopics.LightProperty(lightId, property), vv);
                        break;
                    }
                case StageLightingTopics.PropertyType:
                    {
                        if (value is not LightTypeDto lt) { RaiseValidationError("type_mismatch"); return; }
                        Send(StageLightingTopics.LightProperty(lightId, property), lt);
                        break;
                    }
                case StageLightingTopics.PropertyDisplayName:
                    {
                        if (value is not string sv) { RaiseValidationError("type_mismatch"); return; }
                        Send(StageLightingTopics.LightProperty(lightId, property), sv);
                        break;
                    }
                default:
                    // Unknown property: forward with object payload (typed at the call site).
                    Send(StageLightingTopics.LightProperty(lightId, property), value);
                    break;
            }

            DebouncePersistenceFlush();
        }

        public void SetLightPropertyDragging(string lightId, string property, bool isDragging)
        {
            var key = (lightId, property);
            if (isDragging)
            {
                _draggingProperties[key] = true;
            }
            else
            {
                _draggingProperties.Remove(key);
            }
        }

        public bool IsLightPropertyDragging(string lightId, string property)
        {
            return _draggingProperties.TryGetValue((lightId, property), out var b) && b;
        }

        // ---------- Volume commands (Task 5.4) ----------

        public void SetVolumeOverrideEnabled(string typeFullName, bool enabled)
        {
            if (_disposed) return;
            if (!RequireConnected()) return;
            if (string.IsNullOrEmpty(typeFullName)) { RaiseValidationError("invalid_type_full_name"); return; }
            var topic = StageLightingTopics.VolumeOverrideEnabled(typeFullName);
            var result = _commandClient.PublishState(topic, enabled);
            _diagnostics?.LogCommandSent(topic, "state");
            if (!result.Success) { RaiseSendFailureWarning(result.Error); return; }
            _volumeOverrideEnabled[typeFullName] = enabled;
            DebouncePersistenceFlush();
            RaiseStateChanged();
        }

        public void UpdateVolumeOverrideParam(
            string typeFullName,
            string paramName,
            VolumeOverrideParamValueDto value)
        {
            if (_disposed) return;
            if (!RequireConnected()) return;
            if (string.IsNullOrEmpty(typeFullName) || string.IsNullOrEmpty(paramName))
            {
                RaiseValidationError("invalid_arguments");
                return;
            }

            // If schema is loaded, run range validation; otherwise forward as-is so the
            // ViewModel does not block the UI when the schema arrives lazily.
            if (_volumeSchemaCache.Schema is { } schema)
            {
                if (TryFindParamSchema(schema, typeFullName, paramName, out var pSchema))
                {
                    var v = LightPropertyValidator.ValidateVolumeParam(pSchema, value);
                    if (!v.IsValid)
                    {
                        RaiseValidationError(v.ErrorCode!);
                        return;
                    }
                }
            }

            var topic = StageLightingTopics.VolumeOverrideParam(typeFullName, paramName);
            var result = _commandClient.PublishState(topic, value);
            _diagnostics?.LogCommandSent(topic, "state");
            if (!result.Success) { RaiseSendFailureWarning(result.Error); return; }
            _volumeParamValues[(typeFullName, paramName)] = value;
            DebouncePersistenceFlush();
            RaiseStateChanged();
        }

        public async Task<bool> RetryVolumeSchemaFetchAsync()
        {
            if (_disposed) return false;
            _volumeSchemaCache.ResetCache();
            var ok = await _volumeSchemaCache.FetchAsync().ConfigureAwait(false);
            if (!ok)
            {
                RaiseOperationWarning(WarnVolumeSchemaFailed);
            }
            RaiseStateChanged();
            return ok;
        }

        // ---------- Preset CRUD (Task 5.5) ----------

        public PresetOpResult CreatePreset(string name)
        {
            if (_disposed) return PresetOpResult.Fail(PresetOpError.NotFound);
            if (string.IsNullOrWhiteSpace(name))
                return PresetOpResult.Fail(PresetOpError.EmptyName);
            if (FindPresetIndex(name) >= 0)
                return PresetOpResult.Fail(PresetOpError.DuplicateName);
            _presets.Add(new PresetDto { Name = name });
            DebouncePersistenceFlush();
            RaiseStateChanged();
            return PresetOpResult.Ok();
        }

        public PresetOpResult RenamePreset(string oldName, string newName)
        {
            if (_disposed) return PresetOpResult.Fail(PresetOpError.NotFound);
            if (string.IsNullOrWhiteSpace(newName))
                return PresetOpResult.Fail(PresetOpError.EmptyName);
            int idx = FindPresetIndex(oldName);
            if (idx < 0) return PresetOpResult.Fail(PresetOpError.NotFound);
            if (FindPresetIndex(newName) >= 0 && !string.Equals(oldName, newName, StringComparison.Ordinal))
                return PresetOpResult.Fail(PresetOpError.DuplicateName);
            _presets[idx].Name = newName;
            if (string.Equals(_activePresetName, oldName, StringComparison.Ordinal))
                _activePresetName = newName;
            DebouncePersistenceFlush();
            RaiseStateChanged();
            return PresetOpResult.Ok();
        }

        public PresetOpResult DuplicatePreset(string sourceName, string newName)
        {
            if (_disposed) return PresetOpResult.Fail(PresetOpError.NotFound);
            if (string.IsNullOrWhiteSpace(newName))
                return PresetOpResult.Fail(PresetOpError.EmptyName);
            int idx = FindPresetIndex(sourceName);
            if (idx < 0) return PresetOpResult.Fail(PresetOpError.NotFound);
            if (FindPresetIndex(newName) >= 0)
                return PresetOpResult.Fail(PresetOpError.DuplicateName);
            var clone = ClonePreset(_presets[idx], newName);
            _presets.Add(clone);
            DebouncePersistenceFlush();
            RaiseStateChanged();
            return PresetOpResult.Ok();
        }

        public PresetOpResult DeletePreset(string name)
        {
            if (_disposed) return PresetOpResult.Fail(PresetOpError.NotFound);
            int idx = FindPresetIndex(name);
            if (idx < 0) return PresetOpResult.Fail(PresetOpError.NotFound);
            _presets.RemoveAt(idx);
            if (string.Equals(_activePresetName, name, StringComparison.Ordinal))
            {
                _activePresetName = null;
            }
            DebouncePersistenceFlush();
            RaiseStateChanged();
            return PresetOpResult.Ok();
        }

        public PresetOpResult ActivatePreset(string name)
        {
            if (_disposed) return PresetOpResult.Fail(PresetOpError.NotFound);
            int idx = FindPresetIndex(name);
            if (idx < 0) return PresetOpResult.Fail(PresetOpError.NotFound);
            _activePresetName = name;
            _isSwitchingPreset = true;
            try
            {
                ApplyPresetSemantics(_presets[idx]);
            }
            finally
            {
                _isSwitchingPreset = false;
            }
            DebouncePersistenceFlush();
            RaiseStateChanged();
            return PresetOpResult.Ok();
        }

        // ---------- Internal helpers ----------

        private async Task InitializeAsync()
        {
            try
            {
                await _volumeSchemaCache.FetchAsync().ConfigureAwait(false);
                if (!_volumeSchemaCache.IsLoaded)
                {
                    RaiseOperationWarning(WarnVolumeSchemaFailed);
                }

                var loadResult = await _presetStorage.LoadAsync().ConfigureAwait(false);
                if (loadResult.Data is { } file)
                {
                    _presets.Clear();
                    foreach (var p in file.Presets) _presets.Add(p);
                    _activePresetName = file.ActivePresetName;
                    if (!string.IsNullOrEmpty(_activePresetName))
                    {
                        var idx = FindPresetIndex(_activePresetName!);
                        if (idx >= 0) ApplyPresetSemantics(_presets[idx]);
                    }
                }

                _isInitialized = true;
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"ViewModel.InitializeAsync failed: {ex.Message}");
            }
        }

        private void ApplyPresetSemantics(PresetDto preset)
        {
            // Step 1: disable currently enabled overrides.
            var enabledKeys = new List<string>(_volumeOverrideEnabled.Keys);
            foreach (var typeName in enabledKeys)
            {
                if (_volumeOverrideEnabled.TryGetValue(typeName, out var en) && en)
                {
                    var topic = StageLightingTopics.VolumeOverrideEnabled(typeName);
                    var r = _commandClient.PublishState(topic, false);
                    if (!r.Success) RaiseSendFailureWarning(r.Error);
                    _volumeOverrideEnabled[typeName] = false;
                }
            }

            // Step 2: switch stage.
            if (!string.IsNullOrEmpty(preset.StageAddressableKey))
            {
                if (_stageCatalogState.TryFind(preset.StageAddressableKey, out _))
                {
                    var sc = _commandClient.PublishEvent(
                        StageLightingTopics.StageCommand,
                        new StageCommandDto("load", preset.StageAddressableKey));
                    if (!sc.Success) RaiseSendFailureWarning(sc.Error);
                }
                else
                {
                    RaiseOperationWarning(WarnStageUnresolved);
                }
            }
            else
            {
                var sc = _commandClient.PublishEvent(
                    StageLightingTopics.StageCommand,
                    new StageCommandDto("unload", null));
                if (!sc.Success) RaiseSendFailureWarning(sc.Error);
            }

            // Step 3: remove all existing lights.
            var currentLightIds = new List<string>();
            foreach (var l in _lightListState.CurrentList) currentLightIds.Add(l.LightId);
            foreach (var lid in currentLightIds)
            {
                var r = _commandClient.PublishEvent(
                    StageLightingTopics.LightCommand,
                    new LightCommandDto("remove", lid, null));
                if (!r.Success) RaiseSendFailureWarning(r.Error);
            }

            // Step 4: add preset lights and queue param state for each.
            foreach (var lc in preset.Lights)
            {
                var initial = new LightInitialDto(
                    lc.Type, lc.Rotation, lc.Color, lc.Intensity, lc.Range, lc.SpotAngle, lc.DisplayName);
                var r = _commandClient.PublishEvent(
                    StageLightingTopics.LightCommand,
                    new LightCommandDto("add", null, initial));
                if (!r.Success) RaiseSendFailureWarning(r.Error);
            }

            // Step 5+6: re-enable preset volume overrides and apply params.
            foreach (var vo in preset.VolumeOverrides)
            {
                var enabledTopic = StageLightingTopics.VolumeOverrideEnabled(vo.TypeFullName);
                var r1 = _commandClient.PublishState(enabledTopic, vo.Enabled);
                if (!r1.Success) RaiseSendFailureWarning(r1.Error);
                _volumeOverrideEnabled[vo.TypeFullName] = vo.Enabled;
                foreach (var kv in vo.Params)
                {
                    var paramTopic = StageLightingTopics.VolumeOverrideParam(vo.TypeFullName, kv.Key);
                    var r2 = _commandClient.PublishState(paramTopic, kv.Value);
                    if (!r2.Success) RaiseSendFailureWarning(r2.Error);
                    _volumeParamValues[(vo.TypeFullName, kv.Key)] = kv.Value;
                }
            }
        }

        private void Send<T>(string topic, T payload)
        {
            var result = _commandClient.PublishState(topic, payload);
            _diagnostics?.LogCommandSent(topic, "state");
            if (!result.Success) RaiseSendFailureWarning(result.Error);
        }

        private void DebouncePersistenceFlush()
        {
            _debounceFlusher.Schedule(() => FlushPersistenceAsync());
        }

        private async Task FlushPersistenceAsync()
        {
            var snapshot = BuildSnapshot();
            try
            {
                var saveResult = await _presetStorage.SaveAsync(snapshot).ConfigureAwait(false);
                if (saveResult.Success)
                {
                    _diagnostics?.RecordPersistenceSave(_clock.UtcNow);
                }
                else
                {
                    _diagnostics?.LogPersistenceFailure("save", saveResult.Error?.ToString() ?? "unknown");
                    RaiseOperationWarning(WarnPersistenceFailed);
                }
            }
            catch (Exception ex)
            {
                _diagnostics?.LogPersistenceFailure("save", ex.Message);
                RaiseOperationWarning(WarnPersistenceFailed);
            }
        }

        private PresetFileRoot BuildSnapshot()
        {
            return new PresetFileRoot
            {
                SchemaVersion = 1,
                ActivePresetName = _activePresetName,
                Presets = new List<PresetDto>(_presets),
            };
        }

        private async Task WatchAddTimeoutAsync(string correlationId)
        {
            try
            {
                await _clock.Delay(DefaultLightAddTimeout, default).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (_disposed) return;
            if (_pendingLightAdds.Remove(correlationId))
            {
                RaiseOperationWarning(WarnLightAddTimeout);
                RaiseStateChanged();
            }
        }

        private bool RequireConnected()
        {
            if (!_connectionStatus.IsConnected)
            {
                RaiseOperationWarning(WarnIpcDisconnected);
                return false;
            }
            return true;
        }

        private void OnConnectionStatusChanged(ConnectionStatusEvent ev)
        {
            _diagnostics?.SetIpcConnected(ev.NextStatus == ConnectionStatusCode.Connected);
            if (ev.NextStatus == ConnectionStatusCode.Connected && _activated && !_isInitialized)
            {
                _ = InitializeAsync();
            }
            else if (ev.NextStatus == ConnectionStatusCode.Connected && _isInitialized)
            {
                // Connection recovery: refresh schema + re-publish state we hold.
                _volumeSchemaCache.ResetCache();
                _ = _volumeSchemaCache.FetchAsync();
            }
            RaiseStateChanged();
        }

        private void OnLightListChanged(LightListChangeEvent _) => RaiseStateChanged();
        private void OnStageCatalogChanged() => RaiseStateChanged();

        private void OnStageCurrentEnvelope(MessageEnvelope<StageCurrentDto> env)
        {
            _stageCurrent = env.Payload;
            _diagnostics?.SetCurrentStageKey(env.Payload.AddressableKey);
            RaiseStateChanged();
        }

        private void OnStageLoadedEnvelope(MessageEnvelope<StageCurrentDto> env)
        {
            _stageCurrent = env.Payload;
            _isSwitchingStage = false;
            _diagnostics?.SetCurrentStageKey(env.Payload.AddressableKey);
            RaiseStateChanged();
        }

        private void OnStageLoadFailedEnvelope(MessageEnvelope<StageLoadFailedDto> env)
        {
            _isSwitchingStage = false;
            RaiseOperationWarning(WarnStageLoadFailed);
            RaiseStateChanged();
        }

        private void OnLightAddedEnvelope(MessageEnvelope<LightAddedDto> env)
        {
            // Resolve the most recent pending add. We don't currently have a reliable
            // correlation id channel for light/added (event payload only carries lightId),
            // so any pending add ack arriving here is treated as fulfilling the oldest
            // pending entry.
            string? oldestKey = null;
            DateTimeOffset oldestAt = DateTimeOffset.MaxValue;
            foreach (var kv in _pendingLightAdds)
            {
                if (kv.Value.StartedAt < oldestAt)
                {
                    oldestAt = kv.Value.StartedAt;
                    oldestKey = kv.Key;
                }
            }
            if (oldestKey is not null) _pendingLightAdds.Remove(oldestKey);

            _diagnostics?.LogEventReceived(env.Topic, env.CorrelationId);
            RaiseStateChanged();
        }

        private void OnLightErrorEnvelope(MessageEnvelope<LightErrorDto> env)
        {
            _diagnostics?.LogEventReceived(env.Topic, env.CorrelationId);
            RaiseOperationWarning(WarnLightAddFailed);
            RaiseStateChanged();
        }

        private void TrackSubscription(ISubscriptionToken token)
        {
            _ownedSubscriptions.Add(token);
        }

        private void UnsubscribeAll()
        {
            for (int i = 0; i < _ownedSubscriptions.Count; i++)
            {
                try { _ownedSubscriptions[i].Dispose(); } catch { }
            }
            _ownedSubscriptions.Clear();
        }

        private int FindPresetIndex(string name)
        {
            for (int i = 0; i < _presets.Count; i++)
            {
                if (string.Equals(_presets[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private static PresetDto ClonePreset(PresetDto src, string newName)
        {
            var clone = new PresetDto
            {
                Name = newName,
                StageAddressableKey = src.StageAddressableKey,
            };
            foreach (var l in src.Lights)
            {
                clone.Lights.Add(new LightConfigDto
                {
                    DisplayName = l.DisplayName,
                    Type = l.Type,
                    Rotation = l.Rotation,
                    Color = l.Color,
                    Intensity = l.Intensity,
                    Range = l.Range,
                    SpotAngle = l.SpotAngle,
                });
            }
            foreach (var v in src.VolumeOverrides)
            {
                var voClone = new VolumeOverrideConfigDto
                {
                    TypeFullName = v.TypeFullName,
                    Enabled = v.Enabled,
                };
                foreach (var kv in v.Params) voClone.Params[kv.Key] = kv.Value;
                clone.VolumeOverrides.Add(voClone);
            }
            return clone;
        }

        private static bool TryFindParamSchema(
            VolumeOverrideSchemaDto schema, string typeFullName, string paramName,
            out VolumeOverrideParamDto found)
        {
            found = default;
            if (schema.Types is null) return false;
            for (int i = 0; i < schema.Types.Count; i++)
            {
                if (!string.Equals(schema.Types[i].TypeFullName, typeFullName, StringComparison.Ordinal))
                    continue;
                var ps = schema.Types[i].Params;
                if (ps is null) return false;
                for (int j = 0; j < ps.Count; j++)
                {
                    if (string.Equals(ps[j].ParamName, paramName, StringComparison.Ordinal))
                    {
                        found = ps[j];
                        return true;
                    }
                }
                return false;
            }
            return false;
        }

        private void RaiseStateChanged()
        {
            try { OnStateChanged?.Invoke(); } catch { /* never break the VM */ }
        }

        private void RaiseValidationError(string code)
        {
            try { OnValidationError?.Invoke(code); } catch { }
        }

        private void RaiseOperationWarning(string code)
        {
            try { OnOperationWarning?.Invoke(code); } catch { }
        }

        private void RaiseSendFailureWarning(SendError? error)
        {
            _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                $"ViewModel send failed code={error?.Code}",
                new { code = error?.Code });
            RaiseOperationWarning(WarnSendFailed);
        }

        private struct PendingLightAdd
        {
            public string CorrelationId;
            public LightInitialDto Initial;
            public DateTimeOffset StartedAt;
        }
    }

    /// <summary>Result of a preset CRUD operation. <see cref="Success"/> is mutually exclusive with <see cref="Error"/>.</summary>
    public readonly struct PresetOpResult
    {
        public bool Success { get; init; }
        public PresetOpError? Error { get; init; }

        public static PresetOpResult Ok() => new PresetOpResult { Success = true };
        public static PresetOpResult Fail(PresetOpError error) =>
            new PresetOpResult { Success = false, Error = error };
    }

    public enum PresetOpError
    {
        DuplicateName,
        NotFound,
        EmptyName,
        SendFailed,
    }
}
