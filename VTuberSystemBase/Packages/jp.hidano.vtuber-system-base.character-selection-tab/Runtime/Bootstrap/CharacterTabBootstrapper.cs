#nullable enable
using System;
using System.Threading;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Diagnostics;
using VTuberSystemBase.CharacterSelectionTab.Ipc;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.View;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.CharacterSelectionTab.Bootstrap
{
    /// <summary>
    /// Composition root for the Character Selection Tab. (task 6.1.) Wires
    /// State → Services → Presenters → IpcBinder → RestoreOrchestrator on
    /// construction, validates the tab config, mounts presenters into the
    /// regions defined by <see cref="ViewQueryHelpers"/>, and tears every
    /// resource down idempotently in <see cref="Dispose"/>.
    /// </summary>
    public sealed class CharacterTabBootstrapper : IDisposable
    {
        private readonly ITabLifecycleHandle _handle;
        private readonly IUiCommandClient _cmd;
        private readonly IUiSubscriptionClient _sub;
        private readonly IConnectionStatus _conn;
        private readonly IAsyncAssetLoader _loader;
        private readonly IDiagnosticsLogger? _log;
        private readonly IPresetStorage _storage;
        private readonly IClock _clock;
        private readonly CharacterTabConfig _config;

        private readonly CharacterTabStateStore _store;
        private readonly InteractionGuard _guard;
        private readonly DynamicSettingControlFactory _factory;
        private readonly IAvatarThumbnailResolver _thumbnails;
        private readonly PresetStoreLogic _presets;
        private readonly CharacterTabIpcBinder _binder;
        private readonly PresetRestoreOrchestrator _restore;
        private readonly CharacterTabDiagnostics _diagnostics;

        private readonly SlotListPresenter _slotList;
        private readonly AvatarCatalogPresenter _avatarCatalog;
        private readonly AssignmentFlowPresenter _assignmentFlow;
        private readonly SettingsPanelPresenter _settingsPanel;
        private readonly PresetManagerPresenter _presetManager;
        private readonly TabDiagnosticsPresenter _diagPresenter;

        private bool _activated;
        private bool _disposed;

        public CharacterTabBootstrapper(
            ITabLifecycleHandle tabHandle,
            IUiCommandClient commandClient,
            IUiSubscriptionClient subscriptionClient,
            IConnectionStatus connectionStatus,
            IAsyncAssetLoader assetLoader,
            IDiagnosticsLogger? logger,
            IPresetStorage presetStorage,
            IClock clock,
            VisualElement tabRoot,
            IAvatarThumbnailResolver? thumbnailResolverOverride = null,
            CharacterTabConfig? configOverride = null,
            VisualTreeAsset? playerCardTemplate = null,
            VisualTreeAsset? avatarItemTemplate = null,
            VisualTreeAsset? presetBarTemplate = null)
        {
            _handle = tabHandle ?? throw new ArgumentNullException(nameof(tabHandle));
            _cmd = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
            _sub = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
            _conn = connectionStatus ?? throw new ArgumentNullException(nameof(connectionStatus));
            _loader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
            _storage = presetStorage ?? throw new ArgumentNullException(nameof(presetStorage));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (tabRoot is null) throw new ArgumentNullException(nameof(tabRoot));
            _log = logger;
            _config = ValidateConfig(configOverride ?? CharacterTabConfig.Default, _log);

            // Order: State → Services → Presenters → IpcBinder → RestoreOrchestrator
            _store = new CharacterTabStateStore();
            _guard = new InteractionGuard(_clock, _config.InteractionIdleThreshold);
            _factory = new DynamicSettingControlFactory(_log);
            _thumbnails = thumbnailResolverOverride
                ?? new AvatarThumbnailResolver(_loader, _config.DefaultThumbnailAddressableKey, _log);
            _presets = new PresetStoreLogic(_storage, _clock, _config.PresetDebounce, _log);

            // Region containers under the root UXML.
            var playerCardsRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.PlayerCardsRegion);
            var avatarCatalogRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.AvatarCatalogRegion);
            var settingsPanelRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.SettingsPanelRegion);
            var presetBarRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.PresetBarRegion);
            var diagnosticsRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.DiagnosticsRegion);

            _binder = new CharacterTabIpcBinder(_cmd, _sub, _store, _log);
            _restore = new PresetRestoreOrchestrator(_presets, _binder, _store, _conn, _log);

            _slotList = new SlotListPresenter(_store, playerCardsRegion, playerCardTemplate, _log);
            _avatarCatalog = new AvatarCatalogPresenter(
                _store, _thumbnails, avatarCatalogRegion, avatarItemTemplate, _config.PresetScopeId, _log);
            _assignmentFlow = new AssignmentFlowPresenter(
                _store, _binder, _clock, _config.AssignmentTimeout, _log);
            _settingsPanel = new SettingsPanelPresenter(
                _store, _binder, _factory, _guard, settingsPanelRegion, _config.SchemaRequestTimeout, _log);
            _presetManager = new PresetManagerPresenter(
                _presets, _store, _restore, presetBarRegion, presetBarTemplate, _log);
            _diagnostics = new CharacterTabDiagnostics(_store, _presets, _conn, () => _clock.UtcNow);
            _diagPresenter = new TabDiagnosticsPresenter(
                _diagnostics, _store, _presets, _clock, diagnosticsRegion);

            // Wire SlotList → AssignmentFlow / SettingsPanel.
            _slotList.OnSlotSelected = id => _assignmentFlow.SelectSlot(id);
            _slotList.OnResetRequested = id => _assignmentFlow.RequestOperation(id, AssignmentOperation.Reset);
            _slotList.OnReloadRequested = id => _assignmentFlow.RequestOperation(id, AssignmentOperation.Reload);
            _slotList.OnSettingsRequested = id => _ = _settingsPanel.OpenForAsync(id);
            // Wire Avatar catalog clicks → AssignmentFlow.RequestAssignment.
            _avatarCatalog.OnAvatarClicked = key => _assignmentFlow.RequestAssignment(key);

            // Lifecycle hooks.
            _handle.OnActivated += OnActivated;
            _handle.OnDeactivated += OnDeactivated;
            _handle.Track(_binder);
            _handle.Track(_restore);
            _handle.Track(_thumbnails);
            _handle.Track(_presets);
            _handle.Track(_slotList);
            _handle.Track(_avatarCatalog);
            _handle.Track(_assignmentFlow);
            _handle.Track(_settingsPanel);
            _handle.Track(_presetManager);
            _handle.Track(_diagPresenter);
            _handle.TrackAssetScope(_loader);

            // Subscribe upstream + load presets in parallel.
            _binder.SubscribeAll();
            _ = _presets.InitializeAsync(CancellationToken.None);

            // Probe the default thumbnail asset; failure is non-fatal but logged.
            DefaultThumbnailValidator.ValidateAsync(
                _loader, _config.DefaultThumbnailAddressableKey, _config.PresetScopeId, _log);

            _log?.Log(LogLevel.Info, LogCategory.TabSpec, "Init.Complete");
        }

        public bool IsRunning => !_disposed;
        public bool IsActivated => _activated;

        public TabDiagnosticsSnapshot CaptureDiagnostics() => _diagnostics.Capture();

        public ICharacterTabStateStore StoreForTesting => _store;
        public ICharacterTabIpcBinder BinderForTesting => _binder;
        public PresetStoreLogic PresetsForTesting => _presets;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle.OnActivated -= OnActivated;
            _handle.OnDeactivated -= OnDeactivated;
            try
            {
                _presets.FlushPendingAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"Init.FlushOnDispose failed: {ex.Message}");
            }
            // Disposing the handle drains every Track()'d resource.
            _handle.Dispose();
            _log?.Log(LogLevel.Info, LogCategory.TabSpec, "Init.Disposed");
        }

        // ---------- private ----------

        private void OnActivated()
        {
            if (_activated) return;
            _activated = true;
            _log?.Log(LogLevel.Debug, LogCategory.TabSpec, "Tab.Activated");
            // Resume render; subscriptions are kept on through deactivation
            // intentionally so background state stays fresh (design.md).
            _slotList.Render();
            _avatarCatalog.Render();
            _presetManager.RenderPresetBar();
            _diagPresenter.Render();
        }

        private void OnDeactivated()
        {
            if (!_activated) return;
            _activated = false;
            _log?.Log(LogLevel.Debug, LogCategory.TabSpec, "Tab.Deactivated");
        }

        private static CharacterTabConfig ValidateConfig(
            CharacterTabConfig config, IDiagnosticsLogger? log)
        {
            bool needsFallback = false;
            void NoteFault(string field, object value)
            {
                needsFallback = true;
                log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"CharacterTabConfig invalid: {field}={value}; falling back to default.");
            }
            if (config.PresetDebounce <= TimeSpan.Zero) NoteFault("PresetDebounce", config.PresetDebounce);
            if (config.AssignmentTimeout <= TimeSpan.Zero) NoteFault("AssignmentTimeout", config.AssignmentTimeout);
            if (config.SchemaRequestTimeout <= TimeSpan.Zero) NoteFault("SchemaRequestTimeout", config.SchemaRequestTimeout);
            if (config.InteractionIdleThreshold <= TimeSpan.Zero) NoteFault("InteractionIdleThreshold", config.InteractionIdleThreshold);
            if (string.IsNullOrEmpty(config.PresetScopeId)) NoteFault("PresetScopeId", config.PresetScopeId);
            if (string.IsNullOrEmpty(config.DefaultThumbnailAddressableKey))
                NoteFault("DefaultThumbnailAddressableKey", config.DefaultThumbnailAddressableKey);
            return needsFallback ? CharacterTabConfig.Default : config;
        }
    }
}
