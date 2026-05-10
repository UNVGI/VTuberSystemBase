using System;
using System.Collections.Generic;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// <see cref="IAvatarSchemaProvider"/> のメモリ実装。事前登録された辞書から
    /// <see cref="AvatarSettingsSchemaPayload"/> を返す。テストから <see cref="ThrowOnKey"/> で例外シミュレートも可能。
    /// </summary>
    public sealed class InMemoryAvatarSchemaProvider : IAvatarSchemaProvider
    {
        private readonly Dictionary<string, AvatarSettingsSchemaPayload> _schemas = new();
        private readonly HashSet<string> _throwOnKeys = new();

        /// <summary>事前登録した avatarKey に対する Resolve 呼出で例外を投げる。</summary>
        public void ThrowOnKey(string avatarKey) => _throwOnKeys.Add(avatarKey);

        /// <summary>事前登録 schema をセットする。</summary>
        public void SetSchema(string avatarKey, AvatarSettingsSchemaPayload payload) => _schemas[avatarKey] = payload;

        /// <inheritdoc/>
        public AvatarSettingsSchemaPayload Resolve(string avatarKey)
        {
            if (_throwOnKeys.Contains(avatarKey))
                throw new InvalidOperationException($"InMemoryAvatarSchemaProvider configured to throw for '{avatarKey}'.");
            return _schemas.TryGetValue(avatarKey, out var p) ? p : null;
        }
    }
}
