#nullable enable
using System.Collections.Generic;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// One preset record, persisted as JSON via <see cref="IPresetStore"/>. Captures
    /// the camera lineup, each camera's Local Volume configuration, and the active
    /// camera id. Logical camera ids stored here are stable across restarts; the
    /// adapter remaps them to runtime cameraIds via <c>camera/created</c>.
    /// </summary>
    public sealed class PresetPayload
    {
        public string Name { get; init; } = string.Empty;
        public IReadOnlyList<PresetCameraEntry> Cameras { get; init; } = new List<PresetCameraEntry>();
        public IReadOnlyDictionary<string, VolumeConfig> VolumeConfigs { get; init; }
            = new Dictionary<string, VolumeConfig>();
        /// <summary>Logical id of the camera that was active when the preset was saved.</summary>
        public string? ActiveCameraLogicalId { get; init; }
    }

    /// <summary>One row inside <see cref="PresetPayload.Cameras"/>.</summary>
    public sealed class PresetCameraEntry
    {
        /// <summary>UI-side stable logical id used to correlate across save/restore cycles.</summary>
        public string LogicalId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public CameraType Type { get; init; }
        public CameraDefaultTransform DefaultTransform { get; init; }
    }
}
