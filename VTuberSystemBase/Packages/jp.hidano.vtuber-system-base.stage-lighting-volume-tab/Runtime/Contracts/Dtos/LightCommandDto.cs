namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="StageLightingTopics.LightCommand"/>. Carries either an
    /// "add" command (with <see cref="Initial"/> populated) or a "remove" command (with
    /// <see cref="LightId"/> populated).
    /// </summary>
    public readonly record struct LightCommandDto(
        string Op,                // "add" or "remove"
        string? LightId,          // required for "remove"
        LightInitialDto? Initial);
}
