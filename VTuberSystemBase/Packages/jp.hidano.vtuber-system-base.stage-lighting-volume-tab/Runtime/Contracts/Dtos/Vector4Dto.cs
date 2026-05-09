namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Wire-format 4D vector. Used both as Vector4 itself and as a packed carrier for
    /// Vector2/Vector3 inside <see cref="VolumeOverrideParamValueDto"/>.
    /// </summary>
    public readonly record struct Vector4Dto(float X, float Y, float Z, float W);
}
