namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="StageLightingTopics.PreviewState"/>. <see cref="HostReady"/>
    /// stays false while the main-output-side <c>StagePreviewHost</c> is still initializing
    /// (<c>Awake</c> not yet completed), so the UI should defer RenderTexture binding
    /// until it flips to true.
    /// </summary>
    public readonly record struct PreviewStateDto(
        bool Enabled,
        bool HostReady);
}
