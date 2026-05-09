using System;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// Request payload for <c>avatars/{avatarKey}/schema</c>. UI requests the metadata
    /// shape for the given avatar; main output side responds with
    /// <see cref="AvatarSettingsSchemaPayload"/>.
    /// </summary>
    [Serializable]
    public sealed class AvatarSchemaRequestPayload
    {
        public string AvatarKey { get; init; } = "";
    }
}
