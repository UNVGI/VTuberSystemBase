#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Preview
{
    /// <summary>
    /// Owns the preview panel <see cref="VisualElement"/>: binds the live
    /// <see cref="RenderTexture"/> as its background image, keeps a placeholder visible
    /// while the host has no RT, and forwards lifecycle events
    /// (<c>preview/command set-enabled</c> / <c>reset-view</c>) into the IPC layer.
    /// See design.md §Preview §PreviewPanelController (Requirements 2.2, 2.6, 2.7, 2.8, 2.11).
    /// </summary>
    public sealed class PreviewPanelController : IDisposable
    {
        public const string PlaceholderClass = "vsb-slv-preview--placeholder";

        private readonly VisualElement _panel;
        private readonly IPreviewRenderTextureAccessor _accessor;
        private readonly IUiCommandClient _commandClient;
        private readonly IDiagnosticsLogger? _log;

        private bool _disposed;

        public PreviewPanelController(
            VisualElement panel,
            IPreviewRenderTextureAccessor accessor,
            IUiCommandClient commandClient,
            IDiagnosticsLogger? logger = null)
        {
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
            _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
            _log = logger;

            _accessor.RenderTextureChanged += OnRenderTextureChanged;
            _panel.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            ApplyRenderTexture(_accessor.TryGet());
        }

        public void OnActivated()
        {
            if (_disposed) return;
            // Refresh visual binding in case the host published a new RT while inactive.
            ApplyRenderTexture(_accessor.TryGet());
            SendPreviewCommand(new PreviewCommandDto("set-enabled", true));
        }

        public void OnDeactivated()
        {
            if (_disposed) return;
            SendPreviewCommand(new PreviewCommandDto("set-enabled", false));
        }

        public void ResetView()
        {
            if (_disposed) return;
            SendPreviewCommand(new PreviewCommandDto("reset-view", null));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _accessor.RenderTextureChanged -= OnRenderTextureChanged;
            // Clear the panel binding so a Disposed controller cannot keep the RT alive.
            _panel.style.backgroundImage = StyleKeyword.None;
            _panel.AddToClassList(PlaceholderClass);
        }

        // --------------------------------------------------------------------

        private void OnRenderTextureChanged(RenderTexture? rt)
        {
            if (_disposed) return;
            ApplyRenderTexture(rt);
        }

        private void ApplyRenderTexture(RenderTexture? rt)
        {
            if (rt is not null)
            {
                _panel.style.backgroundImage = Background.FromRenderTexture(rt);
                _panel.RemoveFromClassList(PlaceholderClass);
            }
            else
            {
                _panel.style.backgroundImage = StyleKeyword.None;
                if (!_panel.ClassListContains(PlaceholderClass))
                    _panel.AddToClassList(PlaceholderClass);
            }
        }

        private void SendPreviewCommand(PreviewCommandDto dto)
        {
            var result = _commandClient.PublishEvent(StageLightingTopics.PreviewCommand, dto);
            if (!result.Success)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"PreviewPanelController.PublishEvent failed op={dto.Op} code={result.Error?.Code}",
                    new { op = dto.Op, code = result.Error?.Code });
            }
        }
    }
}
