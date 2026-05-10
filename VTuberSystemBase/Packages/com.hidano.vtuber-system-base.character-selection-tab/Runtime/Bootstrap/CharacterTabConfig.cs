#nullable enable
using System;

namespace VTuberSystemBase.CharacterSelectionTab.Bootstrap
{
    /// <summary>
    /// Tab-wide configuration with sensible defaults (design.md §Composition Root,
    /// task 1.2). Instances are immutable; the bootstrapper validates each member
    /// and falls back to defaults on out-of-range values (negative or zero).
    /// </summary>
    public sealed class CharacterTabConfig
    {
        public TimeSpan PresetDebounce { get; init; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan AssignmentTimeout { get; init; } = TimeSpan.FromSeconds(5);
        public TimeSpan SchemaRequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
        public TimeSpan InteractionIdleThreshold { get; init; } = TimeSpan.FromMilliseconds(200);
        public string PresetScopeId { get; init; } = "tab:character";
        public string DefaultThumbnailAddressableKey { get; init; }
            = "vtuber-system-base/character/default-avatar-thumbnail";

        public static CharacterTabConfig Default { get; } = new CharacterTabConfig();
    }
}
