namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.VolumeEnabled(string)"/>
    /// (<c>camera/{cameraId}/volume/enabled</c>, design.md L1272). Bidirectional
    /// state — either side may publish; coalesced (last-write-wins).
    /// </summary>
    public readonly struct VolumeEnabledStatePayload
    {
        /// <summary>Whether the entire Local Volume is enabled for this camera.</summary>
        public bool Enabled { get; init; }
    }
}
