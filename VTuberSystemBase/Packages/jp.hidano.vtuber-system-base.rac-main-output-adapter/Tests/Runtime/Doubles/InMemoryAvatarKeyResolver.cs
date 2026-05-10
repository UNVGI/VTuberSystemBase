using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RealtimeAvatarController.Core;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// <see cref="IAvatarKeyResolver"/> のメモリ実装。事前登録された辞書から <see cref="AvatarProviderDescriptor"/> を返す。
    /// テストから <see cref="SetEntries"/> で内容を入れ替え、<see cref="OnAvatarKeysChanged"/> を発火させる。
    /// </summary>
    public sealed class InMemoryAvatarKeyResolver : IAvatarKeyResolver
    {
        private readonly Dictionary<string, AvatarProviderDescriptor> _descriptors = new();
        private readonly Dictionary<string, AvatarCatalogEntry> _entries = new();

        /// <inheritdoc/>
        public IReadOnlyList<AvatarCatalogEntry> AvatarKeys
        {
            get
            {
                var list = new List<AvatarCatalogEntry>(_entries.Count);
                foreach (var kv in _entries) list.Add(kv.Value);
                return list;
            }
        }

        /// <inheritdoc/>
        public event Action OnAvatarKeysChanged;

        /// <inheritdoc/>
        public AvatarProviderDescriptor Resolve(string avatarKey)
        {
            if (string.IsNullOrEmpty(avatarKey)) return null;
            return _descriptors.TryGetValue(avatarKey, out var d) ? d : null;
        }

        /// <inheritdoc/>
        public UniTask Refresh() => UniTask.CompletedTask;

        /// <summary>
        /// テスト用に事前登録内容を入れ替え、<see cref="OnAvatarKeysChanged"/> を発火する。
        /// </summary>
        public void SetEntries(IDictionary<string, AvatarProviderDescriptor> descriptors,
            IDictionary<string, string> displayNames = null)
        {
            _descriptors.Clear();
            _entries.Clear();
            if (descriptors != null)
            {
                foreach (var kv in descriptors)
                {
                    _descriptors[kv.Key] = kv.Value;
                    var displayName = (displayNames != null && displayNames.TryGetValue(kv.Key, out var dn)) ? dn : kv.Key;
                    _entries[kv.Key] = new AvatarCatalogEntry { AvatarKey = kv.Key, DisplayName = displayName };
                }
            }
            OnAvatarKeysChanged?.Invoke();
        }
    }
}
