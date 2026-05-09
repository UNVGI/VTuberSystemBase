using System;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// State payload for <c>slot/{slotId}/assignment</c>. UI is the authority for
    /// assignments; the main output side echoes status back via <c>slot/{slotId}/status</c>.
    /// <see cref="AvatarKey"/> being null indicates the slot is intentionally empty.
    /// </summary>
    [Serializable]
    public sealed class SlotAssignmentPayload
    {
        public string? AvatarKey { get; init; }
    }
}
