#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class PreviewRenderTextureFactoryTests
    {
        [Test]
        public void Create_ReturnsRtNamedPreviewRT_AndIsCreated()
        {
            RenderTexture? rt = PreviewRenderTextureFactory.Create();
            try
            {
                Assert.That(rt, Is.Not.Null);
                Assert.That(rt!.name, Is.EqualTo("PreviewRT"));
                Assert.That(rt.IsCreated(), Is.True);
                Assert.That(rt.width, Is.EqualTo(PreviewRenderTextureFactory.DefaultWidth));
                Assert.That(rt.height, Is.EqualTo(PreviewRenderTextureFactory.DefaultHeight));
            }
            finally { PreviewRenderTextureFactory.Release(rt); }
        }

        [Test]
        public void Release_Null_IsNoOp()
        {
            Assert.DoesNotThrow(() => PreviewRenderTextureFactory.Release(null));
        }
    }
}
