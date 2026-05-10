#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Runtime
{
    [TestFixture]
    public sealed class CameraGameObjectFactoryTests
    {
        private GameObject? _camerasRoot;

        [SetUp]
        public void SetUp()
        {
            _camerasRoot = new GameObject("CamerasRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_camerasRoot != null) Object.Destroy(_camerasRoot);
            _camerasRoot = null;
        }

        [UnityTest]
        public IEnumerator Create_AppliesPhysicalPropertiesAndDefaultTransform()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var factory = new CameraGameObjectFactory(binder, new Vector2(36f, 24f));
            var defaults = new CameraDefaultTransform
            {
                Position = new[] { 1f, 2f, 3f },
                Rotation = new[] { 0f, 0f, 0f, 1f },
                FocalLengthMm = 50f,
            };
            var entry = factory.Create(_camerasRoot!.transform, new CameraId("cam-0001"),
                "Main", CameraType.Perspective, defaults, allocOrder: 1);
            try
            {
                Assert.That(entry.CameraComponent!.usePhysicalProperties, Is.True);
                Assert.That(entry.CameraComponent.focalLength, Is.EqualTo(50f).Within(1e-3f));
                Assert.That(entry.CameraComponent.sensorSize.x, Is.EqualTo(36f).Within(1e-3f));
                Assert.That(entry.CameraComponent.enabled, Is.False);
                Assert.That(entry.GameObject!.transform.parent, Is.SameAs(_camerasRoot.transform));
                Assert.That(entry.GameObject.transform.position, Is.EqualTo(new Vector3(1f, 2f, 3f)));

                Assert.That(entry.LocalVolume!.isGlobal, Is.True);
                Assert.That(entry.LocalVolume.enabled, Is.False);
                Assert.That(entry.LocalVolume.transform.parent, Is.SameAs(entry.GameObject.transform));
            }
            finally
            {
                factory.Destroy(entry);
            }
        }

        [UnityTest]
        public IEnumerator Destroy_RemovesGameObjectAndVolume()
        {
            yield return null;
            var binder = new GlobalEnabledLocalVolumeBinder();
            var factory = new CameraGameObjectFactory(binder, new Vector2(36f, 24f));
            var entry = factory.Create(_camerasRoot!.transform, new CameraId("cam-0002"),
                "Cam", CameraType.Perspective,
                new CameraDefaultTransform
                {
                    Position = new[] { 0f, 0f, 0f },
                    Rotation = new[] { 0f, 0f, 0f, 1f },
                    FocalLengthMm = 50f,
                }, allocOrder: 2);
            var go = entry.GameObject!;
            factory.Destroy(entry);
            yield return null;
            Assert.That(go == null, Is.True, "GameObject should be destroyed");
        }
    }
}
