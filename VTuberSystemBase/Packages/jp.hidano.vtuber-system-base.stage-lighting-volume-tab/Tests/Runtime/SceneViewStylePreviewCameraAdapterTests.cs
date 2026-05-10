#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Preview;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the Locator-driven preview camera adapter behaviour
    /// (<see cref="SceneViewStylePreviewCameraAdapter"/>, Task 4.2, Requirements 2.4,
    /// 2.8, 2.10). Because <c>SceneViewStyleCameraController</c> exposes no public
    /// reset API, the adapter falls back to writing the camera Transform directly,
    /// re-using the "default view" snapshot captured at registration time.
    /// </summary>
    [TestFixture]
    public sealed class SceneViewStylePreviewCameraAdapterTests
    {
        [TearDown]
        public void TearDown()
        {
            var current = StagePreviewHostLocator.Current;
            if (current is not null) StagePreviewHostLocator.Unregister(current);
        }

        [Test]
        public void NoHost_IsAvailableFalse_AndResetIsNoOp()
        {
            var sut = new SceneViewStylePreviewCameraAdapter();
            Assert.That(sut.IsAvailable, Is.False);

            // Should not throw.
            sut.ResetToDefaultView();
        }

        [Test]
        public void HostWithCamera_IsAvailableTrue()
        {
            var go = new GameObject("camera-host-test");
            try
            {
                var camera = go.AddComponent<Camera>();
                var host = new FakePreviewHostService();
                host.SetCamera(camera);
                StagePreviewHostLocator.Register(host);

                var sut = new SceneViewStylePreviewCameraAdapter();
                Assert.That(sut.IsAvailable, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResetToDefaultView_RestoresCapturedTransform()
        {
            var go = new GameObject("camera-host-reset");
            try
            {
                var camera = go.AddComponent<Camera>();
                var t = camera.transform;
                t.position = new Vector3(0f, 1.5f, -3f);
                t.rotation = Quaternion.Euler(15f, 30f, 0f);
                var initialFov = 50f;
                camera.fieldOfView = initialFov;

                var host = new FakePreviewHostService();
                host.SetCamera(camera);
                StagePreviewHostLocator.Register(host);

                var sut = new SceneViewStylePreviewCameraAdapter();

                // Operator orbits the camera...
                t.position = new Vector3(20f, 5f, 7f);
                t.rotation = Quaternion.Euler(80f, -100f, 30f);
                camera.fieldOfView = 90f;

                sut.ResetToDefaultView();

                Assert.That(t.position, Is.EqualTo(new Vector3(0f, 1.5f, -3f)));
                Assert.That(t.rotation, Is.EqualTo(Quaternion.Euler(15f, 30f, 0f)));
                Assert.That(camera.fieldOfView, Is.EqualTo(initialFov));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HostUnregister_RaisesOnAvailabilityChanged_AndIsAvailableFlipsFalse()
        {
            var go = new GameObject("camera-host-availability");
            try
            {
                var camera = go.AddComponent<Camera>();
                var host = new FakePreviewHostService();
                host.SetCamera(camera);
                StagePreviewHostLocator.Register(host);

                var sut = new SceneViewStylePreviewCameraAdapter();
                int notified = 0;
                sut.OnAvailabilityChanged += () => notified++;
                Assert.That(sut.IsAvailable, Is.True);

                StagePreviewHostLocator.Unregister(host);

                // After Unregister IsAvailable must report false on next read.
                Assert.That(sut.IsAvailable, Is.False);
                Assert.That(notified, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
