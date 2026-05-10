using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="StageLightingTopics.StageCatalog"/>. Lists every
    /// stage prefab discovered (typically via Addressables) on the main-output side.
    /// </summary>
    public readonly record struct StageCatalogDto(IReadOnlyList<StageCatalogEntryDto> Items);
}
