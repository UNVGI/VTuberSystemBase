namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="StageLightingTopics.StageCurrent"/> and event payload
    /// for <see cref="StageLightingTopics.StageLoaded"/>. <see cref="AddressableKey"/> is
    /// null when no stage is currently loaded.
    /// </summary>
    public readonly record struct StageCurrentDto(string? AddressableKey);
}
