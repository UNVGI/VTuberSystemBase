using System.Collections.Generic;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.VolumeOverridesList(string)"/>
    /// (<c>camera/{cameraId}/volume/overrides</c>, design.md L1275). Coalesced state
    /// listing all currently-attached override blocks for the camera and their
    /// individual enabled flags. Published by the main-output side.
    /// </summary>
    public readonly struct VolumeOverridesStatePayload
    {
        /// <summary>The complete attached-override set for this camera.</summary>
        public IReadOnlyList<VolumeOverrideEntry> Overrides { get; init; }
    }

    /// <summary>One row inside <see cref="VolumeOverridesStatePayload.Overrides"/>.</summary>
    public readonly struct VolumeOverrideEntry
    {
        /// <summary>The override type name (e.g. <c>Bloom</c>).</summary>
        public string Type { get; init; }

        /// <summary>Whether this override block is enabled.</summary>
        public bool Enabled { get; init; }
    }
}
