#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Adapters.Preview
{
    /// <summary>
    /// Default <see cref="IPreviewHandleResolver"/> that holds a process-local
    /// dictionary mapping <c>textureKey</c> → <see cref="RenderTexture"/>. The
    /// main-output side registers entries via <see cref="Register"/>; the UI
    /// resolves them on-demand. The dictionary is the in-process fallback for
    /// the future Service-Locator-based <c>IMainOutputPreviewRegistry</c>
    /// (design.md §RenderTextureHandleResolver).
    /// </summary>
    public sealed class RenderTextureHandleResolver : IPreviewHandleResolver
    {
        private readonly Dictionary<string, RenderTexture> _registry = new Dictionary<string, RenderTexture>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        public void Register(string textureKey, RenderTexture texture)
        {
            if (string.IsNullOrEmpty(textureKey)) throw new ArgumentException("textureKey is empty", nameof(textureKey));
            lock (_lock) _registry[textureKey] = texture;
        }

        public bool Unregister(string textureKey)
        {
            if (string.IsNullOrEmpty(textureKey)) return false;
            lock (_lock) return _registry.Remove(textureKey);
        }

        public Task<PreviewHandleResolution> ResolveAsync(string textureKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_registry.TryGetValue(textureKey, out var rt))
                {
                    return Task.FromResult(PreviewHandleResolution.Hit(rt));
                }
            }
            return Task.FromResult(PreviewHandleResolution.Miss($"textureKey not registered: {textureKey}"));
        }

        public void Release(string textureKey)
        {
            // Reference is held by the main-output side; the UI is a borrower.
            // No reference counting here on purpose.
        }
    }
}
