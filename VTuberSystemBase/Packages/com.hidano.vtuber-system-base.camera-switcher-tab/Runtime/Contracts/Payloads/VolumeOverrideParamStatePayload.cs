using System.Text.Json;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for
    /// <see cref="CameraIpcTopics.VolumeOverrideParam(string,string,string)"/>
    /// (<c>camera/{cameraId}/volume/override/{type}/{param}</c>, design.md L1274).
    /// Bidirectional state. The wire shape carried in <see cref="Value"/> is
    /// determined by the matching <see cref="VolumeParamSchema.TypeTag"/>
    /// (<c>float</c> / <c>int</c> / <c>bool</c> / <c>color</c> / <c>enum</c>).
    /// </summary>
    public readonly struct VolumeOverrideParamStatePayload
    {
        /// <summary>Opaque value whose JSON shape depends on the parameter's type tag.</summary>
        public JsonElement Value { get; init; }
    }
}
