#nullable enable
namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal
{
    /// <summary>
    /// Adapter-internal empty payload used as the request type for handlers that take no
    /// arguments (e.g. <c>volume/override/schema</c>). The shared Contracts assembly does
    /// not define an empty DTO, and inventing one there would force a contracts update for
    /// every consumer. A spec-local placeholder keeps the surface area in the adapter.
    /// </summary>
    public readonly record struct EmptyDto;
}
