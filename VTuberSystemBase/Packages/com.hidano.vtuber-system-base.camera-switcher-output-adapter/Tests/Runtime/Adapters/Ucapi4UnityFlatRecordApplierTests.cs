#nullable enable
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Ucapi;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Adapters
{
    [TestFixture]
    public sealed class Ucapi4UnityFlatRecordApplierTests
    {
        private GameObject? _cameraGo;
        private Camera? _camera;

        [SetUp]
        public void SetUp()
        {
            _cameraGo = new GameObject("[Ucapi4UnityFlatRecordApplierTests]");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.usePhysicalProperties = true;
            _camera.sensorSize = new Vector2(36f, 24f);
            _camera.focalLength = 50f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_cameraGo != null) UnityEngine.Object.Destroy(_cameraGo);
            _cameraGo = null;
            _camera = null;
        }

        [UnityTest]
        public IEnumerator Apply_ValidBlob_RoundTripsCameraTransform()
        {
            yield return null; // PlayMode warm-up frame.

            var expectedPosition = new Vector3(1.5f, 2f, -4.5f);
            var expectedEuler = new Vector3(10f, 20f, 0f);
            const float expectedFocal = 35f;

            var blob = UcapiFlatRecordTestFactory.CreateBlob(expectedPosition, expectedEuler, expectedFocal);
            var applier = new Ucapi4UnityFlatRecordApplier();

            var success = applier.Apply(new CameraId("cam-0001"), blob, _camera!);

            Assert.That(success, Is.True);
            Assert.That(_camera!.transform.position.x, Is.EqualTo(expectedPosition.x).Within(1e-3f));
            Assert.That(_camera.transform.position.y, Is.EqualTo(expectedPosition.y).Within(1e-3f));
            Assert.That(_camera.transform.position.z, Is.EqualTo(expectedPosition.z).Within(1e-3f));

            var actualEuler = _camera.transform.rotation.eulerAngles;
            // Quaternions can be returned in different but equivalent forms; compare via dot product.
            var expectedQuat = Quaternion.Euler(expectedEuler);
            var actualQuat = _camera.transform.rotation;
            Assert.That(Mathf.Abs(Quaternion.Dot(expectedQuat, actualQuat)), Is.GreaterThan(1f - 1e-3f),
                $"Expected rotation~{expectedEuler}, got {actualEuler}");

            Assert.That(_camera.focalLength, Is.EqualTo(expectedFocal).Within(1e-2f));
            Assert.That(_camera.sensorSize.x, Is.EqualTo(36f).Within(1e-3f));
            Assert.That(_camera.sensorSize.y, Is.EqualTo(24f).Within(1e-3f));
        }

        [Test]
        public void Apply_InvalidBlob_TriggersFailureCallback()
        {
            CameraId? capturedId = null;
            Exception? capturedEx = null;
            var applier = new Ucapi4UnityFlatRecordApplier(onDecodeFailure: (id, ex) =>
            {
                capturedId = id;
                capturedEx = ex;
            });

            var bogus = new byte[16];
            var success = applier.Apply(new CameraId("cam-0042"), bogus, _camera!);

            Assert.That(success, Is.False);
            Assert.That(capturedId.HasValue, Is.True);
            Assert.That(capturedId!.Value.Value, Is.EqualTo("cam-0042"));
            Assert.That(capturedEx, Is.Not.Null);
        }

        [Test]
        public void Apply_NullCamera_ReturnsFalseWithoutCallback()
        {
            var fired = false;
            var applier = new Ucapi4UnityFlatRecordApplier(onDecodeFailure: (_, _) => fired = true);
            var success = applier.Apply(new CameraId("cam-0042"), new byte[] { 1, 2, 3 }, null!);
            Assert.That(success, Is.False);
            Assert.That(fired, Is.False);
        }

        [Test]
        public void Apply_EmptyBlob_ReturnsFalseWithoutCallback()
        {
            var fired = false;
            var applier = new Ucapi4UnityFlatRecordApplier(onDecodeFailure: (_, _) => fired = true);
            var success = applier.Apply(new CameraId("cam-0042"), Array.Empty<byte>(), _camera!);
            Assert.That(success, Is.False);
            Assert.That(fired, Is.False);
        }
    }
}
