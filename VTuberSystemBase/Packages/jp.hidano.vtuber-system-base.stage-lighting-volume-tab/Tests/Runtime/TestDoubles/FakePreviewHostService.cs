#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IPreviewHostService"/> double used by tests that exercise
    /// <c>StagePreviewHostLocator</c> and the preview RT accessor without spawning a
    /// real <c>StagePreviewHost</c> MonoBehaviour. Tests drive the active RenderTexture
    /// through <see cref="SetTexture"/> which both updates <see cref="CurrentRenderTexture"/>
    /// and raises <see cref="RenderTextureChanged"/>.
    /// </summary>
    public sealed class FakePreviewHostService : IPreviewHostService
    {
        private RenderTexture? _texture;
        private bool _isReady;

        public RenderTexture? CurrentRenderTexture => _texture;

        public bool IsReady => _isReady;

        public event Action<RenderTexture?>? RenderTextureChanged;

        public void SetReady(bool ready)
        {
            _isReady = ready;
        }

        public void SetTexture(RenderTexture? rt)
        {
            _texture = rt;
            _isReady = rt != null;
            RenderTextureChanged?.Invoke(rt);
        }
    }
}
