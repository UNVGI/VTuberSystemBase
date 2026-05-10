using System.Collections.Generic;
using System.Text.Json;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Request payload for
    /// <see cref="CameraIpcTopics.VolumeOverridesMetadata(string)"/> (design.md L1276
    /// / L1349). UI asks the main-output side for the dynamic Override schema so it
    /// can build the parameter UI controls.
    /// </summary>
    public readonly struct VolumeMetadataRequest
    {
        /// <summary>Target camera. Redundant with the topic <c>{cameraId}</c> segment, kept for self-contained logging.</summary>
        public string CameraId { get; init; }
    }

    /// <summary>
    /// Response payload for
    /// <see cref="CameraIpcTopics.VolumeOverridesMetadata(string)"/> (design.md L1276
    /// / L1350). Lists every override that may be attached, with each parameter's
    /// type / range / labelling metadata.
    /// </summary>
    public readonly struct VolumeMetadataResponse
    {
        public IReadOnlyList<VolumeOverrideSchema> Overrides { get; init; }
    }

    /// <summary>
    /// Schema for one override block (design.md L1352-L1357). Drives the headline
    /// label and the list of parameter controls inside the Override card UI.
    /// </summary>
    public readonly struct VolumeOverrideSchema
    {
        /// <summary>Override type name (e.g. <c>Bloom</c>, <c>Tonemapping</c>).</summary>
        public string Type { get; init; }

        /// <summary>Display label for the Override card.</summary>
        public string DisplayName { get; init; }

        /// <summary>The parameter schemas for this override.</summary>
        public IReadOnlyList<VolumeParamSchema> Params { get; init; }
    }

    /// <summary>
    /// Schema for one parameter inside a <see cref="VolumeOverrideSchema"/>
    /// (design.md L1359-L1369). Drives the choice and configuration of UI control
    /// (slider / numeric / toggle / colour picker / dropdown).
    /// </summary>
    public readonly struct VolumeParamSchema
    {
        /// <summary>The parameter key — also the topic <c>{param}</c> segment.</summary>
        public string Name { get; init; }

        /// <summary>One of <c>float</c> / <c>int</c> / <c>bool</c> / <c>color</c> / <c>enum</c>.</summary>
        public string TypeTag { get; init; }

        /// <summary>Minimum value (numeric types only). Forward-compatible: receivers MUST tolerate omission.</summary>
        public JsonElement? Min { get; init; }

        /// <summary>Maximum value (numeric types only). Forward-compatible: receivers MUST tolerate omission.</summary>
        public JsonElement? Max { get; init; }

        /// <summary>Default value, used to seed initial control state when no state has been published yet.</summary>
        public JsonElement Default { get; init; }

        /// <summary>Display label for the parameter control.</summary>
        public string DisplayName { get; init; }

        /// <summary>Optional unit suffix (e.g. <c>"px"</c>, <c>"deg"</c>).</summary>
        public string? Unit { get; init; }

        /// <summary>Required when <see cref="TypeTag"/> is <c>enum</c>; null otherwise.</summary>
        public IReadOnlyList<string>? EnumValues { get; init; }
    }
}
