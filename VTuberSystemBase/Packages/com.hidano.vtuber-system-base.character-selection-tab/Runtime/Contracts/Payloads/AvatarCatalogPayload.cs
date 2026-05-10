using System;
using System.Collections.Generic;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// State payload for <c>avatars/catalog</c>. Published by the main output side to
    /// expose the available avatar candidates discovered (typically) via Addressables.
    /// </summary>
    [Serializable]
    public sealed class AvatarCatalogPayload
    {
        public IReadOnlyList<AvatarCatalogEntry> Avatars { get; init; } = Array.Empty<AvatarCatalogEntry>();
    }

    [Serializable]
    public sealed class AvatarCatalogEntry
    {
        public string AvatarKey { get; init; } = "";
        public string DisplayName { get; init; } = "";
    }
}
