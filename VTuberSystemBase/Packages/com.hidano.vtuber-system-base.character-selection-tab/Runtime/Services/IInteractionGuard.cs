#nullable enable
using System;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Tracks which (slotId, settingKey) pairs the operator is currently editing.
    /// Used by <c>CharacterTabStateStore</c> to suppress remote state echoes that
    /// would otherwise overwrite the operator's in-flight value (Req 5.7).
    /// </summary>
    public interface IInteractionGuard
    {
        bool IsInteracting(string slotId, string settingKey);
        void MarkInteracting(string slotId, string settingKey);
        void EndInteracting(string slotId, string settingKey);
        void Tick(DateTimeOffset now);
        event Action<InteractingChangedEventArgs> OnChanged;
    }

    public readonly struct InteractingChangedEventArgs
    {
        public string SlotId { get; }
        public string SettingKey { get; }
        public bool IsInteracting { get; }

        public InteractingChangedEventArgs(string slotId, string settingKey, bool isInteracting)
        {
            SlotId = slotId;
            SettingKey = settingKey;
            IsInteracting = isInteracting;
        }
    }
}
