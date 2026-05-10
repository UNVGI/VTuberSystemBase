using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Response payload for the <see cref="StageLightingTopics.VolumeOverrideSchema"/>
    /// request. Describes every Volume Override type the main-output side knows about,
    /// together with their tunable parameters.
    /// </summary>
    /// <remarks>
    /// <see cref="SchemaVersion"/> = 1 for the initial release. Forward compatibility:
    /// unknown fields are ignored (core-ipc-foundation D-7).
    /// </remarks>
    public readonly record struct VolumeOverrideSchemaDto(
        int SchemaVersion,
        IReadOnlyList<VolumeOverrideTypeDto> Types);
}
