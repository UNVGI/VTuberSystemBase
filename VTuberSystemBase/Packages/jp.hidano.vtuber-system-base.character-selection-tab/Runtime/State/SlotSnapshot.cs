#nullable enable
using System;
using System.Collections.Generic;

namespace VTuberSystemBase.CharacterSelectionTab.State
{
    /// <summary>
    /// Immutable per-slot UI snapshot. Tasks 1.2, 2.1.
    /// </summary>
    public sealed class SlotSnapshot
    {
        public string SlotId { get; init; } = "";
        public string? AssignedAvatarKey { get; init; }
        public SlotStatus Status { get; init; } = SlotStatus.Empty;
        public string? StatusDetail { get; init; }
        public IReadOnlyDictionary<string, SettingValue> SettingValues { get; init; } = new Dictionary<string, SettingValue>();
        public InFlightOperationKind? InFlight { get; init; }
        public string? DisplayName { get; init; }
    }

    public enum SlotStatus
    {
        Empty = 0,
        Assigning = 1,
        Assigned = 2,
        Error = 3,
    }

    public enum InFlightOperationKind
    {
        Assignment = 0,
        Reset = 1,
        Reload = 2,
        PresetApply = 3,
    }

    /// <summary>
    /// Token returned by <c>TryBeginInFlight</c>; opaque ID prevents stale completions
    /// from overriding a fresh InFlight slot.
    /// </summary>
    public readonly struct InFlightToken : IEquatable<InFlightToken>
    {
        public string SlotId { get; }
        public InFlightOperationKind Kind { get; }
        public Guid Id { get; }

        public InFlightToken(string slotId, InFlightOperationKind kind, Guid id)
        {
            SlotId = slotId;
            Kind = kind;
            Id = id;
        }

        public bool Equals(InFlightToken other) =>
            string.Equals(SlotId, other.SlotId, StringComparison.Ordinal)
            && Kind == other.Kind
            && Id == other.Id;

        public override bool Equals(object? obj) => obj is InFlightToken other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SlotId, (int)Kind, Id);

        public static bool operator ==(InFlightToken a, InFlightToken b) => a.Equals(b);
        public static bool operator !=(InFlightToken a, InFlightToken b) => !a.Equals(b);
    }

    public enum InFlightOutcome
    {
        CompletedOk = 0,
        TimedOut = 1,
        Failed = 2,
        Cancelled = 3,
    }

    /// <summary>
    /// Diff scope flags fired by <c>ICharacterTabStateStore.OnChanged</c>.
    /// Bit field allows multiple scopes to be reported in one event.
    /// </summary>
    [Flags]
    public enum StateChangeScope
    {
        None = 0,
        SlotCatalog = 1 << 0,
        AvatarCatalog = 1 << 1,
        SlotStatus = 1 << 2,
        Assignment = 1 << 3,
        SettingValue = 1 << 4,
        InFlight = 1 << 5,
        Connection = 1 << 6,
        ActivePreset = 1 << 7,
        SlotError = 1 << 8,
    }

    /// <summary>
    /// UI-visible IPC connection status code (mirrors UiToolkitShell.ConnectionStatusCode).
    /// </summary>
    public enum ConnectionStatusCode
    {
        Initializing = 0,
        Connecting = 1,
        Connected = 2,
        Disconnected = 3,
        Reconnecting = 4,
        FailedPermanently = 5,
    }

    /// <summary>
    /// Avatar catalog entry as used by State / Presenters; mirrors
    /// <c>VTuberSystemBase.CharacterSelectionTab.Contracts.AvatarCatalogEntry</c>.
    /// Held as a domain-side value type so Presenters never depend on JSON DTOs.
    /// </summary>
    public sealed class AvatarCatalogEntry : IEquatable<AvatarCatalogEntry>
    {
        public string AvatarKey { get; init; } = "";
        public string DisplayName { get; init; } = "";

        public bool Equals(AvatarCatalogEntry? other) =>
            other is not null
            && string.Equals(AvatarKey, other.AvatarKey, StringComparison.Ordinal)
            && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is AvatarCatalogEntry e && Equals(e);

        public override int GetHashCode() => HashCode.Combine(AvatarKey, DisplayName);
    }

    /// <summary>
    /// Schema entry shape used by services / Presenters; mirrors
    /// <c>VTuberSystemBase.CharacterSelectionTab.Contracts.SettingSchemaEntry</c> with
    /// resolved typed defaults / ranges so the dynamic factory does not have to chase
    /// JsonElement at every call site.
    /// </summary>
    public sealed class SettingSchemaEntry
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public Contracts.SettingType Type { get; init; }
        public SettingValue? Default { get; init; }
        public SettingValue? Min { get; init; }
        public SettingValue? Max { get; init; }
        public string? Unit { get; init; }
        public IReadOnlyList<string>? Options { get; init; }
        public string? Kind { get; init; }
        public float? Step { get; init; }
    }
}
