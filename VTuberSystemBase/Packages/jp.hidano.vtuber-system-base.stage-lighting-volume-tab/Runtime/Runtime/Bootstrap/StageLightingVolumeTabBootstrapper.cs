#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Preview;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.View;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.StageLightingVolumeTab.Bootstrap
{
    /// <summary>
    /// Composition root for the stage-lighting-volume tab. Registers with the shell tab
    /// registry, builds the Services / ViewModel / Preview / View graph, wires
    /// activation callbacks to <see cref="ITabLifecycleHandle.OnActivated"/> /
    /// <see cref="ITabLifecycleHandle.OnDeactivated"/>, and ensures a bounded final
    /// flush on Dispose so a slow disk cannot hang shutdown.
    /// See design.md §Bootstrap §StageLightingVolumeTabBootstrapper
    /// (Requirements 1.3, 1.4, 1.5, 1.7, 11.1-11.7).
    /// </summary>
    public sealed class StageLightingVolumeTabBootstrapper : IDisposable
    {
        private readonly ITabLifecycleHandle? _tabHandle;
        private readonly StageLightingVolumeTabViewModel _viewModel;
        private readonly PreviewPanelController _previewController;
        private readonly StageLightingVolumeTabPanel _panel;
        private readonly StagePresetSectionView _presetSection;
        private readonly StageSelectionSectionView _stageSection;
        private readonly LightListSectionView _lightListSection;
        private readonly LightPropertyEditorView _lightEditor;
        private readonly VolumeOverrideSectionView _volumeSection;
        private readonly LightListState _lightListState;
        private readonly StageCatalogState _stageCatalogState;
        private readonly VolumeSchemaCache _volumeSchemaCache;
        private readonly DebounceFlusher _debounceFlusher;
        private readonly StageTabDiagnostics _diagnostics;
        private readonly IPresetStorage _presetStorage;
        private readonly IDiagnosticsLogger? _logger;

        private bool _disposed;

        public StageLightingVolumeTabBootstrapper(
            ITabPanelRegistry registry,
            VisualElement tabRoot,
            IUiCommandClient commandClient,
            IUiSubscriptionClient subscriptionClient,
            IAsyncAssetLoader assetLoader,
            IConnectionStatus connectionStatus,
            IDiagnosticsLogger? logger,
            IPresetStorage presetStorage,
            IPreviewRenderTextureAccessor previewAccessor,
            IPreviewCameraAdapter previewCameraAdapter,
            IClock clock)
        {
            if (registry is null) throw new ArgumentNullException(nameof(registry));
            if (tabRoot is null) throw new ArgumentNullException(nameof(tabRoot));
            if (commandClient is null) throw new ArgumentNullException(nameof(commandClient));
            if (subscriptionClient is null) throw new ArgumentNullException(nameof(subscriptionClient));
            if (assetLoader is null) throw new ArgumentNullException(nameof(assetLoader));
            if (connectionStatus is null) throw new ArgumentNullException(nameof(connectionStatus));
            if (presetStorage is null) throw new ArgumentNullException(nameof(presetStorage));
            if (previewAccessor is null) throw new ArgumentNullException(nameof(previewAccessor));
            if (previewCameraAdapter is null) throw new ArgumentNullException(nameof(previewCameraAdapter));
            if (clock is null) throw new ArgumentNullException(nameof(clock));

            _logger = logger;
            _presetStorage = presetStorage;
            _diagnostics = new StageTabDiagnostics(logger ?? new NullDiagnosticsLogger());

            // Build the view facade first so subsequent wiring can read VisualElement handles.
            _panel = new StageLightingVolumeTabPanel(tabRoot);

            _lightListState = new LightListState(subscriptionClient, logger);
            _stageCatalogState = new StageCatalogState(subscriptionClient, logger);
            _volumeSchemaCache = new VolumeSchemaCache(commandClient, logger);
            _debounceFlusher = new DebounceFlusher(
                StageLightingVolumeTabViewModel.DefaultDebounceInterval, clock);

            _viewModel = new StageLightingVolumeTabViewModel(
                commandClient, subscriptionClient, connectionStatus, presetStorage,
                _lightListState, _stageCatalogState, _volumeSchemaCache, _debounceFlusher,
                clock, _diagnostics, logger);

            _previewController = new PreviewPanelController(
                _panel.PreviewPanel, previewAccessor, commandClient, logger);

            _presetSection = new StagePresetSectionView(_panel.PresetSection, _viewModel);
            _stageSection = new StageSelectionSectionView(
                _panel.StageSelectionSection, _viewModel, assetLoader);
            _lightListSection = new LightListSectionView(_panel.LightListSection, _viewModel);
            _lightEditor = new LightPropertyEditorView(_panel.LightEditorSection, _viewModel);
            _volumeSection = new VolumeOverrideSectionView(
                _panel.VolumeOverrideSection, _viewModel,
                new VolumeOverrideParamFactory(logger));

            // Register tab. Idempotent failure: an already-registered tab is logged and the
            // bootstrapper continues with a null handle so re-entrancy from PlayMode iteration
            // does not crash the shell.
            try
            {
                _tabHandle = registry.RegisterTab(TabId.StageLighting,
                    new TabMetadata("Stage / Lighting / Volume"));
                _tabHandle.OnActivated += HandleActivated;
                _tabHandle.OnDeactivated += HandleDeactivated;
                _tabHandle.TrackAssetScope(assetLoader);
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"StageLightingVolumeTabBootstrapper RegisterTab failed (will continue): {ex.Message}");
            }

            registry.NotifyTabMounted(TabId.StageLighting, tabRoot);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_tabHandle is not null)
                {
                    _tabHandle.OnActivated -= HandleActivated;
                    _tabHandle.OnDeactivated -= HandleDeactivated;
                    _tabHandle.Dispose();
                }
            }
            catch { }

            try { _previewController.Dispose(); } catch { }
            try { _presetSection.Dispose(); } catch { }
            try { _stageSection.Dispose(); } catch { }
            try { _lightListSection.Dispose(); } catch { }
            try { _lightEditor.Dispose(); } catch { }
            try { _volumeSection.Dispose(); } catch { }
            try { _viewModel.Dispose(); } catch { }

            // Bounded final flush. ViewModel.Dispose already calls FlushImmediate on the
            // debouncer; this is the fallback that bounds storage flush time per Req 11.3.
            try
            {
                var flushTask = _presetStorage.FlushAsync();
                flushTask.Wait((int)StageLightingVolumeTabViewModel.DefaultDisposeFlushTimeout.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"StageLightingVolumeTabBootstrapper.Dispose flush exceeded budget: {ex.Message}");
            }
        }

        public StageTabDiagnosticsSnapshot CaptureDiagnosticsSnapshot()
            => _diagnostics.GetSnapshot();

        public StageLightingVolumeTabViewModel ViewModel => _viewModel;

        public PreviewPanelController PreviewController => _previewController;

        private void HandleActivated()
        {
            if (_disposed) return;
            _viewModel.OnActivated();
            _previewController.OnActivated();
            _panel.Show();
        }

        private void HandleDeactivated()
        {
            if (_disposed) return;
            _previewController.OnDeactivated();
            _viewModel.OnDeactivated();
            _panel.Hide();
        }

        // Tiny stub used when no logger was supplied so DI does not need to inject one.
        private sealed class NullDiagnosticsLogger : IDiagnosticsLogger
        {
            public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;
            public void Log(LogLevel level, LogCategory category, string message, object? context = null) { }
        }
    }
}
