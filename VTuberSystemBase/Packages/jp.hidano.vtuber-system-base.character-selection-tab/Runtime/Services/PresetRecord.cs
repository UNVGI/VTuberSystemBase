#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    public sealed class PresetHeader
    {
        public string PresetId { get; init; } = "";
        public string Name { get; init; } = "";
        public DateTimeOffset LastModifiedAt { get; init; }
    }

    public sealed class PresetRecord
    {
        public PresetHeader Header { get; init; } = new PresetHeader();
        public IReadOnlyDictionary<string, string?> Assignments { get; init; } = new Dictionary<string, string?>();
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, SettingValue>> Settings { get; init; }
            = new Dictionary<string, IReadOnlyDictionary<string, SettingValue>>();
    }
}
