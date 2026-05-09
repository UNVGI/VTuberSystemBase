#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Ucapi;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Sanitisation contract for <see cref="Ucapi4UnityFlatRecordSerializer"/>.
    /// We pin the engine-free early-return paths (NaN/Inf, focal &lt;= 0,
    /// sensor &lt;= 0, inverted clip planes, unset CameraId) so the serializer
    /// never feeds garbage into the UCAPI native DLL (Requirement 3.4 / 3.5 / 3.8).
    /// The happy-path serialisation depends on the UCAPI native library which is
    /// covered by the OSC loopback integration tests in §4.2.
    /// </summary>
    [TestFixture]
    public sealed class Ucapi4UnityFlatRecordSerializerTests
    {
        private static CameraSnapshot ValidSnapshot()
        {
            return new CameraSnapshot
            {
                CameraId = new CameraId("cam-1"),
                CameraType = CameraType.Perspective,
                PositionX = 0f, PositionY = 0f, PositionZ = 0f,
                RotationX = 0f, RotationY = 0f, RotationZ = 0f, RotationW = 1f,
                FocalLengthMm = 50f,
                SensorWidthMm = 36f,
                SensorHeightMm = 24f,
                NearClipM = 0.1f,
                FarClipM = 1000f,
                Aperture = 5.6f,
                FocusDistanceM = 1f,
                FrameCounter = 42u,
            };
        }

        [Test]
        public void Serialize_RejectsNaNPosition()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { PositionX = float.NaN };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidPosition, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsInfPosition()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { PositionZ = float.PositiveInfinity };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidPosition, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsNaNRotation()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { RotationW = float.NaN };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidRotation, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsZeroQuaternion()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { RotationX = 0f, RotationY = 0f, RotationZ = 0f, RotationW = 0f };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidRotation, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsZeroFocalLength()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { FocalLengthMm = 0f };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidFocalLength, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsNegativeFocalLength()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { FocalLengthMm = -50f };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidFocalLength, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsZeroSensor()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { SensorWidthMm = 0f };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidSensorSize, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsInvertedClipPlanes()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { NearClipM = 100f, FarClipM = 10f };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidClipPlanes, result.FailureReason);
        }

        [Test]
        public void Serialize_RejectsUnsetCameraId()
        {
            var sut = new Ucapi4UnityFlatRecordSerializer();
            var snap = ValidSnapshot();
            snap = snap with { CameraId = default };
            var result = sut.Serialize(snap);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SerializeFailureReason.InvalidCameraId, result.FailureReason);
        }

        [Test]
        public void Serialize_DoesNotThrow_OnAllFailureBranches()
        {
            // Combined: every sanitisation path must return Invalid, never throw.
            var sut = new Ucapi4UnityFlatRecordSerializer();
            Assert.DoesNotThrow(() =>
            {
                sut.Serialize(ValidSnapshot() with { PositionX = float.NaN });
                sut.Serialize(ValidSnapshot() with { RotationW = float.NaN });
                sut.Serialize(ValidSnapshot() with { FocalLengthMm = -1f });
                sut.Serialize(ValidSnapshot() with { SensorHeightMm = 0f });
                sut.Serialize(ValidSnapshot() with { NearClipM = 0f });
                sut.Serialize(ValidSnapshot() with { CameraId = default });
            });
        }
    }
}
