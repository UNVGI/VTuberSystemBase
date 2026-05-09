namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.PresetActive"/>
    /// (<c>camera/preset/active</c>, design.md L1279). Self-published by the UI;
    /// reflects the currently-active preset name, or null if none is active.
    /// </summary>
    public readonly struct PresetActiveStatePayload
    {
        /// <summary>Currently active preset name, or null if no preset is active.</summary>
        public string? ActiveName { get; init; }
    }
}
