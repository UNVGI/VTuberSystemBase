namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Event payload for <see cref="CameraIpcTopics.CameraError"/>
    /// (<c>camera/error</c>, design.md L1269 / L1339-L1346). Per-operation failure
    /// notification from the main-output side; the UI degrades only the affected
    /// operation and keeps other cameras / operations running (Requirement 12.3).
    /// </summary>
    public readonly struct CameraErrorEventPayload
    {
        /// <summary>Set when the failing operation originated from a UI command.</summary>
        public string? ClientRequestId { get; init; }

        /// <summary>Set when the failure scope is a specific cameraId.</summary>
        public string? CameraId { get; init; }

        /// <summary>The failing operation's name (e.g. <c>add</c>, <c>delete</c>, <c>active-set</c>).</summary>
        public string Op { get; init; }

        /// <summary>
        /// Coarse error category. Examples (design.md L1344): <c>ResourceExhausted</c>,
        /// <c>InvalidType</c>, <c>UnknownCameraId</c>. Receivers MUST treat unknown values
        /// as "Unknown" + log (forward-compatible).
        /// </summary>
        public string Reason { get; init; }

        /// <summary>Optional human-readable detail (logged, optionally surfaced in UI).</summary>
        public string? Detail { get; init; }
    }

    /// <summary>String constants for the well-known <see cref="CameraErrorEventPayload.Reason"/> values.</summary>
    public static class CameraErrorReasons
    {
        public const string ResourceExhausted = "ResourceExhausted";
        public const string InvalidType = "InvalidType";
        public const string UnknownCameraId = "UnknownCameraId";
    }
}
