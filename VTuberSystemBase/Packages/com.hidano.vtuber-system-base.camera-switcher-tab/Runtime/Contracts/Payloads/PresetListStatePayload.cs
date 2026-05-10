using System.Collections.Generic;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.PresetList"/>
    /// (<c>camera/preset/list</c>, design.md L1278). Self-published by the UI based
    /// on the local preset store, exposed for other tabs / RDS observers.
    /// </summary>
    public readonly struct PresetListStatePayload
    {
        /// <summary>Names of all presets currently on disk.</summary>
        public IReadOnlyList<string> Names { get; init; }
    }
}
