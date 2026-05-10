#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts.Results
{
    /// <summary>
    /// Result of <c>IUcapiOscEmitter.Send</c> /
    /// <c>IUcapiOscEmitter.StartAsync</c>. UDP send fails are non-fatal; the
    /// caller logs and aggregates them via <c>FailureAggregator</c>.
    /// </summary>
    public readonly struct OscEmitResult : IEquatable<OscEmitResult>
    {
        private OscEmitResult(bool success, OscEmitFailure? failure)
        {
            Success = success;
            Failure = failure;
        }

        public bool Success { get; }
        public OscEmitFailure? Failure { get; }

        public static OscEmitResult Ok() => new OscEmitResult(true, null);

        public static OscEmitResult Fail(OscEmitFailure failure) => new OscEmitResult(false, failure);

        public bool Equals(OscEmitResult other)
            => Success == other.Success && Nullable.Equals(Failure, other.Failure);

        public override bool Equals(object? obj) => obj is OscEmitResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Success, Failure);
    }

    /// <summary>Failure descriptor accompanying <see cref="OscEmitResult"/>.</summary>
    public readonly struct OscEmitFailure : IEquatable<OscEmitFailure>
    {
        public OscEmitFailure(OscFailureKind kind, string? detail = null, Exception? inner = null)
        {
            Kind = kind;
            Detail = detail;
            Inner = inner;
        }

        public OscFailureKind Kind { get; }
        public string? Detail { get; }
        public Exception? Inner { get; }

        public bool Equals(OscEmitFailure other)
            => Kind == other.Kind
               && string.Equals(Detail, other.Detail, StringComparison.Ordinal)
               && ReferenceEquals(Inner, other.Inner);

        public override bool Equals(object? obj) => obj is OscEmitFailure other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, Detail, Inner);
    }

    /// <summary>Coarse-grained classification for OSC failures.</summary>
    public enum OscFailureKind
    {
        InitializationFailed,
        NotStarted,
        PortInUse,
        SocketError,
        SerializeFailed,
        InvalidAddress,
        Disposed,
    }
}
