using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Defaults
{
    /// <summary>
    /// <see cref="IAvatarSchemaProvider"/> の既定実装。Addressables アドレス <c>{avatarKey}.schema</c> から
    /// <see cref="AvatarSchemaScriptableObject"/> を同期取得し、<see cref="AvatarSettingsSchemaPayload"/> を構築する
    /// （Requirement 8.2, 5.6, 5.7）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>WaitForCompletion()</c> がメインスレッドをブロックするため、利用者プロジェクトの SO 設計を軽量に保つことを README で促す。
    /// 同期取得が失敗した場合は <c>null</c> を返し、呼び出し側（<c>AvatarSchemaResponder</c>）は空スキーマフォールバックする。
    /// </para>
    /// </remarks>
    public sealed class AddressablesAvatarSchemaProvider : IAvatarSchemaProvider
    {
        private readonly IDiagnosticsLogger _logger;

        /// <summary>本 Provider を生成する。</summary>
        public AddressablesAvatarSchemaProvider(IDiagnosticsLogger logger = null)
        {
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <inheritdoc/>
        public AvatarSettingsSchemaPayload Resolve(string avatarKey)
        {
            if (string.IsNullOrEmpty(avatarKey)) return null;

            var address = $"{avatarKey}.schema";
            AvatarSchemaScriptableObject so = null;
            AsyncOperationHandle<AvatarSchemaScriptableObject> op = default;
            try
            {
                op = Addressables.LoadAssetAsync<AvatarSchemaScriptableObject>(address);
                so = op.WaitForCompletion();
                if (op.Status != AsyncOperationStatus.Succeeded || so == null)
                {
                    _logger.Log(AdapterLogLevel.Debug, AdapterLogCategories.SchemaProvider,
                        $"Fallback({avatarKey}): no '{address}' resolved (status={op.Status}).");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.SchemaProvider,
                    $"LoadAssetAsync('{address}') threw.", ex);
                return null;
            }

            try
            {
                return BuildPayload(avatarKey, so);
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.SchemaProvider,
                    $"BuildPayload('{avatarKey}') threw.", ex);
                return null;
            }
            finally
            {
                if (op.IsValid()) Addressables.Release(op);
            }
        }

        private static AvatarSettingsSchemaPayload BuildPayload(string avatarKey, AvatarSchemaScriptableObject so)
        {
            var entries = new List<SettingSchemaEntry>(so.entries?.Count ?? 0);
            if (so.entries != null)
            {
                foreach (var e in so.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.key)) continue;
                    entries.Add(new SettingSchemaEntry
                    {
                        Key = e.key,
                        Label = string.IsNullOrEmpty(e.label) ? e.key : e.label,
                        Type = e.type,
                        Default = ParseJson(e.defaultJson),
                        Min = ParseJson(e.minJson),
                        Max = ParseJson(e.maxJson),
                        Unit = string.IsNullOrEmpty(e.unit) ? null : e.unit,
                        Options = (e.options != null && e.options.Count > 0) ? e.options.AsReadOnly() : null,
                        Kind = string.IsNullOrEmpty(e.kind) ? null : e.kind,
                        Step = e.step > 0f ? e.step : (float?)null,
                    });
                }
            }
            return new AvatarSettingsSchemaPayload
            {
                AvatarKey = avatarKey,
                Settings = entries.AsReadOnly(),
            };
        }

        private static JsonElement? ParseJson(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }
    }
}
