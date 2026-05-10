namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Tag describing the dynamic value type of a Volume Override parameter. The receiver
    /// MUST treat unknown numeric values as <see cref="Unknown"/> (skip + log) per
    /// Requirement 6.10, never as a hard error.
    /// </summary>
    public enum ParamKind
    {
        Bool = 0,
        Int = 1,
        Float = 2,
        ClampedFloat = 3,
        Color = 4,
        Vector2 = 5,
        Vector3 = 6,
        Vector4 = 7,
        Enum = 8,         // with EnumValues populated in VolumeOverrideParamRangeDto
        Unknown = 9       // forwarded unknown type (skipped in UI, Req 6.10)
    }
}
