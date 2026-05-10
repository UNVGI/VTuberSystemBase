using System;
using System.Collections.Generic;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// State payload for <c>slots/catalog</c>. Published by the main output side to
    /// inform the UI tab of the current MoCap slot lineup. Coalesced (last-write-wins).
    /// </summary>
    [Serializable]
    public sealed class SlotCatalogPayload
    {
        public IReadOnlyList<SlotCatalogEntry> Slots { get; init; } = Array.Empty<SlotCatalogEntry>();
    }

    [Serializable]
    public sealed class SlotCatalogEntry
    {
        public string SlotId { get; init; } = "";
        public string? DisplayName { get; init; }
        public int OrderHint { get; init; }
    }
}
