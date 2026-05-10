#nullable enable
using System;
using UCAPI4Unity.Runtime.Core;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Adapters.Ucapi
{
    /// <summary>
    /// Default <see cref="IUcapiFlatRecordSerializer"/> implementation that wraps
    /// UCAPI4Unity's <c>UcApiCore.SerializeFromRecord</c>. Performs per-field
    /// sanitisation (NaN / Inf, focal length &lt;= 0, sensor &lt;= 0, near &gt;= far)
    /// and returns a structured <see cref="SerializeResult.Invalid"/> on any
    /// violation; never throws (Requirement 3.4 / 3.5 / 3.8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// CRC16-CCITT and the on-wire byte layout are produced by the UCAPI native
    /// DLL — this adapter only marshals <see cref="CameraSnapshot"/> field values
    /// into <see cref="UcApiRecord"/> with conservative unit conversion. UCAPI
    /// version updates can be absorbed by editing this file alone (Requirement 3.7).
    /// </para>
    /// <para>
    /// Quaternion → forward / up vector conversion uses the standard
    /// <c>q * (0,0,1)</c> / <c>q * (0,1,0)</c> rotation, hand-rolled so the adapter
    /// stays free of a UnityEngine dependency (only UCAPI4Unity types are
    /// referenced, which is engine-free at the boundary we hit).
    /// </para>
    /// </remarks>
    public sealed class Ucapi4UnityFlatRecordSerializer : IUcapiFlatRecordSerializer
    {
        private const ushort DefaultCommands = 0x000B; // mirrors UcApiRecordParser.FromCamera
        private const byte DefaultPacketNo = 1;
        private const uint DefaultCameraNo = 1;

        public SerializeResult Serialize(in CameraSnapshot snapshot)
        {
            // ---- Sanitization (Requirement 3.4) ----

            if (!IsFinite(snapshot.PositionX) || !IsFinite(snapshot.PositionY) || !IsFinite(snapshot.PositionZ))
                return SerializeResult.Invalid(SerializeFailureReason.InvalidPosition, "Position has NaN/Inf");

            if (!IsFinite(snapshot.RotationX) || !IsFinite(snapshot.RotationY)
                || !IsFinite(snapshot.RotationZ) || !IsFinite(snapshot.RotationW))
                return SerializeResult.Invalid(SerializeFailureReason.InvalidRotation, "Rotation has NaN/Inf");

            // Reject zero-quaternion (degenerate); a unit quaternion has length ~1.
            var qLenSq = snapshot.RotationX * snapshot.RotationX
                       + snapshot.RotationY * snapshot.RotationY
                       + snapshot.RotationZ * snapshot.RotationZ
                       + snapshot.RotationW * snapshot.RotationW;
            if (qLenSq <= 1e-12f)
                return SerializeResult.Invalid(SerializeFailureReason.InvalidRotation, "Rotation is zero quaternion");

            if (!IsFinite(snapshot.FocalLengthMm) || snapshot.FocalLengthMm <= 0f)
                return SerializeResult.Invalid(SerializeFailureReason.InvalidFocalLength,
                    $"focalLength={snapshot.FocalLengthMm}");

            if (!IsFinite(snapshot.SensorWidthMm) || !IsFinite(snapshot.SensorHeightMm)
                || snapshot.SensorWidthMm <= 0f || snapshot.SensorHeightMm <= 0f)
                return SerializeResult.Invalid(SerializeFailureReason.InvalidSensorSize,
                    $"sensor=({snapshot.SensorWidthMm},{snapshot.SensorHeightMm})");

            if (!IsFinite(snapshot.NearClipM) || !IsFinite(snapshot.FarClipM)
                || snapshot.NearClipM <= 0f || snapshot.FarClipM <= snapshot.NearClipM)
                return SerializeResult.Invalid(SerializeFailureReason.InvalidClipPlanes,
                    $"clip=(near={snapshot.NearClipM}, far={snapshot.FarClipM})");

            if (!snapshot.CameraId.HasValue)
                return SerializeResult.Invalid(SerializeFailureReason.InvalidCameraId, "CameraId is unset");

            // ---- Build UCAPI record ----

            // Quaternion → forward / up vectors. q * (0,0,1) and q * (0,1,0).
            // Hand-rolled to avoid UnityEngine dependency in this adapter.
            var (fwdX, fwdY, fwdZ) = RotateForward(snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ, snapshot.RotationW);
            var (upX, upY, upZ) = RotateUp(snapshot.RotationX, snapshot.RotationY, snapshot.RotationZ, snapshot.RotationW);

            var aspect = snapshot.SensorWidthMm > 0f
                ? snapshot.SensorWidthMm / snapshot.SensorHeightMm
                : 1f;
            var focus = IsFinite(snapshot.FocusDistanceM) && snapshot.FocusDistanceM > 0f ? snapshot.FocusDistanceM : 1f;
            var aperture = IsFinite(snapshot.Aperture) && snapshot.Aperture > 0f ? snapshot.Aperture : 5.6f;

            var timeCode = new UcApiTimeCode
            {
                FrameNumber = (uint)(snapshot.FrameCounter & 0xFF),
                Second = 0u,
                Minute = 0u,
                Hour = 0u,
                FrameRate = FrameRate.FrameRate60,
                DropFrame = false,
            };

            var record = new UcApiRecord
            {
                CameraNo = DefaultCameraNo,
                Commands = DefaultCommands,
                PacketNo = DefaultPacketNo,
                TimeCode = UcApiTimeCode.ToRaw(timeCode),
                SubFrame = 0f,
                EyePositionRightM = snapshot.PositionX,
                EyePositionUpM = snapshot.PositionY,
                EyePositionForwardM = snapshot.PositionZ,
                LookVectorRightM = fwdX,
                LookVectorUpM = fwdY,
                LookVectorForwardM = fwdZ,
                UpVectorRightM = upX,
                UpVectorUpM = upY,
                UpVectorForwardM = upZ,
                FocalLengthMm = snapshot.FocalLengthMm,
                AspectRatio = aspect,
                FocusDistanceM = focus,
                Aperture = aperture,
                SensorSizeWidthMm = snapshot.SensorWidthMm,
                SensorSizeHeightMm = snapshot.SensorHeightMm,
                NearClipM = snapshot.NearClipM,
                FarClipM = snapshot.FarClipM,
                LensShiftHorizontalRatio = 0f,
                LensShiftVerticalRatio = 0f,
                LensDistortionRadialCoefficientsK1 = 0f,
                LensDistortionRadialCoefficientsK2 = 0f,
                LensDistortionCenterPointRightMm = 0f,
                LensDistortionCenterPointUpMm = 0f,
            };

            byte[] blob;
            try
            {
                blob = UcApiCore.SerializeFromRecord(record);
            }
            catch (Exception ex)
            {
                return SerializeResult.Invalid(SerializeFailureReason.AdapterFault, ex.Message);
            }
            if (blob == null || blob.Length == 0)
                return SerializeResult.Invalid(SerializeFailureReason.AdapterFault, "UCAPI returned empty blob");

            return SerializeResult.Ok(UcapiFlatRecord.FromBytes(blob));
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // q * (0,0,1)
        private static (float x, float y, float z) RotateForward(float qx, float qy, float qz, float qw)
        {
            // Rotation matrix column for (0,0,1):
            //   [ 2(xz + wy),  2(yz - wx),  1 - 2(x^2 + y^2) ]
            return (
                2f * (qx * qz + qw * qy),
                2f * (qy * qz - qw * qx),
                1f - 2f * (qx * qx + qy * qy));
        }

        // q * (0,1,0)
        private static (float x, float y, float z) RotateUp(float qx, float qy, float qz, float qw)
        {
            // Rotation matrix column for (0,1,0):
            //   [ 2(xy - wz),  1 - 2(x^2 + z^2),  2(yz + wx) ]
            return (
                2f * (qx * qy - qw * qz),
                1f - 2f * (qx * qx + qz * qz),
                2f * (qy * qz + qw * qx));
        }
    }
}
