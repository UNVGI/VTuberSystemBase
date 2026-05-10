namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Topic string constants used by the stage-lighting-volume tab UI and the main-output-side
    /// StageLightingVolume adapter. Centralizes topic literals across stage / light / volume /
    /// preview channels to prevent typos. Dynamic topics (per-light property, per-volume-type
    /// override) are produced via the helper methods.
    /// </summary>
    public static class StageLightingTopics
    {
        // Stage
        public const string StageCatalog = "stage/catalog";                // state, StageCatalogDto
        public const string StageCurrent = "stage/current";                // state, StageCurrentDto
        public const string StageCommand = "stage/command";                // event, StageCommandDto (op: load|unload)
        public const string StageLoaded = "stage/loaded";                  // event, StageCurrentDto
        public const string StageLoadFailed = "stage/load-failed";         // event, StageLoadFailedDto

        // Light
        public const string LightsList = "lights/list";                    // state, LightListDto
        public const string LightCommand = "light/command";                // event, LightCommandDto (op: add|remove)
        public const string LightAdded = "light/added";                    // event, LightAddedDto
        public const string LightError = "light/error";                    // event, LightErrorDto

        // Light property topics are dynamic: light/{lightId}/{property}
        public static string LightProperty(string lightId, string property) => $"light/{lightId}/{property}";
        public const string PropertyIntensity = "intensity";
        public const string PropertyColor = "color";
        public const string PropertyRotation = "rotation";
        public const string PropertyType = "type";
        public const string PropertyRange = "range";
        public const string PropertySpotAngle = "spotAngle";
        public const string PropertyDisplayName = "displayName";

        // Volume
        public const string VolumeOverrideSchema = "volume/override/schema"; // request/response, VolumeOverrideSchemaDto
        public static string VolumeOverrideEnabled(string typeFullName) =>
            $"volume/override/{typeFullName}/enabled";                       // state, bool
        public static string VolumeOverrideParam(string typeFullName, string paramName) =>
            $"volume/override/{typeFullName}/{paramName}";                   // state, VolumeOverrideParamValueDto
        public const string VolumeCommand = "volume/command";                // event, future use (reserved)

        // Preview
        public const string PreviewCommand = "preview/command";              // event, PreviewCommandDto
        public const string PreviewState = "preview/state";                  // state, PreviewStateDto
    }
}
