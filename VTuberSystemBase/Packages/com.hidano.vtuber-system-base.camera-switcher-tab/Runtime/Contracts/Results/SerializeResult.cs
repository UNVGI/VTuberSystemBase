#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts.Results
{
    /// <summary>
    /// Result of <c>IUcapiFlatRecordSerializer.Serialize</c>. A success carries a
    /// non-empty <see cref="UcapiFlatRecord"/>; a failure carries a structured
    /// reason without throwing (Requirement 3.4 / 3.8). Mutually exclusive states.
    /// </summary>
    public readonly struct SerializeResult : IEquatable<SerializeResult>
    {
        private SerializeResult(bool success, UcapiFlatRecord record, SerializeFailureReason reason, string? detail)
        {
            Success = success;
            Record = record;
            FailureReason = reason;
            FailureDetail = detail;
        }

        public bool Success { get; }
        public UcapiFlatRecord Record { get; }
        public SerializeFailureReason FailureReason { get; }
        public string? FailureDetail { get; }

        public static SerializeResult Ok(UcapiFlatRecord record)
        {
            if (!record.HasValue)
                throw new ArgumentException("SerializeResult.Ok requires a non-empty record.", nameof(record));
            return new SerializeResult(true, record, SerializeFailureReason.None, null);
        }

        public static SerializeResult Invalid(SerializeFailureReason reason, string? detail = null)
        {
            if (reason == SerializeFailureReason.None)
                throw new ArgumentException("SerializeFailureReason.None is reserved for the success state.", nameof(reason));
            return new SerializeResult(false, UcapiFlatRecord.Empty, reason, detail);
        }

        public bool Equals(SerializeResult other)
            => Success == other.Success
               && Record.Equals(other.Record)
               && FailureReason == other.FailureReason
               && string.Equals(FailureDetail, other.FailureDetail, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is SerializeResult other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Success, Record, (int)FailureReason, FailureDetail);
    }

    /// <summary>Coarse-grained failure classification for <see cref="SerializeResult"/>.</summary>
    public enum SerializeFailureReason
    {
        None = 0,
        InvalidPosition,
        InvalidRotation,
        InvalidFocalLength,
        InvalidSensorSize,
        InvalidClipPlanes,
        InvalidCameraId,
        AdapterFault,
    }
}
