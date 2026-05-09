#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Preview;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// <see cref="IPreviewRenderTextureAccessor"/> double for tests. Stores a manually
    /// assigned RenderTexture and replays it through <see cref="RenderTextureChanged"/>.
    /// (Task 1.2, Requirements 2.1, 2.5, 12.1)
    /// </summary>
    public sealed class FakePreviewRenderTextureAccessor : IPreviewRenderTextureAccessor
    {
        private RenderTexture? _texture;

        public bool IsReady => _texture != null;

        public RenderTexture? TryGet() => _texture;

        public event Action<RenderTexture?>? RenderTextureChanged;

        /// <summary>Sets the current RenderTexture and raises the change event.</summary>
        public void SetTexture(RenderTexture? rt)
        {
            _texture = rt;
            RenderTextureChanged?.Invoke(rt);
        }
    }
}
