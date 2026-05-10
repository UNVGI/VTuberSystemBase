#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Ucapi;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Adapters
{
    [TestFixture]
    public sealed class FlatRecordAddressDecoderTests
    {
        [Test]
        public void DecodeNormalAddress_ReturnsCameraId()
        {
            var cameraId = FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera/cam-0001/flat");
            Assert.That(cameraId, Is.EqualTo("cam-0001"));
        }

        [Test]
        public void MismatchedPrefix_ReturnsNull()
        {
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("/other/cam-0001/flat"), Is.Null);
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("ucapi/camera/cam-0001/flat"), Is.Null);
        }

        [Test]
        public void MissingFlatSuffix_ReturnsNull()
        {
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera/cam-0001"), Is.Null);
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera/cam-0001/data"), Is.Null);
        }

        [Test]
        public void DisallowedCameraIdCharacter_ReturnsNull()
        {
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera/cam 0001/flat"), Is.Null);
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera/cam.0001/flat"), Is.Null);
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera/cam/0001/flat"), Is.Null);
        }

        [Test]
        public void EmptyOrNullInput_ReturnsNull()
        {
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId(string.Empty), Is.Null);
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId(null), Is.Null);
        }

        [Test]
        public void EmptyCameraId_ReturnsNull()
        {
            Assert.That(FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera//flat"), Is.Null);
        }

        [Test]
        public void CustomPrefix_DecodesAccordingly()
        {
            var cameraId = FlatRecordAddressDecoder.TryDecodeCameraId("/x/y/cam-9999/flat", "/x/y");
            Assert.That(cameraId, Is.EqualTo("cam-9999"));
        }

        [Test]
        public void CameraId_AcceptsAllowedCharacters()
        {
            var cameraId = FlatRecordAddressDecoder.TryDecodeCameraId("/ucapi/camera/cam_abcXYZ-09/flat");
            Assert.That(cameraId, Is.EqualTo("cam_abcXYZ-09"));
        }
    }
}
