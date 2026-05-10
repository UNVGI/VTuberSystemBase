namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for
    /// <see cref="CameraIpcTopics.VolumeOverrideEnabled(string,string)"/>
    /// (<c>camera/{cameraId}/volume/override/{type}/enabled</c>, design.md L1273).
    /// </summary>
    public readonly struct VolumeOverrideEnabledStatePayload
    {
        /// <summary>Whether this individual override block is enabled.</summary>
        public bool Enabled { get; init; }
    }
}
