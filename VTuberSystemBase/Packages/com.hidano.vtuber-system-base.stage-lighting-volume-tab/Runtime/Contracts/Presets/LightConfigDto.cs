namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Persisted per-light configuration inside a <see cref="PresetDto"/>. Mirrors
    /// <see cref="LightInitialDto"/> but lives as a mutable class to keep JSON round-trip
    /// (System.Text.Json) ergonomic.
    /// </summary>
    /// <remarks>
    /// LightId is intentionally NOT persisted (Requirement 7.8 / SL-8); a new GUID is
    /// issued every time the preset is restored, so referencing a saved light by id
    /// across sessions is not supported.
    /// </remarks>
    public sealed class LightConfigDto
    {
        public string DisplayName { get; set; } = "";
        public LightTypeDto Type { get; set; } = LightTypeDto.Directional;
        public Vector3Dto Rotation { get; set; }
        public ColorDto Color { get; set; } = new(1, 1, 1, 1);
        public float Intensity { get; set; } = 1.0f;
        public float Range { get; set; } = 10.0f;
        public float SpotAngle { get; set; } = 30.0f;
    }
}
