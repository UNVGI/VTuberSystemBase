using System;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// State payload for <c>slot/{slotId}/status</c>. Published by the main output side
    /// to reflect the operational state of the slot. Unknown <see cref="Status"/> values
    /// MUST be treated as a forward-compatible "Unknown" by the UI (skip + log).
    /// </summary>
    [Serializable]
    public sealed class SlotStatusPayload
    {
        /// <summary>One of: <c>Empty</c>, <c>Assigning</c>, <c>Assigned</c>, <c>Error</c>.</summary>
        public string Status { get; init; } = "Empty";
        public string? Detail { get; init; }
    }
}
