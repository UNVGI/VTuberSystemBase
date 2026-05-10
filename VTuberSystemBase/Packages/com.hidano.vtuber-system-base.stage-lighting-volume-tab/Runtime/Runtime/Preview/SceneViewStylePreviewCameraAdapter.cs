#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Preview
{
    /// <summary>
    /// Production <see cref="IPreviewCameraAdapter"/>. Resolves the live preview
    /// <see cref="Camera"/> through <see cref="StagePreviewHostLocator"/>; on first
    /// resolution the camera's Transform + FOV are captured as the "default view"
    /// and <see cref="ResetToDefaultView"/> writes the snapshot back so the operator
    /// can recover from extreme orbit/pan input.
    /// </summary>
    /// <remarks>
    /// Why a Transform-level reset and not a controller call:
    /// SceneViewStyleCameraController (Hidano-Dev) does NOT expose a public
    /// <c>ResetView()</c> API; the only built-in reset is the internal Ctrl+R handler
    /// inside <c>Update()</c>. The package source was inspected at
    /// <c>com.hidano.scene-view-style-camera-controller@e447921b832e</c> while
    /// implementing this adapter (Task 4.2, design.md §Preview risk note).
    /// Writing the Transform directly while the controller's Update is running is safe
    /// because the controller reads the Transform every frame and re-pivots from there;
    /// the operator can always re-orbit after a reset.
    /// </remarks>
    public sealed class SceneViewStylePreviewCameraAdapter : IPreviewCameraAdapter, IDisposable
    {
        private IPreviewHostService? _trackedHost;
        private bool _disposed;

        private bool _hasSnapshot;
        private Vector3 _snapshotPosition;
        private Quaternion _snapshotRotation;
        private float _snapshotFov;

        public SceneViewStylePreviewCameraAdapter()
        {
            AttachIfPossible();
        }

        public bool IsAvailable
        {
            get
            {
                if (_disposed) return false;
                var host = StagePreviewHostLocator.Current;
                if (host is null)
                {
                    if (_trackedHost is not null) Detach(raiseChanged: true);
                    return false;
                }
                if (!ReferenceEquals(host, _trackedHost))
                {
                    Rebind(host);
                }
                return host.PreviewCamera != null;
            }
        }

        public event Action? OnAvailabilityChanged;

        public void ResetToDefaultView()
        {
            if (_disposed) return;
            var host = StagePreviewHostLocator.Current;
            if (host is null) return;
            if (!ReferenceEquals(host, _trackedHost)) Rebind(host);

            var camera = host.PreviewCamera;
            if (camera is null) return;

            // Capture lazily on first call so the snapshot reflects whatever the host's
            // Awake configured.
            if (!_hasSnapshot)
            {
                CaptureSnapshot(camera);
            }

            var t = camera.transform;
            t.position = _snapshotPosition;
            t.rotation = _snapshotRotation;
            camera.fieldOfView = _snapshotFov;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach(raiseChanged: false);
            OnAvailabilityChanged = null;
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
            Detach(raiseChanged: false);
            _trackedHost = host;
            _hasSnapshot = false;
            host.RenderTextureChanged += OnHostRtChanged;
            // Initial snapshot if camera already there.
            if (host.PreviewCamera != null) CaptureSnapshot(host.PreviewCamera);
            try
            {
                OnAvailabilityChanged?.Invoke();
            }
            catch
            {
                // Subscribers must not break the adapter.
            }
        }

        private void Detach(bool raiseChanged)
        {
            if (_trackedHost is null) return;
            _trackedHost.RenderTextureChanged -= OnHostRtChanged;
            _trackedHost = null;
            _hasSnapshot = false;
            if (raiseChanged)
            {
                try
                {
                    OnAvailabilityChanged?.Invoke();
                }
                catch
                {
                }
            }
        }

        private void OnHostRtChanged(RenderTexture? rt)
        {
            // RT change is a strong proxy for "camera was replaced"; refresh availability.
            try
            {
                OnAvailabilityChanged?.Invoke();
            }
            catch
            {
            }
        }

        private void CaptureSnapshot(Camera camera)
        {
            var t = camera.transform;
            _snapshotPosition = t.position;
            _snapshotRotation = t.rotation;
            _snapshotFov = camera.fieldOfView;
            _hasSnapshot = true;
        }
    }
}
