namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.CamerasActive"/>
    /// (<c>cameras/active</c>, design.md L1267). Reports which camera the
    /// main-output side is currently rendering / publishing OSC for.
    /// </summary>
    /// <remarks>
    /// <see cref="ActiveCameraId"/> being null means no camera is active (e.g. the
    /// last camera was just deleted; the UI should fall back to a placeholder
    /// preview state).
    /// </remarks>
    public readonly struct CamerasActiveStatePayload
    {
        /// <summary>Currently active cameraId, or null if none.</summary>
        public string? ActiveCameraId { get; init; }

        /// <summary>Wall-clock millisecond timestamp when the active camera last changed.</summary>
        public long UpdatedAtUnixMs { get; init; }
    }
}
