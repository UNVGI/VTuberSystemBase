using System;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// Event payload for <c>slot/{slotId}/command</c>. Used for discrete imperatives
    /// that do not have continuous-value semantics (Reset / Reload / PresetApply).
    /// FIFO-ordered.
    /// </summary>
    [Serializable]
    public sealed class SlotCommandPayload
    {
        /// <summary>One of: <c>Reset</c>, <c>Reload</c>, <c>PresetApply</c>.</summary>
        public string Kind { get; init; } = "Reset";
        public string? Argument { get; init; }
    }
}
