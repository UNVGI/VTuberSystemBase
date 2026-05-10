#nullable enable
using System.Collections.Generic;
using System.Text.Json;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Snapshot of one camera's Local Volume configuration as stored inside a
    /// <see cref="PresetPayload"/>. <see cref="Enabled"/> mirrors
    /// <c>camera/{id}/volume/enabled</c>; <see cref="Overrides"/> lists every
    /// override block with its enabled flag and current parameter values.
    /// </summary>
    public sealed class VolumeConfig
    {
        public bool Enabled { get; init; } = true;
        public IReadOnlyList<VolumeOverride> Overrides { get; init; } = new List<VolumeOverride>();
    }

    /// <summary>
    /// One Local Volume override block (e.g. Bloom, Tonemapping) with its
    /// per-parameter values. Parameter values are kept as <see cref="JsonElement"/>
    /// so the wire shape (driven by the schema's TypeTag) is preserved without
    /// the UI needing per-parameter typed value classes.
    /// </summary>
    public sealed class VolumeOverride
    {
        public string Type { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public IReadOnlyDictionary<string, JsonElement> ParamValues { get; init; }
            = new Dictionary<string, JsonElement>();
    }
}
