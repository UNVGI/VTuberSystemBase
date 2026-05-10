namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="StageLightingTopics.StageLoadFailed"/>. ErrorCode is
    /// one of <c>"not_found"</c>, <c>"load_failed"</c>, <c>"instantiate_failed"</c>.
    /// </summary>
    public readonly record struct StageLoadFailedDto(
        string AddressableKey,
        string ErrorCode,
        string Message);
}
