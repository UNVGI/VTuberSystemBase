namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Initial values supplied when a new light is added. Echoed back inside
    /// <see cref="LightAddedDto"/> after the main-output side allocates a lightId.
    /// </summary>
    /// <remarks>
    /// Preconditions enforced by the UI side (see Requirement 5.7):
    /// <list type="bullet">
    ///   <item><description><c>Intensity &gt;= 0</c>, <c>Range &gt;= 0</c></description></item>
    ///   <item><description><c>SpotAngle</c> in <c>[1, 179]</c></description></item>
    ///   <item><description><c>DisplayName</c> must not be whitespace-only</description></item>
    /// </list>
    /// </remarks>
    public readonly record struct LightInitialDto(
        LightTypeDto Type,
        Vector3Dto Rotation,
        ColorDto Color,
        float Intensity,
        float Range,
        float SpotAngle,
        string DisplayName);
}
