#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Outcome of <see cref="IOscReceiverHost.StartAsync"/>. Designed as a discriminated
    /// union so callers can branch on success / failure without exception-driven control flow.
    /// </summary>
    /// <remarks>
    /// On <see cref="Failure"/>, the host MUST be observable via <see cref="IOscReceiverHost.Status"/>
    /// as <see cref="OscReceiverHostStatus.Failed"/> and MUST NOT raise
    /// <see cref="IOscReceiverHost.MessageReceived"/> until a subsequent successful
    /// <see cref="IOscReceiverHost.StartAsync"/> attempt.
    /// </remarks>
    public readonly struct OscReceiverStartResult
    {
        private OscReceiverStartResult(bool success, string? failureDetail, Exception? exception)
        {
            Success = success;
            FailureDetail = failureDetail;
            Exception = exception;
        }

        public bool Success { get; }

        /// <summary>Human-readable failure reason. Null when <see cref="Success"/> is true.</summary>
        public string? FailureDetail { get; }

        /// <summary>Optional exception captured at start time; null when not applicable.</summary>
        public Exception? Exception { get; }

        public static OscReceiverStartResult Ok() => new OscReceiverStartResult(true, null, null);

        public static OscReceiverStartResult Failure(string detail, Exception? exception = null)
        {
            if (string.IsNullOrEmpty(detail)) detail = "OscReceiverStart failed.";
            return new OscReceiverStartResult(false, detail, exception);
        }
    }
}
