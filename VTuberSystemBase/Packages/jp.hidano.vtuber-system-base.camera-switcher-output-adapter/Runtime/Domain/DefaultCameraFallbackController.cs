#nullable enable
using UnityEngine;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Domain
{
    /// <summary>
    /// Toggles <c>IOutputSceneRoots.DefaultCamera.enabled</c> based on the live
    /// camera count maintained by <see cref="CameraEntryRegistry"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="DefaultCamera"/> is left intact (per CSO-7) and merely
    /// disabled when at least one user-allocated camera is active. The shell
    /// guarantees the reference is non-null after <c>OutputSceneBootstrapper</c>
    /// finishes; if it is null this controller is a safe no-op.
    /// </remarks>
    public sealed class DefaultCameraFallbackController
    {
        private readonly Camera? _defaultCamera;
        private bool _isFallbackActive = true;

        public DefaultCameraFallbackController(Camera? defaultCamera)
        {
            _defaultCamera = defaultCamera;
            if (_defaultCamera != null) _defaultCamera.enabled = true;
        }

        /// <summary>
        /// True when the fallback DefaultCamera is currently the renderer.
        /// </summary>
        public bool IsFallbackActive => _isFallbackActive;

        public void NotifyCameraCountChanged(int count)
        {
            var shouldBeFallback = count <= 0;
            if (shouldBeFallback == _isFallbackActive) return;
            _isFallbackActive = shouldBeFallback;
            if (_defaultCamera != null) _defaultCamera.enabled = shouldBeFallback;
        }

        /// <summary>
        /// Forces the DefaultCamera back to <c>enabled = true</c> regardless of
        /// the previous count state. Used during Dispose / scene shutdown to
        /// preserve the shell's invariant that DefaultCamera is enabled when no
        /// user cameras are present.
        /// </summary>
        public void RestoreFallback()
        {
            _isFallbackActive = true;
            if (_defaultCamera != null) _defaultCamera.enabled = true;
        }
    }
}
