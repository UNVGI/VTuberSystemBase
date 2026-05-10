#nullable enable
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights
{
    /// <summary>
    /// Bidirectional mapping between <see cref="LightTypeDto"/> and <see cref="LightType"/>.
    /// Delegates to <see cref="DtoConverters"/> so the conversion is defined exactly once.
    /// </summary>
    internal static class LightTypeMapper
    {
        public static LightType ToUnity(LightTypeDto dto) => DtoConverters.ToUnity(dto);
        public static LightTypeDto ToDto(LightType type) => DtoConverters.ToDto(type);
    }
}
