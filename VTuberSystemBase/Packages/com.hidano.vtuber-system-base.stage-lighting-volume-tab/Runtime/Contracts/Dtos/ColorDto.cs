namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Wire-format RGBA color (linear, 0..1 nominal range; HDR may exceed 1). Avoids
    /// depending on <c>UnityEngine.Color</c> in Contracts.
    /// </summary>
    public readonly record struct ColorDto(float R, float G, float B, float A);
}
