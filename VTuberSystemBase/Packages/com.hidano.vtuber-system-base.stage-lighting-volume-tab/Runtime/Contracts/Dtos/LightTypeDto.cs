namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Tag describing the underlying Unity light type carried in light DTOs.
    /// Forward-compatible: unknown numeric values seen on the wire MUST be treated as
    /// "skip + log" by the receiver, never as a hard error.
    /// </summary>
    public enum LightTypeDto
    {
        Directional = 0,
        Point = 1,
        Spot = 2,
        Area = 3
    }
}
