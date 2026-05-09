namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Single entry inside <see cref="StageCatalogDto"/>. <see cref="ThumbnailAddressableKey"/>
    /// is optional; the UI falls back to a placeholder when null.
    /// </summary>
    public readonly record struct StageCatalogEntryDto(
        string AddressableKey,
        string DisplayName,
        string? ThumbnailAddressableKey);
}
