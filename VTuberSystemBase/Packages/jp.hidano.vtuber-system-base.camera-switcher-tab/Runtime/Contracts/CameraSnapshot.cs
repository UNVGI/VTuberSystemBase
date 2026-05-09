#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Pure-value snapshot of a Unity Camera at one frame, used as the input to
    /// <c>IUcapiFlatRecordSerializer</c>. Intentionally engine-agnostic so it can
    /// be constructed by tests without instantiating a <c>UnityEngine.Camera</c>.
    /// </summary>
    /// <remarks>
    /// Position is given in metres (Unity world units) as <c>[x, y, z]</c>.
    /// Rotation is a unit quaternion <c>[x, y, z, w]</c>. <see cref="FocalLengthMm"/>
    /// follows URP's physical-camera convention, <see cref="SensorSizeMm"/> is
    /// <c>[width, height]</c> in mm. NaN / Inf and non-positive sensor / focal
    /// values are treated as serialisation failures by the adapter (Requirement 3.4).
    /// </remarks>
    public readonly struct CameraSnapshot : IEquatable<CameraSnapshot>
    {
        public CameraId CameraId { get; init; }

        public CameraType CameraType { get; init; }

        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public float PositionZ { get; init; }

        public float RotationX { get; init; }
        public float RotationY { get; init; }
        public float RotationZ { get; init; }
        public float RotationW { get; init; }

        public float FocalLengthMm { get; init; }
        public float SensorWidthMm { get; init; }
        public float SensorHeightMm { get; init; }
        public float NearClipM { get; init; }
        public float FarClipM { get; init; }
        public float Aperture { get; init; }
        public float FocusDistanceM { get; init; }

        /// <summary>Frame counter at capture time (LateUpdate frame index).</summary>
        public uint FrameCounter { get; init; }

        public bool Equals(CameraSnapshot other)
        {
            return CameraId.Equals(other.CameraId)
                   && CameraType == other.CameraType
                   && PositionX.Equals(other.PositionX)
                   && PositionY.Equals(other.PositionY)
                   && PositionZ.Equals(other.PositionZ)
                   && RotationX.Equals(other.RotationX)
                   && RotationY.Equals(other.RotationY)
                   && RotationZ.Equals(other.RotationZ)
                   && RotationW.Equals(other.RotationW)
                   && FocalLengthMm.Equals(other.FocalLengthMm)
                   && SensorWidthMm.Equals(other.SensorWidthMm)
                   && SensorHeightMm.Equals(other.SensorHeightMm)
                   && NearClipM.Equals(other.NearClipM)
                   && FarClipM.Equals(other.FarClipM)
                   && Aperture.Equals(other.Aperture)
                   && FocusDistanceM.Equals(other.FocusDistanceM)
                   && FrameCounter == other.FrameCounter;
        }

        public override bool Equals(object? obj) => obj is CameraSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add(CameraId);
            h.Add((int)CameraType);
            h.Add(PositionX);
            h.Add(PositionY);
            h.Add(PositionZ);
            h.Add(RotationX);
            h.Add(RotationY);
            h.Add(RotationZ);
            h.Add(RotationW);
            h.Add(FocalLengthMm);
            h.Add(SensorWidthMm);
            h.Add(SensorHeightMm);
            h.Add(NearClipM);
            h.Add(FarClipM);
            h.Add(Aperture);
            h.Add(FocusDistanceM);
            h.Add(FrameCounter);
            return h.ToHashCode();
        }
    }
}
