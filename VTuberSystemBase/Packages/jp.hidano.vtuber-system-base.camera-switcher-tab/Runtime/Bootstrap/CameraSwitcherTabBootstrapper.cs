#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Osc;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Persistence;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Preview;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Time;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Ucapi;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Diagnostics;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.View;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
namespace VTuberSystemBase.CameraSwitcherTab.Bootstrap
{
    /// <summary>
    /// Composition Root for the Camera Switcher Tab. Wires every Adapter
    /// (UCAPI / OSC / FileSystem preset / time / preview-handle resolver) into
    /// the Coordinator and binds the View layer onto the regions defined by
    /// <see cref="ViewQueryHelpers"/>. Mirrors the structure of
    /// <c>character-selection-tab.CharacterTabBootstrapper</c>.
    /// </summary>
    public sealed class CameraSwitcherTabBootstrapper : IDisposable
    {
        private readonly ITabLifecycleHandle _handle;
        private readonly IUiCommandClient _cmd;
        private readonly IUiSubscriptionClient _sub;
        private readonly IConnectionStatus _conn;
        private readonly IDiagnosticsLogger? _log;

        private readonly UnityTimeProvider _time;
        private readonly Ucapi4UnityFlatRecordSerializer _serializer;
        private readonly UoscFlatRecordEmitter _emitter;
        private readonly OscClientLifecycle _oscLifecycle;
        private readonly FileSystemPresetStore _presetStore;
        private readonly RenderTextureHandleResolver _previewResolver;

        private readonly CameraRegistry _registry;
        private readonly ActiveCameraTracker _tracker;
        private readonly TimeoutTracker _timeouts;
        private readonly FailureAggregator _failures;
        private readonly OscStreamController _oscStream;
        private readonly VolumeUiStateManager _volumeUi;
        private readonly PresetController _presets;
        private readonly PreviewSubscriptionController _preview;
        private readonly CameraSwitcherCoordinator _coordinator;

        private readonly CameraListView _cameraListView;
        private readonly LocalVolumeEditorView _volumeEditorView;
        private readonly PresetPanelView _presetPanelView;
        private readonly DiagnosticsBadgeView _badgeView;
        private readonly PreviewPanelView _previewPanelView;
        private readonly CameraSwitcherViewBinder _binder;
        private readonly CameraSwitcherTabDiagnostics _diagnostics;

        private bool _activated;
        private bool _disposed;

