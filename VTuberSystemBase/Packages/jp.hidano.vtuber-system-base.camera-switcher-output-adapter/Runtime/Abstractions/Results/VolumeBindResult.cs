#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Outcome of an <see cref="ILocalVolumeBinder"/> operation.
    /// </summary>
    /// <remarks>
    /// Failure reasons are intentionally string-typed so adapter implementations can
    /// surface URP-specific detail without a closed enum; common values are documented
    /// on <see cref="VolumeBindFailureReasons"/>.
    /// </remarks>
    public readonly struct VolumeBindResult
    {
        private VolumeBindResult(bool success, string? reason, string? detail, Exception? exception)
        {
            Success = success;
            Reason = reason;
            Detail = detail;
            Exception = exception;
        }

        public bool Success { get; }

        /// <summary>Coarse failure category. See <see cref="VolumeBindFailureReasons"/>.</summary>
        public string? Reason { get; }

        /// <summary>Optional human-readable detail.</summary>
        public string? Detail { get; }

        /// <summary>Optional captured exception.</summary>
        public Exception? Exception { get; }

        public static VolumeBindResult Ok() => new VolumeBindResult(true, null, null, null);

        public static VolumeBindResult Error(string reason, string? detail = null, Exception? exception = null)
        {
            if (string.IsNullOrEmpty(reason)) reason = VolumeBindFailureReasons.Unknown;
            return new VolumeBindResult(false, reason, detail, exception);
        }
    }

    /// <summary>Well-known <see cref="VolumeBindResult.Reason"/> identifiers.</summary>
    public static class VolumeBindFailureReasons
    {
        public const string Unknown = "Unknown";
        public const string UnknownOverrideType = "UnknownOverrideType";
        public const string OverrideTypeMismatch = "OverrideTypeMismatch";
        public const string ParamNotFound = "ParamNotFound";
        public const string ParamTypeMismatch = "ParamTypeMismatch";
        public const string ReflectionFailed = "ReflectionFailed";
    }
}
