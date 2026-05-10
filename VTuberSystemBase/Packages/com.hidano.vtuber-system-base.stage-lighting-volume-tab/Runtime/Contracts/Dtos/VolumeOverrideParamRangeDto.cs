using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Range descriptor for a Volume Override parameter. All fields are optional; only the
    /// pair relevant to the corresponding <see cref="ParamKind"/> is populated:
    /// <list type="bullet">
    ///   <item><description><see cref="ParamKind.Float"/>/<see cref="ParamKind.ClampedFloat"/> -> <see cref="FloatMin"/>/<see cref="FloatMax"/></description></item>
    ///   <item><description><see cref="ParamKind.Int"/> -> <see cref="IntMin"/>/<see cref="IntMax"/></description></item>
    ///   <item><description><see cref="ParamKind.Enum"/> -> <see cref="EnumValues"/></description></item>
    /// </list>
    /// </summary>
    public readonly record struct VolumeOverrideParamRangeDto(
        float? FloatMin, float? FloatMax,
        int? IntMin, int? IntMax,
        IReadOnlyList<string>? EnumValues);
}
