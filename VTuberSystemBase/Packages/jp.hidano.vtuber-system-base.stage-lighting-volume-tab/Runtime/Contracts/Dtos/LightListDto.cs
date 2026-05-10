using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="StageLightingTopics.LightsList"/>. Lists every light
    /// currently driven by the main-output-side adapter in display order.
    /// </summary>
    public readonly record struct LightListDto(IReadOnlyList<LightListItemDto> Items);

    /// <summary>
    /// Single entry inside <see cref="LightListDto"/>. Carries only the identity columns
    /// needed to render the list; per-light property values arrive on dynamic topics
    /// (<see cref="StageLightingTopics.LightProperty"/>).
    /// </summary>
    public readonly record struct LightListItemDto(
        string LightId,
        string DisplayName,
        LightTypeDto Type);
}
