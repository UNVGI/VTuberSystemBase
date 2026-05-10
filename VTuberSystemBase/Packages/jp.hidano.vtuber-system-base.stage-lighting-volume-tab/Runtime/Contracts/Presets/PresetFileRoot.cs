using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Root document of <c>stage-lighting-volume-tab.json</c> persisted under
    /// <c>Application.persistentDataPath/vtuber-system-base/</c>. Holds every preset and
    /// remembers which one was last active so the next launch restores it.
    /// </summary>
    /// <remarks>
    /// <see cref="SchemaVersion"/> = 1 in the initial release. Future migrations will be
    /// handled by a dedicated migrator (out of scope for this spec). Forward
    /// compatibility: unknown JSON fields are ignored on read.
    /// </remarks>
    public sealed class PresetFileRoot
    {
        public int SchemaVersion { get; set; } = 1;
        public string? ActivePresetName { get; set; }
        public List<PresetDto> Presets { get; set; } = new();
    }
}
