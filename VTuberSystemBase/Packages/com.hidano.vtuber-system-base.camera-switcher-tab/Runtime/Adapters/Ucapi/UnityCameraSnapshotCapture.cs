#nullable enable
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
namespace VTuberSystemBase.CameraSwitcherTab.Adapters.Ucapi
{
    /// <summary>
    /// Captures a <see cref="CameraSnapshot"/> from a <see cref="UnityEngine.Camera"/>.
    /// The capture is the only place that touches the engine type, keeping the
    /// serializer engine-free and unit-testable (Requirement 3.1, 3.7).
    /// </summary>
    /// <remarks>
    /// Reads <c>transform.position</c> / <c>transform.rotation</c> / physical-camera
    /// properties (focalLength, sensorSize, focus distance, aperture) plus the
    /// near / far clip planes. The caller is responsible for setting
    /// <see cref="CameraSnapshot.CameraId"/>, <see cref="CameraSnapshot.CameraType"/>
    /// and <see cref="CameraSnapshot.FrameCounter"/>.
    /// </remarks>
    public static class UnityCameraSnapshotCapture
    {
        public static CameraSnapshot Capture(
            Camera camera,
            CameraId cameraId,
            CameraType cameraType,
            uint frameCounter)
        {
            if (camera == null) throw new System.ArgumentNullException(nameof(camera));
            var t = camera.transform;
            var p = t.position;
            var r = t.rotation;
            return new CameraSnapshot
            {
                CameraId = cameraId,
                CameraType = cameraType,
                PositionX = p.x,
                PositionY = p.y,
                PositionZ = p.z,
                RotationX = r.x,
                RotationY = r.y,
                RotationZ = r.z,
                RotationW = r.w,
                FocalLengthMm = camera.focalLength,
                SensorWidthMm = camera.sensorSize.x,
                SensorHeightMm = camera.sensorSize.y,
                NearClipM = camera.nearClipPlane,
                FarClipM = camera.farClipPlane,
                Aperture = camera.aperture,
                FocusDistanceM = camera.focusDistance,
                FrameCounter = frameCounter,
            };
        }
    }
}
