#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class DefaultCameraFallbackControllerTests
    {
        [UnityTest]
        public IEnumerator FirstCameraAdded_DisablesDefaultCamera()
        {
            yield return null;
            var go = new GameObject("[default]");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = true;
                var controller = new DefaultCameraFallbackController(camera);
                Assert.That(controller.IsFallbackActive, Is.True);

                controller.NotifyCameraCountChanged(1);
                Assert.That(camera.enabled, Is.False);
                Assert.That(controller.IsFallbackActive, Is.False);

                controller.NotifyCameraCountChanged(0);
                Assert.That(camera.enabled, Is.True);
                Assert.That(controller.IsFallbackActive, Is.True);
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        [UnityTest]
        public IEnumerator RestoreFallback_AlwaysReenablesDefaultCamera()
        {
            yield return null;
            var go = new GameObject("[default]");
            try
            {
                var camera = go.AddComponent<Camera>();
                var controller = new DefaultCameraFallbackController(camera);
                controller.NotifyCameraCountChanged(3);
                Assert.That(camera.enabled, Is.False);

                controller.RestoreFallback();
                Assert.That(camera.enabled, Is.True);
                Assert.That(controller.IsFallbackActive, Is.True);
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        [Test]
        public void NullDefaultCamera_IsSafeNoOp()
        {
            var controller = new DefaultCameraFallbackController(null);
            controller.NotifyCameraCountChanged(2);
            controller.NotifyCameraCountChanged(0);
            controller.RestoreFallback();
            Assert.Pass();
        }
    }
}
