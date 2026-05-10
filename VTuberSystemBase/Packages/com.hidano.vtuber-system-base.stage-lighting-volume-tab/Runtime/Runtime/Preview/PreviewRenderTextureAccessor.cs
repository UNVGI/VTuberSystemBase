#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Preview
{
    /// <summary>
    /// Production <see cref="IPreviewRenderTextureAccessor"/> backed by
    /// <see cref="StagePreviewHostLocator"/>. Resolves the live RenderTexture by reading
    /// the currently registered <see cref="IPreviewHostService"/>; subscribes to that
    /// service's <see cref="IPreviewHostService.RenderTextureChanged"/> so the UI side
    /// observes RT replacements while the host is alive. When the host is unregistered
    /// the accessor returns null without throwing.
    /// See design.md §Preview §PreviewRenderTextureAccessor (Requirements 2.1, 2.2, 2.5).
    /// </summary>
    public sealed class PreviewRenderTextureAccessor : IPreviewRenderTextureAccessor, IDisposable
    {
        private IPreviewHostService? _trackedHost;
        private bool _disposed;

        public PreviewRenderTextureAccessor()
        {
            AttachIfPossible();
        }

        public bool IsReady
        {
            get
            {
                if (_disposed) return false;
                var host = StagePreviewHostLocator.Current;
                if (host is null) return false;
                if (!ReferenceEquals(host, _trackedHost))
                {
                    Rebind(host);
                }
                return host.CurrentRenderTexture != null;
            }
        }

        public RenderTexture? TryGet()
        {
            if (_disposed) return null;
            var host = StagePreviewHostLocator.Current;
            if (host is null)
            {
                if (_trackedHost is not null) Detach();
                return null;
            }
            if (!ReferenceEquals(host, _trackedHost))
            {
                Rebind(host);
            }
            return host.CurrentRenderTexture;
        }

        public event Action<RenderTexture?>? RenderTextureChanged;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
            RenderTextureChanged = null;
        }

        // --------------------------------------------------------------------

        private void AttachIfPossible()
        {
            var host = StagePreviewHostLocator.Current;
            if (host is null) return;
            Rebind(host);
        }

        private void Rebind(IPreviewHostService host)
        {
            Detach();
            _trackedHost = host;
            host.RenderTextureChanged += OnHostRenderTextureChanged;
        }

        private void Detach()
        {
            if (_trackedHost is null) return;
            _trackedHost.RenderTextureChanged -= OnHostRenderTextureChanged;
            _trackedHost = null;
        }

        private void OnHostRenderTextureChanged(RenderTexture? rt)
        {
            if (_disposed) return;
            RenderTextureChanged?.Invoke(rt);
        }
    }
}
