namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Discriminated-union wire format for a Volume Override parameter value. Exactly one
    /// of the optional payload fields is populated according to <see cref="Kind"/>:
    /// <list type="bullet">
    ///   <item><description><see cref="ParamKind.Bool"/> -> <see cref="BoolValue"/></description></item>
    ///   <item><description><see cref="ParamKind.Int"/> -> <see cref="IntValue"/></description></item>
    ///   <item><description><see cref="ParamKind.Float"/> / <see cref="ParamKind.ClampedFloat"/> -> <see cref="FloatValue"/></description></item>
    ///   <item><description><see cref="ParamKind.Color"/> -> <see cref="ColorValue"/></description></item>
    ///   <item><description><see cref="ParamKind.Vector2"/>/<see cref="ParamKind.Vector3"/>/<see cref="ParamKind.Vector4"/> -> <see cref="VectorValue"/> (unused components hold 0)</description></item>
    ///   <item><description><see cref="ParamKind.Enum"/> -> <see cref="EnumValue"/></description></item>
    /// </list>
    /// </summary>
    public readonly record struct VolumeOverrideParamValueDto(
        ParamKind Kind,
        bool? BoolValue,
        int? IntValue,
        float? FloatValue,
        ColorDto? ColorValue,
        Vector4Dto? VectorValue,
        string? EnumValue);
}
