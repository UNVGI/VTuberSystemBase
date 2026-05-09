namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="StageLightingTopics.LightAdded"/>. Echoes the resolved
    /// <see cref="LightId"/> assigned by the main-output-side adapter together with the
    /// initial values that were applied (SL-3).
    /// </summary>
    public readonly record struct LightAddedDto(
        string LightId,
        LightInitialDto Initial);
}
