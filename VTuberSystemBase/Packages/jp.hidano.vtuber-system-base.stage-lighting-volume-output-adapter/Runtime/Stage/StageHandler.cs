#nullable enable
using System;
using System.Threading;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage
{
    /// <summary>
    /// Handles all stage-related IPC topics: receives <c>stage/command</c> events to load
    /// and unload Addressables-backed stage prefabs, performs lazy-swap (instantiate new ->
    /// release old) so the live render never goes black, and publishes <c>stage/catalog</c>,
    /// <c>stage/current</c>, <c>stage/loaded</c>, <c>stage/load-failed</c>.
    /// </summary>
    internal sealed class StageHandler : IDisposable
    {
        private readonly IOutputCommandDispatcher _dispatcher;
        private readonly IOutputSceneRoots _roots;
        private readonly IInstantiationProvider _provider;
        private readonly StageCatalogBuilder _catalogBuilder;
        private readonly AdapterErrorReporter _errorReporter;
        private readonly AdapterLogger _logger;
        private readonly StageLightingVolumeOutputAdapterDiagnostics _diagnostics;
        private readonly IAdapterMessageSink _sink;
        private readonly ActiveStageState _state = new();
        private readonly HandlerRegistrationToken _tokens = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public StageHandler(
            IOutputCommandDispatcher dispatcher,
            IOutputSceneRoots roots,
            IInstantiationProvider provider,
            StageCatalogBuilder catalogBuilder,
            AdapterErrorReporter errorReporter,
            AdapterLogger logger,
            StageLightingVolumeOutputAdapterDiagnostics diagnostics,
            IAdapterMessageSink sink)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _roots = roots ?? throw new ArgumentNullException(nameof(roots));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _catalogBuilder = catalogBuilder ?? throw new ArgumentNullException(nameof(catalogBuilder));
            _errorReporter = errorReporter ?? throw new ArgumentNullException(nameof(errorReporter));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        internal ActiveStageState State => _state;

        public void Start()
        {
            // Subscribe to stage/command (event) for load/unload requests.
            _tokens.Add(_dispatcher.RegisterEventHandler<StageCommandDto>(
                StageLightingTopics.StageCommand,
                cmd => _ = HandleCommandAsync(cmd.Payload)));
            _diagnostics.IncrementHandlerCount(1);

            // Initial publish: stage/current = null until first load completes.
            _sink.PublishState(StageLightingTopics.StageCurrent, new StageCurrentDto(null));

            // Fire-and-forget catalog publish.
            _ = PublishCatalogAsync();
        }

        private async System.Threading.Tasks.Task PublishCatalogAsync()
        {
            try
            {
                var dto = await _catalogBuilder.BuildAsync(_provider, _cts.Token).ConfigureAwait(true);
                _sink.PublishState(StageLightingTopics.StageCatalog, dto);
            }
            catch (Exception ex)
            {
                _logger.Error("StageHandler", "catalog_publish_failed", context: ex.Message, exception: ex);
            }
        }

        internal async System.Threading.Tasks.Task HandleCommandAsync(StageCommandDto cmd)
        {
            try
            {
                if (string.Equals(cmd.Op, "load", StringComparison.Ordinal))
                {
                    await HandleLoadAsync(cmd.AddressableKey ?? string.Empty).ConfigureAwait(true);
                }
                else if (string.Equals(cmd.Op, "unload", StringComparison.Ordinal))
                {
                    HandleUnload();
                }
                else
                {
                    _logger.Warning("StageHandler", "unknown_op", context: cmd.Op,
                        topic: StageLightingTopics.StageCommand);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("StageHandler", "command_failed", context: ex.Message, exception: ex);
            }
        }

        private async System.Threading.Tasks.Task HandleLoadAsync(string addressableKey)
        {
            if (string.IsNullOrEmpty(addressableKey))
            {
                _errorReporter.ReportStageLoadFailed(addressableKey ?? string.Empty,
                    "not_found", "addressableKey is null or empty");
                return;
            }

            _state.SetLoading(true);
            InstantiationResult result;
            try
            {
                result = await _provider.InstantiateAsync(addressableKey, _roots.Stage, _cts.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _state.SetLoading(false);
                _errorReporter.ReportStageLoadFailed(addressableKey, "instantiate_failed", ex.Message);
                return;
            }

            if (!result.Success || result.Instance == null)
            {
                _state.SetLoading(false);
                _errorReporter.ReportStageLoadFailed(addressableKey,
                    result.ErrorCode ?? "load_failed",
                    result.ErrorMessage ?? "instantiate failure");
                return;
            }

            // Lazy swap: release the previous instance only after the new one is ready.
            var oldStage = _state.CurrentStage;
            _state.SetActive(result.Instance, addressableKey);
            if (oldStage != null)
            {
                try { _provider.ReleaseInstance(oldStage); }
                catch (Exception ex) { _logger.Error("StageHandler", "release_failed", context: ex.Message, exception: ex); }
            }

            _diagnostics.SetCurrentStageAddressableKey(addressableKey);
            _sink.PublishState(StageLightingTopics.StageCurrent, new StageCurrentDto(addressableKey));
            _sink.PublishEvent(StageLightingTopics.StageLoaded, new StageCurrentDto(addressableKey));
        }

        private void HandleUnload()
        {
            var oldStage = _state.CurrentStage;
            if (oldStage != null)
            {
                try { _provider.ReleaseInstance(oldStage); }
                catch (Exception ex) { _logger.Error("StageHandler", "release_failed", context: ex.Message, exception: ex); }
            }
            _state.Clear();
            _diagnostics.SetCurrentStageAddressableKey(null);
            _sink.PublishState(StageLightingTopics.StageCurrent, new StageCurrentDto(null));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts.Cancel(); } catch { /* ignore */ }
            try { _tokens.Dispose(); } catch { /* ignore */ }
            // Release the live stage so the asset reference count drops to zero.
            var stage = _state.CurrentStage;
            if (stage != null)
            {
                try { _provider.ReleaseInstance(stage); } catch { /* ignore */ }
            }
            _state.Clear();
            _cts.Dispose();
        }
    }
}
