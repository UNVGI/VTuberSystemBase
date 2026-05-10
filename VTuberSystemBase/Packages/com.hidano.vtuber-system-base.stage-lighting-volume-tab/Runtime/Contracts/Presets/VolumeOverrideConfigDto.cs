using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Persisted per-Volume-Override-type configuration inside a <see cref="PresetDto"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TypeFullName"/> matches <see cref="VolumeOverrideTypeDto.TypeFullName"/>.
    /// <see cref="Params"/> keys match <see cref="VolumeOverrideParamDto.ParamName"/>.
    /// Unknown type/param entries seen on load MUST be skipped (forward compatibility,
    /// core-ipc-foundation D-7).
    /// </remarks>
    public sealed class VolumeOverrideConfigDto
    {
        public string TypeFullName { get; set; } = "";
        public bool Enabled { get; set; }
        public Dictionary<string, VolumeOverrideParamValueDto> Params { get; set; } = new();
    }
}
