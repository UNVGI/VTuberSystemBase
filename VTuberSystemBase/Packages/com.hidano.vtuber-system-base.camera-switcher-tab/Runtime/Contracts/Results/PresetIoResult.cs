#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts.Results
{
    /// <summary>
    /// Result of <c>IPresetStore.LoadAllAsync</c> / <c>SaveAllAsync</c>. Failures
    /// never throw out of the adapter — the caller surfaces the structured reason
    /// to the diagnostics aggregator (Requirement 11.9).
    /// </summary>
    public readonly struct PresetIoResult : IEquatable<PresetIoResult>
    {
        private PresetIoResult(bool success, PresetIoFailureKind kind, string? detail, Exception? inner)
        {
            Success = success;
            FailureKind = kind;
            FailureDetail = detail;
            Inner = inner;
        }

        public bool Success { get; }
        public PresetIoFailureKind FailureKind { get; }
        public string? FailureDetail { get; }
        public Exception? Inner { get; }

        public static PresetIoResult Ok() => new PresetIoResult(true, PresetIoFailureKind.None, null, null);

        public static PresetIoResult Fail(PresetIoFailureKind kind, string? detail = null, Exception? inner = null)
        {
            if (kind == PresetIoFailureKind.None)
                throw new ArgumentException("PresetIoFailureKind.None is reserved for the success state.", nameof(kind));
            return new PresetIoResult(false, kind, detail, inner);
        }

        public bool Equals(PresetIoResult other)
            => Success == other.Success
               && FailureKind == other.FailureKind
               && string.Equals(FailureDetail, other.FailureDetail, StringComparison.Ordinal)
               && ReferenceEquals(Inner, other.Inner);

        public override bool Equals(object? obj) => obj is PresetIoResult other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Success, (int)FailureKind, FailureDetail, Inner);
    }

    /// <summary>Coarse-grained classification for preset I/O failures.</summary>
    public enum PresetIoFailureKind
    {
        None = 0,
        FileNotFound,
        ReadFailed,
        WriteFailed,
        Corrupted,
        SerializationFailed,
        Cancelled,
    }
}
