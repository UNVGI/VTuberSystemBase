#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.CharacterSelectionTab.State
{
    /// <summary>
    /// Single source of truth for the character-selection tab's UI state.
    /// All members must be invoked on the Unity main thread; cross-thread writes
    /// throw <see cref="InvalidOperationException"/>. Mutations fire
    /// <see cref="OnChanged"/> with a minimal <see cref="StateChangeScope"/> so
    /// each Presenter can short-circuit.
    /// (task 2.1, design.md §State §CharacterTabStateStore.)
    /// </summary>
    public interface ICharacterTabStateStore
    {
        SlotSnapshot? GetSlot(string slotId);
        IReadOnlyList<SlotSnapshot> ListSlots();
        IReadOnlyList<AvatarCatalogEntry> AvatarCatalog { get; }
        string? ActivePresetId { get; }
        ConnectionStatusCode ConnectionStatus { get; }
        string? SelectedSlotId { get; }

        void ApplySlotCatalog(SlotCatalogPayload payload);
        void ApplyAvatarCatalog(AvatarCatalogPayload payload);
        void ApplyAssignment(string slotId, string? avatarKey);
        void ApplyStatus(string slotId, SlotStatus status, string? detail);
        void ApplySettingValue(string slotId, string settingKey, SettingValue value, bool isFromRemote);
        void ApplyError(string slotId, SlotErrorPayload error);
        void ClearError(string slotId);

        bool TryBeginInFlight(string slotId, InFlightOperationKind kind, out InFlightToken token);
        bool EndInFlight(InFlightToken token, InFlightOutcome outcome);

        void SetActivePreset(string? presetId);
        void SetConnectionStatus(ConnectionStatusCode status);

        void SetSelectedSlot(string? slotId);
        void FlushBufferedSetting(string slotId, string settingKey);

        event Action<StateChangeScope> OnChanged;
    }
}
