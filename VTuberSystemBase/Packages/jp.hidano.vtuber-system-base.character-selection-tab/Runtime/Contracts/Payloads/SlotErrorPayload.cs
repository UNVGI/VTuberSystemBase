using System;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// Event payload for <c>slot/{slotId}/error</c>. Published by the main output side
    /// to notify the UI of slot-scoped failures. The UI MUST keep other slots running
    /// when receiving this event.
    /// </summary>
    [Serializable]
    public sealed class SlotErrorPayload
    {
        /// <summary>One of: <c>KeyNotFound</c>, <c>MotionPipelineInit</c>, <c>ApplyFailed</c>, <c>Unknown</c>.</summary>
        public string ErrorCode { get; init; } = "Unknown";
        public string? Detail { get; init; }
    }
}
