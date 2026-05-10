namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="StageLightingTopics.StageCommand"/>. Op is either
    /// <c>"load"</c> (with <see cref="AddressableKey"/> populated) or <c>"unload"</c>.
    /// </summary>
    public readonly record struct StageCommandDto(
        string Op,                // "load" or "unload"
        string? AddressableKey);  // required for "load"
}
