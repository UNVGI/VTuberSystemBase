using System;
using System.Text;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Topic string constants and type-safe builders used by the camera-switcher tab UI
    /// and the main-output-side camera adapter. Centralises every topic literal listed
    /// in design.md L1263-L1281 to prevent typos and to apply a single ASCII safety
    /// policy on dynamic <c>cameraId</c> / <c>type</c> / <c>param</c> / <c>key</c>
    /// segments (mirrors the policy in <c>character-selection-tab</c>'s
    /// <c>CharacterTopics.Safe</c>).
    /// </summary>
    public static class CameraIpcTopics
    {
        // ---- Static topics (design.md L1265-L1267, L1277-L1280) ----

        /// <summary>Event: UI → main, with op = add / delete / active-set.</summary>
        public const string CameraCommand = "camera/command";

        /// <summary>State: main → UI, full camera lineup.</summary>
        public const string CamerasList = "cameras/list";

        /// <summary>State: main → UI, currently active cameraId (or null).</summary>
        public const string CamerasActive = "cameras/active";

        /// <summary>Event: main → UI, allocated cameraId for a UI request.</summary>
        public const string CameraCreated = "camera/created";

        /// <summary>Event: main → UI, camera-related failure notification.</summary>
        public const string CameraError = "camera/error";

        /// <summary>Event: UI → main, preset CRUD / activation command.</summary>
        public const string PresetCommand = "camera/preset/command";

        /// <summary>State: UI self-published, current preset name list.</summary>
        public const string PresetList = "camera/preset/list";

        /// <summary>State: UI self-published, currently active preset name.</summary>
        public const string PresetActive = "camera/preset/active";

        /// <summary>Event: UI → main, multi-preview attach / detach request.</summary>
        public const string PreviewCommand = "camera/preview/command";

        // ---- Per-camera dynamic topics (design.md L1270-L1276, L1281) ----

        /// <summary>State: <c>camera/{cameraId}/metadata/{key}</c> (design.md L1270).</summary>
        public static string CameraMetadata(string cameraId, string key)
            => $"camera/{Safe(cameraId)}/metadata/{Safe(key)}";

        /// <summary>State prefix used for subscribe-by-prefix matching against all metadata keys of a camera.</summary>
        public static string CameraMetadataPrefix(string cameraId)
            => $"camera/{Safe(cameraId)}/metadata/";

        /// <summary>Event: <c>camera/{cameraId}/volume/command</c> (design.md L1271).</summary>
        public static string VolumeCommand(string cameraId)
            => $"camera/{Safe(cameraId)}/volume/command";

        /// <summary>State: <c>camera/{cameraId}/volume/enabled</c> (design.md L1272).</summary>
        public static string VolumeEnabled(string cameraId)
            => $"camera/{Safe(cameraId)}/volume/enabled";

        /// <summary>State: <c>camera/{cameraId}/volume/override/{type}/enabled</c> (design.md L1273).</summary>
        public static string VolumeOverrideEnabled(string cameraId, string overrideType)
            => $"camera/{Safe(cameraId)}/volume/override/{Safe(overrideType)}/enabled";

        /// <summary>State: <c>camera/{cameraId}/volume/override/{type}/{param}</c> (design.md L1274).</summary>
        public static string VolumeOverrideParam(string cameraId, string overrideType, string param)
            => $"camera/{Safe(cameraId)}/volume/override/{Safe(overrideType)}/{Safe(param)}";

        /// <summary>State prefix used for subscribe-by-prefix matching against every override-param topic of a camera.</summary>
        public static string VolumeOverrideParamPrefix(string cameraId, string overrideType)
            => $"camera/{Safe(cameraId)}/volume/override/{Safe(overrideType)}/";

        /// <summary>State: <c>camera/{cameraId}/volume/overrides</c> (design.md L1275).</summary>
        public static string VolumeOverridesList(string cameraId)
            => $"camera/{Safe(cameraId)}/volume/overrides";

        /// <summary>Request/Response: <c>camera/{cameraId}/volume/overrides/metadata</c> (design.md L1276).</summary>
        public static string VolumeOverridesMetadata(string cameraId)
            => $"camera/{Safe(cameraId)}/volume/overrides/metadata";

        /// <summary>State: <c>camera/{cameraId}/preview/handle</c> (design.md L1281).</summary>
        public static string PreviewHandle(string cameraId)
            => $"camera/{Safe(cameraId)}/preview/handle";

        // ---- CameraId overloads (skip re-validation; CameraId already enforces character class) ----

        public static string CameraMetadata(CameraId cameraId, string key)
            => $"camera/{Required(cameraId)}/metadata/{Safe(key)}";

        public static string VolumeCommand(CameraId cameraId)
            => $"camera/{Required(cameraId)}/volume/command";

        public static string VolumeEnabled(CameraId cameraId)
            => $"camera/{Required(cameraId)}/volume/enabled";

        public static string VolumeOverrideEnabled(CameraId cameraId, string overrideType)
            => $"camera/{Required(cameraId)}/volume/override/{Safe(overrideType)}/enabled";

        public static string VolumeOverrideParam(CameraId cameraId, string overrideType, string param)
            => $"camera/{Required(cameraId)}/volume/override/{Safe(overrideType)}/{Safe(param)}";

        public static string VolumeOverridesList(CameraId cameraId)
            => $"camera/{Required(cameraId)}/volume/overrides";

        public static string VolumeOverridesMetadata(CameraId cameraId)
            => $"camera/{Required(cameraId)}/volume/overrides/metadata";

        public static string PreviewHandle(CameraId cameraId)
            => $"camera/{Required(cameraId)}/preview/handle";

        // ---- Safety helpers ----

        /// <summary>
        /// Percent-encodes any character outside ASCII alphanumerics and <c>-</c>,
        /// <c>_</c>, <c>.</c>. Idempotent for already-safe strings; throws on null or
        /// empty input. Mirrors <c>character-selection-tab</c>'s <c>CharacterTopics.Safe</c>
        /// so both contracts share a single segment-encoding policy.
        /// </summary>
        public static string Safe(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length == 0) throw new ArgumentException("Topic segment must not be empty.", nameof(value));

            StringBuilder? builder = null;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (IsSafeChar(c))
                {
                    builder?.Append(c);
                    continue;
                }
                builder ??= new StringBuilder(value.Length + 8).Append(value, 0, i);
                AppendPercentEncoded(builder, c);
            }
            return builder?.ToString() ?? value;
        }

        private static string Required(CameraId cameraId)
        {
            if (!cameraId.HasValue)
                throw new ArgumentException("CameraId is unset.", nameof(cameraId));
            return cameraId.Value;
        }

        private static bool IsSafeChar(char c)
        {
            return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_' or '.';
        }

        private static void AppendPercentEncoded(StringBuilder builder, char c)
        {
            var bytes = Encoding.UTF8.GetBytes(new[] { c });
            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append('%');
                builder.Append(HexUpper(bytes[i] >> 4));
                builder.Append(HexUpper(bytes[i] & 0xF));
            }
        }

        private static char HexUpper(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));
    }
}
