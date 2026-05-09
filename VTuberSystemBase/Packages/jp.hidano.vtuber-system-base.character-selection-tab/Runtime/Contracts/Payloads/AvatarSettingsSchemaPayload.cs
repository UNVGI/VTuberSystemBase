using System;
using System.Collections.Generic;
using System.Text.Json;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// Response payload for <c>avatars/{avatarKey}/schema</c>. Describes the per-avatar
    /// settings UI to be generated dynamically by the tab. Forward-compatible: unknown
    /// fields and unknown <see cref="SettingSchemaEntry.Kind"/> values MUST be skipped
    /// with a diagnostic log.
    /// </summary>
    [Serializable]
    public sealed class AvatarSettingsSchemaPayload
    {
        public string AvatarKey { get; init; } = "";
        public IReadOnlyList<SettingSchemaEntry> Settings { get; init; } = Array.Empty<SettingSchemaEntry>();
    }

    [Serializable]
    public sealed class SettingSchemaEntry
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public SettingType Type { get; init; }
        public JsonElement? Default { get; init; }
        public JsonElement? Min { get; init; }
        public JsonElement? Max { get; init; }
        public string? Unit { get; init; }
        public IReadOnlyList<string>? Options { get; init; }
        /// <summary>If set to <c>"command"</c>, the entry is rendered as a discrete button
        /// and exchanged via the slot/command event topic instead of a state topic.</summary>
        public string? Kind { get; init; }
        public float? Step { get; init; }
    }
}
