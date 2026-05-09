using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RealtimeAvatarController.Core;
using RealtimeAvatarController.Avatar.Builtin;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Defaults
{
    /// <summary>
    /// <see cref="IAvatarKeyResolver"/> の既定実装。Unity Addressables の <c>{avatarKey}</c> アドレスから
    /// <see cref="GameObject"/> Prefab を解決し、<see cref="BuiltinAvatarProviderConfig"/> を動的生成して
    /// <see cref="AvatarProviderDescriptor"/> を返す（Requirement 8.1）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// アバター列挙は「label = `avatar`」で登録された Addressables エントリを <see cref="Addressables.LoadResourceLocationsAsync(object, Type)"/>
    /// で列挙する戦略。利用者プロジェクトはアバター Prefab に <c>avatar</c> ラベルを付けることで本 Resolver の対象とする。
    /// </para>
    /// <para>
    /// Addressables の同期化（<c>WaitForCompletion</c>）はメインスレッドをブロックするため、
    /// 多数のアバターを扱う場合は利用者プロジェクトが <see cref="IAvatarKeyResolver"/> を差し替えて
    /// 事前ロード戦略を採用することを README で促す（design.md §Performance）。
    /// </para>
    /// </remarks>
    public sealed class AddressablesAvatarKeyResolver : IAvatarKeyResolver
    {
        /// <summary>本 Resolver が列挙対象とする Addressables ラベル。</summary>
        public const string AvatarLabel = "avatar";

        private readonly IDiagnosticsLogger _logger;
        private readonly Dictionary<string, AvatarCatalogEntry> _entries = new();

        /// <summary><see cref="IAvatarKeyResolver"/> の既定実装を生成する。</summary>
        public AddressablesAvatarKeyResolver(IDiagnosticsLogger logger = null)
        {
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

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

            GameObject prefab = null;
            try
            {
                var op = Addressables.LoadAssetAsync<GameObject>(avatarKey);
                prefab = op.WaitForCompletion();
                if (op.Status != AsyncOperationStatus.Succeeded || prefab == null)
                {
                    _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Adapter,
                        $"Resolve('{avatarKey}') failed via Addressables (status={op.Status}).");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Adapter,
                    $"Resolve('{avatarKey}') threw: {ex.GetType().Name}", ex);
                return null;
            }

            var config = ScriptableObject.CreateInstance<BuiltinAvatarProviderConfig>();
            config.name = $"BuiltinAvatarProviderConfig_{avatarKey}";
            config.avatarPrefab = prefab;
            return new AvatarProviderDescriptor
            {
                ProviderTypeId = BuiltinAvatarProviderFactory.BuiltinProviderTypeId,
                Config = config,
            };
        }

        /// <inheritdoc/>
        public async UniTask Refresh()
        {
            // Addressables ラベル "avatar" のリソース位置を列挙する。
            // 失敗しても例外を伝播させず、AvatarKeys を空にして OnAvatarKeysChanged を発火するに留める。
            var newEntries = new Dictionary<string, AvatarCatalogEntry>();
            try
            {
                var locOp = Addressables.LoadResourceLocationsAsync(AvatarLabel, typeof(GameObject));
                var locations = await locOp.ToUniTask();
                if (locations != null)
                {
                    foreach (var loc in locations)
                    {
                        var key = loc.PrimaryKey ?? string.Empty;
                        if (string.IsNullOrEmpty(key)) continue;
                        if (newEntries.ContainsKey(key)) continue;
                        newEntries[key] = new AvatarCatalogEntry
                        {
                            AvatarKey = key,
                            DisplayName = key,
                        };
                    }
                }
                Addressables.Release(locOp);
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Adapter,
                    "Addressables enumeration failed during Refresh().", ex);
            }

            // 差分検出。順序は無視し、キー集合の差を取る。
            bool changed = newEntries.Count != _entries.Count;
            if (!changed)
            {
                foreach (var kv in newEntries)
                {
                    if (!_entries.ContainsKey(kv.Key)) { changed = true; break; }
                }
            }

            _entries.Clear();
            foreach (var kv in newEntries) _entries[kv.Key] = kv.Value;

            if (changed) OnAvatarKeysChanged?.Invoke();
        }
    }
}
