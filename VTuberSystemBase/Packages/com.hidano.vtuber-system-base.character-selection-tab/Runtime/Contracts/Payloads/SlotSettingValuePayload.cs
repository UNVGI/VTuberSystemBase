using System;
using System.Text.Json;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// State payload for <c>slot/{slotId}/settings/{settingKey}</c>. The concrete value
    /// shape carried in <see cref="Value"/> depends on <see cref="Type"/>; see
    /// <see cref="SettingType"/>. The UI is the authority for non-command settings;
    /// command-kind settings use <c>slot/{slotId}/command</c> (event) instead.
    /// </summary>
    [Serializable]
    public sealed class SlotSettingValuePayload
    {
        public string SettingKey { get; init; } = "";
        public SettingType Type { get; init; }
        public JsonElement Value { get; init; }
    }
}
