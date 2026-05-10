#nullable enable
using System;
using UnityEngine;

namespace VTuberSystemBase.StageLightingVolumeTab.Preview
{
    /// <summary>
    /// UI-side abstraction for resolving the live preview <see cref="RenderTexture"/>.
    /// The production implementation reads <c>StagePreviewHostLocator.Current</c>;
    /// tests inject a fake to drive <see cref="RenderTextureChanged"/> manually.
    /// See design.md §Preview §PreviewRenderTextureAccessor (Requirements 2.1, 2.5).
    /// </summary>
    public interface IPreviewRenderTextureAccessor
    {
        /// <summary>True while a non-null RenderTexture is currently published.</summary>
        bool IsReady { get; }

        /// <summary>
        /// Returns the current preview RenderTexture or null when no host is
        /// registered yet (initial state) or the host has been disposed.
        /// </summary>
        RenderTexture? TryGet();

        /// <summary>
        /// Raised whenever the underlying RenderTexture is reallocated or released
        /// (null carries through to subscribers).
        /// </summary>
        event Action<RenderTexture?>? RenderTextureChanged;
    }
}
