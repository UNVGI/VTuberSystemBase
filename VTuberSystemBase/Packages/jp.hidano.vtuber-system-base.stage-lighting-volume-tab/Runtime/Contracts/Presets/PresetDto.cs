using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Single preset entry inside <see cref="PresetFileRoot"/>. Captures the stage
    /// selection, the configured lights, and the volume override settings that should be
    /// restored when the preset is activated.
    /// </summary>
    /// <remarks>
    /// <see cref="Name"/> must be unique within the parent file (Requirement 7.5). LightId
    /// is intentionally not persisted (Requirement 7.8 / SL-8); a fresh GUID is issued on
    /// restore.
    /// </remarks>
    public sealed class PresetDto
    {
        public string Name { get; set; } = "";
        public string? StageAddressableKey { get; set; }
        public List<LightConfigDto> Lights { get; set; } = new();
        public List<VolumeOverrideConfigDto> VolumeOverrides { get; set; } = new();
    }
}
