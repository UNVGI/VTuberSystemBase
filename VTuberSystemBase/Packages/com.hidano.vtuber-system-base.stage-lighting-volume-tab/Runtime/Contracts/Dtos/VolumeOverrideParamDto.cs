namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Single Volume Override parameter descriptor inside <see cref="VolumeOverrideTypeDto"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Range"/> is null for unbounded numeric kinds and for kinds that have no
    /// natural range (Bool, Color, Vector*). For <see cref="ParamKind.Enum"/>, the enum
    /// candidates are carried via <see cref="VolumeOverrideParamRangeDto.EnumValues"/>.
    /// </remarks>
    public readonly record struct VolumeOverrideParamDto(
        string ParamName,
        ParamKind Kind,
        string DisplayName,
        VolumeOverrideParamValueDto DefaultValue,
        VolumeOverrideParamRangeDto? Range);
}
