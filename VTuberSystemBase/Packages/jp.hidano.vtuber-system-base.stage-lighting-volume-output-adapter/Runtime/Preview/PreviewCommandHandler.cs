#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview
{
    /// <summary>
    /// Translates <c>preview/command</c> events into <see cref="StagePreviewHost"/> calls
    /// and republishes <c>preview/state</c> after every command. Subscribes to the host's
    /// <see cref="StagePreviewHost.RenderTextureChanged"/> so the UI receives an updated
    /// state when the RenderTexture is reallocated or released.
    /// </summary>
    internal sealed class PreviewCommandHandler : IDisposable
    {
        private readonly IOutputCommandDispatcher _dispatcher;
        private readonly StagePreviewHost _host;
        private readonly IAdapterMessageSink _sink;
        private readonly AdapterLogger _logger;
        private readonly StageLightingVolumeOutputAdapterDiagnostics _diagnostics;
        private readonly HandlerRegistrationToken _tokens = new();
        private Action<RenderTexture?>? _rtSubscription;
        private bool _disposed;

        public PreviewCommandHandler(
            IOutputCommandDispatcher dispatcher,
            StagePreviewHost host,
            IAdapterMessageSink sink,
            AdapterLogger logger,
            StageLightingVolumeOutputAdapterDiagnostics diagnostics)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public void Start()
        {
            _tokens.Add(_dispatcher.RegisterEventHandler<PreviewCommandDto>(
                StageLightingTopics.PreviewCommand, OnPreviewCommand));
            _diagnostics.IncrementHandlerCount(1);

            _rtSubscription = _ => PublishPreviewState();
            _host.RenderTextureChanged += _rtSubscription;
            _diagnostics.SetPreviewHostReady(_host.IsReady);
            PublishPreviewState();
        }

        private void OnPreviewCommand(EventCommand<PreviewCommandDto> cmd)
        {
            try
            {
                switch (cmd.Payload.Op)
                {
                    case "set-enabled":
                        _host.SetEnabled(cmd.Payload.Enabled ?? true);
                        break;
                    case "reset-view":
                        _host.ResetView();
                        break;
                    case "init":
                        _host.SetEnabled(true);
                        break;
                    case "dispose":
                        _host.SetEnabled(false);
                        break;
                    default:
                        _logger.Warning("PreviewCommandHandler", "unknown_op", context: cmd.Payload.Op,
                            topic: StageLightingTopics.PreviewCommand);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("PreviewCommandHandler", "command_failed", context: ex.Message,
                    topic: StageLightingTopics.PreviewCommand, exception: ex);
            }
            finally
            {
                PublishPreviewState();
            }
        }

        private void PublishPreviewState()
        {
            var enabled = _host.PreviewCamera != null && _host.PreviewCamera.enabled;
            _diagnostics.SetPreviewHostReady(_host.IsReady);
            _sink.PublishState(StageLightingTopics.PreviewState,
                new PreviewStateDto(Enabled: enabled, HostReady: _host.IsReady));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_rtSubscription != null)
                {
                    _host.RenderTextureChanged -= _rtSubscription;
                    _rtSubscription = null;
                }
            }
            catch { /* ignore */ }
            try { _tokens.Dispose(); } catch { /* ignore */ }
        }
    }
}
