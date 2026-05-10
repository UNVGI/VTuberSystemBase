#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Preview;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the locator-driven RenderTexture resolution implemented by
    /// <see cref="PreviewRenderTextureAccessor"/> (Task 4.1, Requirements 2.1, 2.2, 2.5).
    /// </summary>
    [TestFixture]
    public sealed class PreviewRenderTextureAccessorTests
    {
        [TearDown]
        public void TearDown()
        {
            // The locator is process-static; clean it up to keep tests isolated.
            var current = StagePreviewHostLocator.Current;
            if (current is not null) StagePreviewHostLocator.Unregister(current);
        }

        [Test]
        public void NoHostRegistered_TryGet_ReturnsNull_AndIsReadyFalse()
        {
            var sut = new PreviewRenderTextureAccessor();

            Assert.That(sut.TryGet(), Is.Null);
            Assert.That(sut.IsReady, Is.False);
        }

        [Test]
        public void HostRegisteredWithRT_TryGet_ReturnsRT()
        {
            var rt = new RenderTexture(64, 64, 0);
            try
            {
                var host = new FakePreviewHostService();
                host.SetTexture(rt);
                StagePreviewHostLocator.Register(host);

                var sut = new PreviewRenderTextureAccessor();
                Assert.That(sut.TryGet(), Is.SameAs(rt));
                Assert.That(sut.IsReady, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        [Test]
        public void RenderTextureChanged_PropagatesToSubscribers()
        {
            var hostRt1 = new RenderTexture(32, 32, 0);
            var hostRt2 = new RenderTexture(64, 64, 0);
            try
            {
                var host = new FakePreviewHostService();
                host.SetTexture(hostRt1);
                StagePreviewHostLocator.Register(host);

                var sut = new PreviewRenderTextureAccessor();
                RenderTexture? captured = null;
                int callCount = 0;
                sut.RenderTextureChanged += rt => { captured = rt; callCount++; };

                host.SetTexture(hostRt2);

                Assert.That(callCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(captured, Is.SameAs(hostRt2));
            }
            finally
            {
                Object.DestroyImmediate(hostRt1);
                Object.DestroyImmediate(hostRt2);
            }
        }

        [Test]
        public void HostUnregistered_TryGet_ReturnsNull()
        {
            var rt = new RenderTexture(32, 32, 0);
            try
            {
                var host = new FakePreviewHostService();
                host.SetTexture(rt);
                StagePreviewHostLocator.Register(host);

                var sut = new PreviewRenderTextureAccessor();
                Assert.That(sut.TryGet(), Is.Not.Null);

                StagePreviewHostLocator.Unregister(host);
                Assert.That(sut.TryGet(), Is.Null);
                Assert.That(sut.IsReady, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }

        [Test]
        public void Dispose_StopsForwardingRenderTextureChanges()
        {
            var rt = new RenderTexture(32, 32, 0);
            try
            {
                var host = new FakePreviewHostService();
                host.SetTexture(rt);
                StagePreviewHostLocator.Register(host);

                var sut = new PreviewRenderTextureAccessor();
                int calls = 0;
                sut.RenderTextureChanged += _ => calls++;

                sut.Dispose();

                host.SetTexture(null);
                Assert.That(calls, Is.EqualTo(0),
                    "Disposed accessor must not forward host events");
            }
            finally
            {
                Object.DestroyImmediate(rt);
            }
        }
    }
}
