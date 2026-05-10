using System.Collections.Generic;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.CamerasList"/>
    /// (<c>cameras/list</c>, design.md L1266 / L1308-L1313). Coalesced state
    /// (last-write-wins). Published by the main-output side.
    /// </summary>
    public readonly struct CamerasListPayload
    {
        /// <summary>The full lineup of cameras visible to the UI.</summary>
        public IReadOnlyList<CameraListEntry> Cameras { get; init; }

        /// <summary>
        /// Wall-clock millisecond timestamp when the main-output side built this snapshot.
        /// Used for staleness detection and ordering when multiple producers race.
        /// </summary>
        public long UpdatedAtUnixMs { get; init; }
    }

    /// <summary>
    /// One row inside <see cref="CamerasListPayload.Cameras"/> (design.md L1315-L1321).
    /// </summary>
    public readonly struct CameraListEntry
    {
        /// <summary>Stable identifier allocated by the main-output side.</summary>
        public string CameraId { get; init; }

        /// <summary>Human-readable name as currently set in metadata.</summary>
        public string DisplayName { get; init; }

        /// <summary>Wire-format projection-mode string (<see cref="CameraTypeNames"/>).</summary>
        public string Type { get; init; }

        /// <summary>Default spawn transform attached to this camera at creation time.</summary>
        public CameraDefaultTransform DefaultTransform { get; init; }
    }
}
