namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="CameraIpcTopics.VolumeCommand(string)"/>
    /// (<c>camera/{cameraId}/volume/command</c>, design.md L1271). UI requests
    /// the main-output side to add or remove a Local Volume override.
    /// </summary>
    public readonly struct VolumeCommandPayload
    {
        /// <summary>One of <c>override-add</c> / <c>override-remove</c>.</summary>
        public string Op { get; init; }

        /// <summary>The override type name (e.g. <c>Bloom</c>, <c>Tonemapping</c>).</summary>
        public string OverrideType { get; init; }
    }

    /// <summary>String constants for <see cref="VolumeCommandPayload.Op"/>.</summary>
    public static class VolumeCommandOps
    {
        public const string OverrideAdd = "override-add";
        public const string OverrideRemove = "override-remove";
    }
}
