using System.Text.Json;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// State payload for <see cref="CameraIpcTopics.CameraMetadata(string,string)"/>
    /// (<c>camera/{cameraId}/metadata/{key}</c>, design.md L1270). Bidirectional —
    /// either side may publish; the topic key (<c>displayName</c> /
    /// <c>type</c> / <c>defaultTransform</c>) determines the value shape carried in
    /// <see cref="Value"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Value"/> is intentionally a <see cref="JsonElement"/> so this DTO
    /// covers all current and future metadata keys without per-key types. Receivers
    /// pattern-match on the topic key (see <see cref="CameraMetadataKeys"/>) to decide
    /// how to deserialise the inner value.
    /// </remarks>
    public readonly struct CameraMetadataStatePayload
    {
        /// <summary>Opaque value whose shape depends on the topic key.</summary>
        public JsonElement Value { get; init; }
    }

    /// <summary>Well-known keys carried as the <c>{key}</c> segment of the metadata topic (design.md L1270).</summary>
    public static class CameraMetadataKeys
    {
        public const string DisplayName = "displayName";
        public const string Type = "type";
        public const string DefaultTransform = "defaultTransform";
    }
}
