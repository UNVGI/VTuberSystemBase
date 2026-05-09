#nullable enable
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview
{
    /// <summary>
    /// Builds the preview camera GameObject hierarchy: a child of <c>roots.Cameras</c> with
    /// <see cref="UnityEngine.Camera"/> + <c>UniversalAdditionalCameraData</c> +
    /// <c>SceneViewStyleCameraController</c> + <see cref="StagePreviewHost"/>. The Awake of
    /// <c>StagePreviewHost</c> registers the host into <c>StagePreviewHostLocator</c> in the
    /// same frame.
    /// </summary>
    internal static class PreviewCameraFactory
    {
        public static StagePreviewHost Build(IOutputSceneRoots roots, AdapterLogger? logger = null)
        {
            var go = new GameObject("PreviewCamera");
            go.transform.SetParent(roots.Cameras, worldPositionStays: false);

            var cam = go.AddComponent<UnityEngine.Camera>();
            if (roots.DefaultCamera != null)
            {
                cam.cullingMask = roots.DefaultCamera.cullingMask;
            }
            cam.targetDisplay = 0;

            // URP additional camera data; render type Base lets the preview drive its own
            // post-processing stack independently of the main output camera.
            try
            {
                var urpData = go.AddComponent<UniversalAdditionalCameraData>();
                urpData.renderType = CameraRenderType.Base;
            }
            catch (System.Exception ex)
            {
                logger?.Warning("PreviewCameraFactory", "urp_data_failed", context: ex.Message, exception: ex);
            }

            // Scene-view-style camera controller. Wire its private targetCamera field via
            // reflection so the controller drives our newly-allocated camera.
            try
            {
                var ctrl = go.AddComponent<SceneViewStyleCameraController.SceneViewStyleCameraController>();
                var field = typeof(SceneViewStyleCameraController.SceneViewStyleCameraController)
                    .GetField("targetCamera", BindingFlags.NonPublic | BindingFlags.Instance);
                field?.SetValue(ctrl, cam);
            }
            catch (System.Exception ex)
            {
                logger?.Warning("PreviewCameraFactory", "controller_failed", context: ex.Message, exception: ex);
            }

            var host = go.AddComponent<StagePreviewHost>();
            host.Logger = logger;
            return host;
        }
    }
}
