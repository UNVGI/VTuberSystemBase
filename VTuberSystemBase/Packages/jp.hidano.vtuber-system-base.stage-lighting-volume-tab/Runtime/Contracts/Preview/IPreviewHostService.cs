using System;
using UnityEngine;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Same-process abstraction over the main-output-side <c>StagePreviewHost</c>
    /// MonoBehaviour. The active RenderTexture native handle cannot cross the IPC
    /// boundary, so the UI side acquires it through this service registered into
    /// <see cref="StagePreviewHostLocator"/>.
    /// </summary>
    public interface IPreviewHostService
    {
        /// <summary>
        /// Current preview RenderTexture, or null when the host is not yet ready or has
        /// been disposed.
        /// </summary>
        RenderTexture? CurrentRenderTexture { get; }

        /// <summary>
        /// True once the host has finished <c>Awake</c> and a RenderTexture is allocated.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Raised when the underlying RenderTexture is reallocated (e.g. resize) or
        /// released (null). Subscribers MUST drop cached references on null.
        /// </summary>
        event Action<RenderTexture?>? RenderTextureChanged;

        /// <summary>
        /// Live preview <see cref="Camera"/>, exposed so the UI-side
        /// <c>IPreviewCameraAdapter</c> can implement "reset view" by writing the
        /// camera's <see cref="Transform"/> directly. Returns null while the host is
        /// not yet ready or has been disposed.
        /// </summary>
        /// <remarks>
        /// Same-process only (D-1). Native pointer cannot cross IPC. Hosts MAY return
        /// null even when <see cref="IsReady"/> is true if the camera has not been
        /// allocated yet.
        /// </remarks>
        Camera? PreviewCamera { get; }
    }
}
