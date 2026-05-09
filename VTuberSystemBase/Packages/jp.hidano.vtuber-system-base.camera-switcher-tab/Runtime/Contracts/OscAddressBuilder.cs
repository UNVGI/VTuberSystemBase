using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Builds the OSC address used to publish UCAPI Flat Records from the UI to the
    /// main-output side (design.md L1287).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default prefix is <c>/ucapi/camera</c>. Per design.md L1289 it is
    /// configurable; callers wishing to override may pass a custom prefix to
    /// <see cref="BuildFlatAddress(string, string)"/>. The prefix MUST start with
    /// <c>/</c> and MUST NOT end with <c>/</c>.
    /// </para>
    /// <para>
    /// <c>cameraId</c> is constrained to ASCII alphanumerics + <c>-</c> + <c>_</c>
    /// (design.md L1289 : 採番側のメイン出力側が保証する). Violations raise
    /// <see cref="ArgumentException"/> rather than being silently encoded; this matches
    /// the Topic-segment policy in <c>character-selection-tab</c>'s contracts.
    /// </para>
    /// </remarks>
    public static class OscAddressBuilder
    {
        /// <summary>The default OSC address prefix (design.md L1287).</summary>
        public const string DefaultPrefix = "/ucapi/camera";

        /// <summary>
        /// Builds <c>{prefix}/{cameraId}/flat</c> using <see cref="DefaultPrefix"/>.
        /// </summary>
        public static string BuildFlatAddress(string cameraId)
            => BuildFlatAddress(DefaultPrefix, cameraId);

        /// <summary>
        /// Builds <c>{prefix}/{cameraId}/flat</c>. <paramref name="prefix"/> is validated
        /// for the leading <c>/</c> and absence of trailing <c>/</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="prefix"/> is empty / does not start with <c>/</c> / ends with
        /// <c>/</c>; or <paramref name="cameraId"/> contains a disallowed character.
        /// </exception>
        public static string BuildFlatAddress(string prefix, string cameraId)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (cameraId == null) throw new ArgumentNullException(nameof(cameraId));
            ValidatePrefix(prefix);
            ValidateCameraId(cameraId);
            return string.Concat(prefix, "/", cameraId, "/flat");
        }

        /// <summary>
        /// Builds <c>{prefix}/{cameraId}/flat</c> from a <see cref="CameraId"/>. The
        /// CameraId character set is already validated by its constructor, so only the
        /// prefix is re-checked here.
        /// </summary>
        public static string BuildFlatAddress(string prefix, CameraId cameraId)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (!cameraId.HasValue) throw new ArgumentException("CameraId is unset.", nameof(cameraId));
            ValidatePrefix(prefix);
            return string.Concat(prefix, "/", cameraId.Value, "/flat");
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="cameraId"/> is non-empty and contains
        /// only characters allowed in an OSC <c>cameraId</c> segment.
        /// </summary>
        public static bool IsValidCameraIdSegment(string? cameraId)
        {
            if (string.IsNullOrEmpty(cameraId)) return false;
            for (var i = 0; i < cameraId!.Length; i++)
            {
                if (!IsAllowedCameraIdChar(cameraId[i])) return false;
            }
            return true;
        }

        private static void ValidatePrefix(string prefix)
        {
            if (prefix.Length == 0)
                throw new ArgumentException("OSC address prefix must not be empty.", nameof(prefix));
            if (prefix[0] != '/')
                throw new ArgumentException("OSC address prefix must start with '/'.", nameof(prefix));
            if (prefix[^1] == '/')
                throw new ArgumentException("OSC address prefix must not end with '/'.", nameof(prefix));
        }

        private static void ValidateCameraId(string cameraId)
        {
            if (cameraId.Length == 0)
                throw new ArgumentException("cameraId must not be empty.", nameof(cameraId));
            for (var i = 0; i < cameraId.Length; i++)
            {
                if (!IsAllowedCameraIdChar(cameraId[i]))
                {
                    throw new ArgumentException(
                        $"cameraId may contain only ASCII alphanumerics, '-', and '_'. Got: '{cameraId}'",
                        nameof(cameraId));
                }
            }
        }

        private static bool IsAllowedCameraIdChar(char c)
        {
            return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_';
        }
    }
}
