#nullable enable
using UnityEngine;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview
{
    /// <summary>
    /// Allocates and releases the preview <see cref="RenderTexture"/> consumed by
    /// <c>StagePreviewHost</c>. Centralized so the same allocation policy (size, format,
    /// name) is reused on PlayMode reset, manual recreate, and teardown paths.
    /// </summary>
    internal static class PreviewRenderTextureFactory
    {
        public const int DefaultWidth = 1280;
        public const int DefaultHeight = 720;

        public static RenderTexture Create(
            int width = DefaultWidth,
            int height = DefaultHeight,
            RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            var rt = new RenderTexture(width, height, depth: 16, format: format)
            {
                name = "PreviewRT",
            };
            rt.Create();
            return rt;
        }

        public static void Release(RenderTexture? rt)
        {
            if (rt == null) return;
            try { rt.Release(); } catch { /* ignore */ }
            try { Object.DestroyImmediate(rt); } catch { /* ignore */ }
        }
    }
}
