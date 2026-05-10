namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="StageLightingTopics.LightError"/>. Reported by the
    /// main-output-side adapter when an add/remove/property-write request fails.
    /// </summary>
    /// <remarks>
    /// <see cref="LightId"/> is null when the failure happened before a lightId was
    /// assigned (e.g., add path with limit_exceeded). <see cref="ErrorCode"/> is one of
    /// <c>"limit_exceeded"</c>, <c>"internal_error"</c>, <c>"not_found"</c>, etc.
    /// </remarks>
    public readonly record struct LightErrorDto(
        string? LightId,
        string CorrelationId,
        string ErrorCode,
        string Message);
}
