namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Spawn-time transform for a camera (design.md L1323-L1328). Pure data, no Unity
    /// types — the receiver converts to <c>Vector3</c>/<c>Quaternion</c> at the boundary.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Position"/> is a 3-element array <c>[x, y, z]</c>.</para>
    /// <para><see cref="Rotation"/> is a 4-element quaternion <c>[x, y, z, w]</c>.</para>
    /// <para><see cref="FocalLengthMm"/> is the URP physical-camera focal length in mm
    /// (only meaningful for <see cref="CameraType.Perspective"/>).</para>
    /// <para>Receivers MUST treat arrays of unexpected length as a forward-compatible
    /// "skip + log" condition rather than a hard error (design.md L1372-L1380).</para>
    /// </remarks>
    public readonly struct CameraDefaultTransform
    {
        /// <summary><c>[x, y, z]</c> in world units.</summary>
        public float[] Position { get; init; }

        /// <summary>Quaternion <c>[x, y, z, w]</c>.</summary>
        public float[] Rotation { get; init; }

        /// <summary>Physical-camera focal length, in millimetres.</summary>
        public float FocalLengthMm { get; init; }
    }
}
