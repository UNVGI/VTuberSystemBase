using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Single Volume Override type entry inside <see cref="VolumeOverrideSchemaDto"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TypeFullName"/> is the full reflected type name (e.g.
    /// <c>"UnityEngine.Rendering.Universal.Bloom"</c>) and is the stable key for routing
    /// per-type topics via <see cref="StageLightingTopics.VolumeOverrideEnabled"/> and
    /// <see cref="StageLightingTopics.VolumeOverrideParam"/>.
    /// </remarks>
    public readonly record struct VolumeOverrideTypeDto(
        string TypeFullName,
        string DisplayName,
        IReadOnlyList<VolumeOverrideParamDto> Params);
}
