#nullable enable
using System;
using SceneViewStyleCameraController;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview
{
    /// <summary>
    /// MonoBehaviour bridging the live preview <see cref="Camera"/> + <see cref="RenderTexture"/>
    /// to the UI-side <see cref="IPreviewHostService"/> contract via
    /// <see cref="StagePreviewHostLocator"/>. Created by <c>PreviewCameraFactory</c> and
    /// destroyed by the adapter Bootstrapper.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class StagePreviewHost : MonoBehaviour, IPreviewHostService
    {
        private RenderTexture? _rt;
        private Camera? _previewCamera;
        private SceneViewStyleCameraController.SceneViewStyleCameraController? _cameraController;
        private Vector3 _initialPosition;
        private Quaternion _initialRotation = Quaternion.identity;
        private bool _initialCaptured;

        // Optional logger; PreviewCameraFactory may inject before Awake runs.
        internal AdapterLogger? Logger { get; set; }

        public RenderTexture? CurrentRenderTexture => _rt;
        public bool IsReady { get; private set; }
        public Camera? PreviewCamera => _previewCamera;

        public event Action<RenderTexture?>? RenderTextureChanged;

        private void Awake()
        {
            try
            {
                _rt = PreviewRenderTextureFactory.Create();
                _previewCamera = GetComponent<Camera>();
                _cameraController = GetComponent<SceneViewStyleCameraController.SceneViewStyleCameraController>();
                if (_previewCamera != null)
                {
                    _previewCamera.targetTexture = _rt;
                    _initialPosition = _previewCamera.transform.localPosition;
                    _initialRotation = _previewCamera.transform.localRotation;
                    _initialCaptured = true;
                }
                StagePreviewHostLocator.Register(this);
                IsReady = true;
                RenderTextureChanged?.Invoke(_rt);
            }
            catch (Exception ex)
            {
                Logger?.Warning("StagePreviewHost", "awake_failed", context: ex.Message, exception: ex);
                IsReady = false;
            }
        }

        private void OnDestroy()
        {
            try
            {
                StagePreviewHostLocator.Unregister(this);
                RenderTextureChanged?.Invoke(null);
                if (_previewCamera != null) _previewCamera.targetTexture = null;
                PreviewRenderTextureFactory.Release(_rt);
                _rt = null;
                IsReady = false;
            }
            catch (Exception ex)
            {
                Logger?.Warning("StagePreviewHost", "destroy_failed", context: ex.Message, exception: ex);
            }
        }

        /// <summary>Toggles whether the preview camera renders. Safe before <see cref="IsReady"/>.</summary>
        public void SetEnabled(bool enabled)
        {
            if (_previewCamera != null) _previewCamera.enabled = enabled;
        }

        /// <summary>
        /// Resets the preview camera to its initial pose. The bundled
        /// <c>SceneViewStyleCameraController</c> v1.0.1 does not expose a ResetView API, so we
        /// fall back to driving the Transform directly.
        /// </summary>
        public void ResetView()
        {
            if (_previewCamera == null || !_initialCaptured) return;
            _previewCamera.transform.localPosition = _initialPosition;
            _previewCamera.transform.localRotation = _initialRotation;
        }

        /// <summary>
        /// Synchronizes the preview camera's culling mask with the main output camera so
        /// what the streamer sees in the preview matches the broadcast.
        /// </summary>
        public void SyncCullingMaskFromDefault(Camera defaultCam)
        {
            if (defaultCam == null || _previewCamera == null) return;
            _previewCamera.cullingMask = defaultCam.cullingMask;
        }

        /// <summary>Internal hook used by the Bootstrapper teardown.</summary>
        internal void DestroySafely()
        {
            if (this != null && this.gameObject != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(this.gameObject);
                else UnityEngine.Object.DestroyImmediate(this.gameObject);
            }
        }
    }
}
