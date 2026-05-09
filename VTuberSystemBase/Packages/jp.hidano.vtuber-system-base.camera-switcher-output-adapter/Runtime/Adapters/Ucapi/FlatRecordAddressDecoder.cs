#nullable enable
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Ucapi
{
    /// <summary>
    /// Pure parser that extracts the cameraId segment from an OSC address that
    /// matches <c>/ucapi/camera/{cameraId}/flat</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parser performs <em>only</em> structural validation: leading prefix
    /// match, trailing <c>/flat</c> segment, and cameraId character class via
    /// <c>OscAddressBuilder.IsValidCameraIdSegment</c>. Membership / lifecycle
    /// checks belong to the router and registry layers (CSO-15, Requirement 2.2).
    /// </para>
    /// <para>
    /// The decoder is allocation-free for the success path (returns a substring
    /// allocated by <c>System.String.Substring</c>; uOSC delivers OSC addresses
    /// already as <c>string</c>, so this is the canonical encoding).
    /// </para>
    /// </remarks>
    public static class FlatRecordAddressDecoder
    {
        /// <summary>The default OSC address prefix (mirrors <c>OscAddressBuilder.DefaultPrefix</c>).</summary>
        public const string DefaultPrefix = OscAddressBuilder.DefaultPrefix;

        private const string FlatSuffix = "/flat";

        /// <summary>
        /// Tries to extract the cameraId from <paramref name="address"/> using
        /// <see cref="DefaultPrefix"/>.
        /// </summary>
        /// <returns>The cameraId segment, or <c>null</c> if the address does not
        /// match the expected shape.</returns>
        public static string? TryDecodeCameraId(string? address)
            => TryDecodeCameraId(address, DefaultPrefix);

        /// <summary>
        /// Tries to extract the cameraId from <paramref name="address"/> using
        /// <paramref name="prefix"/>.
        /// </summary>
        public static string? TryDecodeCameraId(string? address, string? prefix)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (string.IsNullOrEmpty(prefix)) return null;

            // Must start with prefix + '/'.
            var prefixLen = prefix!.Length;
            if (address!.Length < prefixLen + 1 + 1 + FlatSuffix.Length) return null; // prefix + '/' + at least 1 char cameraId + '/flat'
            if (!StartsWith(address, prefix)) return null;
            if (address[prefixLen] != '/') return null;

            // Must end with '/flat'.
            if (!EndsWith(address, FlatSuffix)) return null;

            var cameraIdStart = prefixLen + 1;
            var cameraIdEnd = address.Length - FlatSuffix.Length; // exclusive
            if (cameraIdEnd <= cameraIdStart) return null;
            var cameraId = address.Substring(cameraIdStart, cameraIdEnd - cameraIdStart);

            if (!OscAddressBuilder.IsValidCameraIdSegment(cameraId)) return null;

            return cameraId;
        }

        private static bool StartsWith(string s, string prefix)
        {
            if (s.Length < prefix.Length) return false;
            for (var i = 0; i < prefix.Length; i++)
            {
                if (s[i] != prefix[i]) return false;
            }
            return true;
        }

        private static bool EndsWith(string s, string suffix)
        {
            if (s.Length < suffix.Length) return false;
            var offset = s.Length - suffix.Length;
            for (var i = 0; i < suffix.Length; i++)
            {
                if (s[offset + i] != suffix[i]) return false;
            }
            return true;
        }
    }
}
