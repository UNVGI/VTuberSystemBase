namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="StageLightingTopics.PreviewCommand"/>. Op is one of
    /// <c>"set-enabled"</c> (with <see cref="Enabled"/> populated), <c>"reset-view"</c>,
    /// <c>"init"</c>, <c>"dispose"</c>.
    /// </summary>
    public readonly record struct PreviewCommandDto(
        string Op,
        bool? Enabled);
}
