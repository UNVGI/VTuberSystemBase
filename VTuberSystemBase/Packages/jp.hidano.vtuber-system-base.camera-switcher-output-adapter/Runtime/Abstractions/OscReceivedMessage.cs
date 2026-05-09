#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// One OSC message dispatched by <see cref="IOscReceiverHost.MessageReceived"/> after the
    /// adapter has parsed the address and validated that the blob argument is present.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CameraId"/> carries the cameraId segment extracted from the
    /// <c>/ucapi/camera/{cameraId}/flat</c> address. The string is guaranteed to satisfy
    /// the <c>OscAddressBuilder.IsValidCameraIdSegment</c> predicate (ASCII alphanumerics
    /// + <c>-</c>, <c>_</c>); the host MUST drop messages whose cameraId segment fails
    /// the predicate before raising <see cref="IOscReceiverHost.MessageReceived"/>.
    /// </para>
    /// <para>
    /// <see cref="Blob"/> is the raw UCAPI Flat Record bytes obtained directly from
    /// <c>uOSC.Message.values[0]</c>. Per CSO-4 the array reference is reused without
    /// copying — receivers MUST NOT retain the reference past the synchronous handler
    /// call (uOSC may reuse the buffer for the next message).
    /// </para>
    /// </remarks>
    public readonly struct OscReceivedMessage
    {
        public OscReceivedMessage(string cameraId, byte[] blob)
        {
            if (cameraId == null) throw new ArgumentNullException(nameof(cameraId));
            if (cameraId.Length == 0) throw new ArgumentException("cameraId must not be empty.", nameof(cameraId));
            if (blob == null) throw new ArgumentNullException(nameof(blob));
            CameraId = cameraId;
            Blob = blob;
        }

        /// <summary>cameraId extracted from the OSC address segment.</summary>
        public string CameraId { get; }

        /// <summary>UCAPI Flat Record bytes (no copy; lifetime bounded by the synchronous handler call).</summary>
        public byte[] Blob { get; }
    }
}
