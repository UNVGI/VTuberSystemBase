#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Scene;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Bootstrap
{
    /// <summary>
    /// Composition root for the Stage / Lighting / Volume output adapter. Should be added
    /// to the same scene as <c>OutputSceneBootstrapper</c>; resolves dependencies (output
    /// shell dispatcher and scene roots, plus the user-supplied <see cref="ICoreIpcBusProvider"/>),
    /// constructs each domain handler, and tears everything down on destruction.
    /// </summary>
    public sealed class StageLightingVolumeOutputAdapterBootstrapper : MonoBehaviour
    {
        [SerializeField] private bool _autoStart = true;
        [SerializeField] private OutputSceneBootstrapper? _outputSceneBootstrapper;

        private StageLightingVolumeOutputAdapterDiagnostics? _diagnostics;
        private AdapterLogger? _logger;
        private AdapterErrorReporter? _errorReporter;
        private IAdapterMessageSink? _sink;

        private StageHandler? _stage;
        private LightHandler? _light;
        private VolumeOverrideHandler? _volume;
        private PreviewCommandHandler? _preview;
        private StagePreviewHost? _previewHost;
        private bool _started;

        public StageLightingVolumeOutputAdapterDiagnostics? Diagnostics => _diagnostics;

        private void Awake()
        {
            if (!Application.isPlaying) return;
        }

        private void Start()
        {
            if (!Application.isPlaying || !_autoStart) return;
            TryStart();
        }

        public void TryStart()
        {
            if (_started) return;

            _logger = new AdapterLogger();
            _diagnostics = new StageLightingVolumeOutputAdapterDiagnostics();

            // Resolve OutputSceneBootstrapper (Dispatcher + Roots).
            var outputBoot = _outputSceneBootstrapper != null
                ? _outputSceneBootstrapper
                : UnityEngine.Object.FindFirstObjectByType<OutputSceneBootstrapper>();
            if (outputBoot == null || outputBoot.Dispatcher == null || outputBoot.Roots == null)
            {
                _logger.Warning("StageLightingVolumeOutputAdapterBootstrapper", "dependencies_missing",
                    context: "OutputSceneBootstrapper / Dispatcher / Roots not yet ready; aborting start");
                return;
            }
            var dispatcher = outputBoot.Dispatcher;
            var roots = outputBoot.Roots;

            // Resolve ICoreIpcBus through user-supplied provider.
            var provider = ResolveBusProvider();
            if (provider == null || provider.Bus == null)
            {
                _logger.Warning("StageLightingVolumeOutputAdapterBootstrapper", "ipc_bus_missing",
                    context: "ICoreIpcBusProvider not found; outbound publish is disabled until configured");
                return;
            }
            _sink = new CoreIpcBusMessageSink(provider.Bus);

            _errorReporter = new AdapterErrorReporter(_sink, _logger, _diagnostics);

            try
            {
                _stage = new StageHandler(dispatcher, roots, new AddressablesInstantiationProvider(),
                    new StageCatalogBuilder(_logger), _errorReporter, _logger, _diagnostics, _sink);
                _stage.Start();

                _light = new LightHandler(dispatcher, roots, _sink, _errorReporter, _logger, _diagnostics);
                _light.Start();

                _volume = new VolumeOverrideHandler(dispatcher, roots, _sink, _errorReporter, _logger, _diagnostics);
                _volume.Start();

                _previewHost = PreviewCameraFactory.Build(roots, _logger);
                _preview = new PreviewCommandHandler(dispatcher, _previewHost, _sink, _logger, _diagnostics);
                _preview.Start();

                _diagnostics.SetReady(true);
                _started = true;
            }
            catch (Exception ex)
            {
                _logger.Error("StageLightingVolumeOutputAdapterBootstrapper", "start_failed",
                    context: ex.Message, exception: ex);
            }
        }

        private ICoreIpcBusProvider? ResolveBusProvider()
        {
            // Prefer a sibling MonoBehaviour for explicit wiring; fall back to scene-wide search.
            var local = GetComponent<MonoBehaviour>();
            if (local is ICoreIpcBusProvider lp && lp.Bus != null) return lp;
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is ICoreIpcBusProvider p && p.Bus != null) return p;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying) return;
            // Reverse-order teardown; each disposal is guarded so a failure cannot block the rest.
            TryDispose(_preview, "preview");
            if (_previewHost != null)
            {
                try { _previewHost.DestroySafely(); }
                catch (Exception ex) { _logger?.Warning("StageLightingVolumeOutputAdapterBootstrapper", "previewhost_destroy_failed", context: ex.Message, exception: ex); }
                _previewHost = null;
            }
            TryDispose(_volume, "volume");
            TryDispose(_light, "light");
            TryDispose(_stage, "stage");
            _diagnostics?.SetReady(false);
            _started = false;
        }

        private void TryDispose(IDisposable? d, string label)
        {
            if (d == null) return;
            try { d.Dispose(); }
            catch (Exception ex)
            {
                _logger?.Warning("StageLightingVolumeOutputAdapterBootstrapper", "dispose_failed",
                    context: $"{label}: {ex.Message}", exception: ex);
            }
        }
    }
}
