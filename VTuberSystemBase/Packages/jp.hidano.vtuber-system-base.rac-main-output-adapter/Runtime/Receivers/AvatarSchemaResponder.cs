using System;
using System.Collections.Generic;
using System.Diagnostics;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.RacMainOutputAdapter.Bootstrapper;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;
using Debug = UnityEngine.Debug;

namespace VTuberSystemBase.RacMainOutputAdapter.Receivers
{
    /// <summary>
    /// <c>avatars/{key}/schema</c> request を <see cref="IAvatarSchemaProvider.Resolve"/> 同期実行で応答する Responder
    /// （Requirement 5.1〜5.5）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provider が null / 例外を返した場合は空スキーマフォールバック。経過時間が
    /// <see cref="RacMainOutputAdapterConfig.SchemaProviderSlowThresholdMs"/> を超えたら <c>SchemaProvider.Slow</c> ログを残す。
    /// </para>
    /// <para>
    /// 解決成功時は登録済みの <see cref="SlotSettingsApplier"/> に schema を伝播し、settingKey 単位の動的登録を促す。
    /// </para>
    /// </remarks>
    internal sealed class AvatarSchemaResponder : IDisposable
    {
        private readonly IOutputCommandDispatcher _dispatcher;
        private readonly IAvatarSchemaProvider _schemaProvider;
        private readonly RacMainOutputAdapterConfig _config;
        private readonly SlotSettingsApplier _settingsApplier;
        private readonly IDiagnosticsLogger _logger;

        private readonly Dictionary<string, OutputCommandHandlerRegistration> _registrations = new();
        // 解決済 avatarKey → schema（settingKey 動的登録の重複起動防止）
        private readonly Dictionary<string, AvatarSettingsSchemaPayload> _resolvedSchemas = new();

        /// <summary>本 Responder を生成する。</summary>
        public AvatarSchemaResponder(
            IOutputCommandDispatcher dispatcher,
            IAvatarSchemaProvider schemaProvider,
            RacMainOutputAdapterConfig config,
            SlotSettingsApplier settingsApplier,
            IDiagnosticsLogger logger)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
            _config = config ?? new RacMainOutputAdapterConfig();
            _settingsApplier = settingsApplier ?? throw new ArgumentNullException(nameof(settingsApplier));
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary><paramref name="avatarKey"/> 用の動的 request ハンドラを登録する。</summary>
        public void RegisterDynamic(string avatarKey)
        {
            if (string.IsNullOrEmpty(avatarKey)) return;
            if (_registrations.ContainsKey(avatarKey)) return;
            var topic = CharacterTopics.AvatarSchema(avatarKey);
            var reg = _dispatcher.RegisterRequestHandler<AvatarSchemaRequestPayload, AvatarSettingsSchemaPayload>(topic,
                cmd => HandleRequest(cmd.Payload));
            _registrations[avatarKey] = reg;
        }

        /// <summary><paramref name="avatarKey"/> 用の動的 request ハンドラを解除する。</summary>
        public void UnregisterDynamic(string avatarKey)
        {
            if (_registrations.TryGetValue(avatarKey, out var reg))
            {
                reg.Dispose();
                _registrations.Remove(avatarKey);
            }
            _resolvedSchemas.Remove(avatarKey);
        }

        /// <summary>
        /// 既知の slot がアバターを assigned した時点で、対応する schema が解決済みなら settingKey 動的登録を促す。
        /// 未解決の場合は次の request 受信時に解決される。
        /// </summary>
        public void NotifySlotActiveForAvatar(string slotId, string avatarKey)
        {
            if (string.IsNullOrEmpty(slotId) || string.IsNullOrEmpty(avatarKey)) return;
            if (_resolvedSchemas.TryGetValue(avatarKey, out var schema) && schema != null)
            {
                _settingsApplier.OnSchemaResolved(slotId, avatarKey, schema);
            }
        }

        private AvatarSettingsSchemaPayload HandleRequest(AvatarSchemaRequestPayload payload)
        {
            var avatarKey = payload?.AvatarKey ?? string.Empty;
            var sw = Stopwatch.StartNew();
            AvatarSettingsSchemaPayload result = null;
            try
            {
                result = _schemaProvider.Resolve(avatarKey);
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.SchemaProvider,
                    $"Failed avatarKey={avatarKey}", ex);
                result = null;
            }
            finally
            {
                sw.Stop();
            }

            if (sw.ElapsedMilliseconds > _config.SchemaProviderSlowThresholdMs)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.SchemaProvider,
                    $"Slow avatarKey={avatarKey} elapsedMs={sw.ElapsedMilliseconds}");
            }

            // null フォールバック
            if (result == null)
            {
                _logger.Log(AdapterLogLevel.Info, AdapterLogCategories.SchemaProvider,
                    $"Fallback avatarKey={avatarKey} elapsedMs={sw.ElapsedMilliseconds}");
                result = new AvatarSettingsSchemaPayload
                {
                    AvatarKey = avatarKey,
                    Settings = Array.Empty<SettingSchemaEntry>(),
                };
            }
            else
            {
                _logger.Log(AdapterLogLevel.Debug, AdapterLogCategories.SchemaProvider,
                    $"Resolved avatarKey={avatarKey} entries={result.Settings?.Count ?? 0} elapsedMs={sw.ElapsedMilliseconds}");
            }

            // 解決済 schema をキャッシュし、SettingsApplier の動的登録に流す。
            _resolvedSchemas[avatarKey] = result;
            // 現状 slotId は未知のため、SlotAssignmentApplier 側で OnAvatarKeyChanged → NotifySlotActiveForAvatar を呼ぶ経路に委譲。
            return result;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var kv in _registrations) kv.Value.Dispose();
            _registrations.Clear();
            _resolvedSchemas.Clear();
        }
    }
}
