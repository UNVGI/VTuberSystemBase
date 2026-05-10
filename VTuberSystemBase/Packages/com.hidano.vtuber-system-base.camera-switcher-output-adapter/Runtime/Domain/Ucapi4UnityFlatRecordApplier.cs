#nullable enable
using System;
using UCAPI4Unity.Runtime.UnityCamera;
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Ucapi
{
    /// <summary>
    /// Thin wrapper around <see cref="UcApi4UnityCamera.ApplyToCamera(byte[], Camera)"/>
    /// that converts UCAPI / DLL exceptions into a structured failure callback so the
    /// OSC receive loop can keep running (Requirement 2.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per CSO-4 the <c>byte[]</c> is forwarded to the UCAPI API by reference; no
    /// extra copy is performed at this layer. Per CSO-15 the UCAPI <c>CameraNo</c>
    /// field is ignored — the cameraId carried in the OSC address is the source of
    /// truth for camera multiplexing.
    /// </para>
    /// <para>
    /// The class itself is allocation-free on the success path and stateless; the
    /// failure callback is invoked on the calling thread (Unity main).
    /// </para>
    /// </remarks>
    public sealed class Ucapi4UnityFlatRecordApplier
    {
        private readonly Action<CameraId, Exception>? _onDecodeFailure;

        public Ucapi4UnityFlatRecordApplier(Action<CameraId, Exception>? onDecodeFailure = null)
        {
            _onDecodeFailure = onDecodeFailure;
        }

        /// <summary>
        /// Applies <paramref name="blob"/> to <paramref name="camera"/>. On a UCAPI
        /// decode failure (CRC, DLL missing, parse error) the exception is captured
        /// and forwarded to the configured callback; the camera's previous values
        /// are left intact and the caller is expected to drop the frame.
        /// </summary>
        /// <returns><c>true</c> on success; <c>false</c> when an exception was caught.</returns>
        public bool Apply(CameraId cameraId, byte[] blob, Camera camera)
        {
            if (camera == null) return false;
            if (blob == null || blob.Length == 0) return false;

            try
            {
                UcApi4UnityCamera.ApplyToCamera(blob, camera);
                return true;
            }
            catch (Exception ex)
            {
                _onDecodeFailure?.Invoke(cameraId, ex);
                return false;
            }
        }
    }
}