        public CameraSwitcherTabBootstrapper(
            ITabLifecycleHandle tabHandle,
            IUiCommandClient commandClient,
            IUiSubscriptionClient subscriptionClient,
            IConnectionStatus connectionStatus,
            IAsyncAssetLoader assetLoader,
            IDiagnosticsLogger? logger,
            VisualElement tabRoot,
            string? oscHost = null,
            int? oscPort = null,
            string? presetFilePath = null,
            RenderTextureHandleResolver? previewResolverOverride = null)
        {
            _handle = tabHandle ?? throw new ArgumentNullException(nameof(tabHandle));
            _cmd = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
            _sub = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
            _conn = connectionStatus ?? throw new ArgumentNullException(nameof(connectionStatus));
            if (assetLoader is null) throw new ArgumentNullException(nameof(assetLoader));
            if (tabRoot is null) throw new ArgumentNullException(nameof(tabRoot));
            _log = logger;

            // ---- Adapters ----
            _time = new UnityTimeProvider();
            _serializer = new Ucapi4UnityFlatRecordSerializer();
            _emitter = new UoscFlatRecordEmitter();
            _oscLifecycle = new OscClientLifecycle(_emitter, oscHost, oscPort, _log);
            _presetStore = new FileSystemPresetStore(presetFilePath, _log);
            _previewResolver = previewResolverOverride ?? new RenderTextureHandleResolver();

            // ---- Domain ----
            _registry = new CameraRegistry();
            _tracker = new ActiveCameraTracker();
            _failures = new FailureAggregator();
            _timeouts = new TimeoutTracker(_time);
            _oscStream = new OscStreamController(_serializer, _emitter, _failures, _time);
            _volumeUi = new VolumeUiStateManager(_cmd, _failures, _time);
            _presets = new PresetController(_presetStore, _cmd, _time, _failures);
            _preview = new PreviewSubscriptionController(_cmd, _previewResolver);

            _coordinator = new CameraSwitcherCoordinator(
                _cmd, _sub, _conn, _time,
                _registry, _tracker, _timeouts, _failures,
                _oscStream, _volumeUi, _presets, _preview, _log);

            // ---- View ----
            var cameraListRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.CameraListRegion);
            var volumeEditorRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.VolumeEditorRegion);
            var presetPanelRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.PresetPanelRegion);
            var diagnosticsRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.DiagnosticsRegion);
            var previewActiveRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.PreviewActiveRegion);
            var previewMultiRegion = ViewQueryHelpers.RequireByName(tabRoot, ViewQueryHelpers.PreviewMultiRegion);

            _cameraListView = new CameraListView(_coordinator, cameraListRegion);
            _cameraListView.OnAddCameraClicked = () => _coordinator.RequestAddCamera(CameraType.Perspective, null);
            _volumeEditorView = new LocalVolumeEditorView(_coordinator, _volumeUi, volumeEditorRegion);
            _presetPanelView = new PresetPanelView(_coordinator, _presets, presetPanelRegion);
            _badgeView = new DiagnosticsBadgeView(
                () => _coordinator.Status,
                () => _oscLifecycle.EmitterState == OscEmitterState.Running,
                _failures, diagnosticsRegion);
            _previewPanelView = new PreviewPanelView(_coordinator, _preview, previewActiveRegion, previewMultiRegion);

            void RenderAll()
            {
                _cameraListView.Render();
                _volumeEditorView.Render();
                _presetPanelView.Render();
                _badgeView.Render();
                _previewPanelView.Render();
            }
            _binder = new CameraSwitcherViewBinder(_coordinator, RenderAll);

            _diagnostics = new CameraSwitcherTabDiagnostics(_coordinator, _presets, _oscLifecycle, _conn);

            // Lifecycle hooks.
            _handle.OnActivated += OnActivated;
            _handle.OnDeactivated += OnDeactivated;
            _handle.Track(_binder);
            foreach (var sub in _coordinator.SubscribeAll())
            {
                _handle.Track(sub);
            }

            // Initial render + restore presets in the background.
            RenderAll();
            _ = _presets.RestoreOnStartAsync();
            _ = _oscLifecycle.StartAsync();

            _log?.Log(LogLevel.Info, LogCategory.TabSpec, "CameraSwitcherTab.Init complete");
        }

        public CameraSwitcherCoordinator Coordinator => _coordinator;
        public CameraSwitcherTabDiagnostics Diagnostics => _diagnostics;
        public OscClientLifecycle OscLifecycle => _oscLifecycle;
        public RenderTextureHandleResolver PreviewResolver => _previewResolver;
        public UnityTimeProvider TimeProvider => _time;
        public bool IsRunning => !_disposed;
        public bool IsActivated => _activated;

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
                    $"PresetController flush failed on dispose: {ex.Message}");
            }

            _coordinator.Dispose();
            try { _oscLifecycle.Dispose(); } catch { }
            try { _emitter.Dispose(); } catch { }
            _handle.Dispose();
            _log?.Log(LogLevel.Info, LogCategory.TabSpec, "CameraSwitcherTab.Disposed");
        }

        private void OnActivated()
        {
            if (_activated) return;
            _activated = true;
            _coordinator.OnTabActivated();
            _log?.Log(LogLevel.Debug, LogCategory.TabSpec, "CameraSwitcherTab.Activated");
        }

        private void OnDeactivated()
        {
            if (!_activated) return;
            _activated = false;
            _coordinator.OnTabDeactivated();
            _log?.Log(LogLevel.Debug, LogCategory.TabSpec, "CameraSwitcherTab.Deactivated");
        }
    }
}
