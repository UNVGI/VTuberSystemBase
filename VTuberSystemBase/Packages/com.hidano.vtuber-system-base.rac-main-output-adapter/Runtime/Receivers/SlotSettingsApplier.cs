using System;
using System.Collections.Generic;
using System.Diagnostics;
using RealtimeAvatarController.Core;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.Domain;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;
using VTuberSystemBase.RacMainOutputAdapter.Senders;
using Debug = UnityEngine.Debug;

namespace VTuberSystemBase.RacMainOutputAdapter.Receivers
{
    /// <summary>
    /// <c>slot/{id}/settings/{key}</c> state を <see cref="IAvatarSettingsAdapter.Apply"/> に翻訳する Applier
    /// （Requirement 3.1〜3.9）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// schema 解決された avatarKey に対して settingKey 単位で動的登録する（Schema が分からないと topic を確定できないため、
    /// <see cref="OnSchemaResolved"/> を契機に登録する設計）。
    /// </para>
    /// <para>
    /// 非 Active Slot に届いた値は <c>(slotId, avatarKey)</c> 単位の保留バッファに last-write-wins で格納し、
    /// Slot が Active になったタイミングで flush。アバター差替時は旧バッファを破棄（RA-6）。
    /// </para>
    /// </remarks>
    internal sealed class SlotSettingsApplier : IDisposable
    {
        private readonly IOutputCommandDispatcher _dispatcher;
        private readonly SlotManager _slotManager;
        private readonly IAvatarSettingsAdapter _settingsAdapter;
        private readonly SlotErrorTranslator _errorTranslator;
        private readonly IDiagnosticsLogger _logger;

        // (slotId, avatarKey, settingKey) → Registration
        private readonly Dictionary<(string slotId, string avatarKey, string settingKey), OutputCommandHandlerRegistration>
            _registrations = new();

        // (slotId, avatarKey) → settingKey → PendingSettingValue
        private readonly Dictionary<(string slotId, string avatarKey), Dictionary<string, PendingSettingValue>>
            _pending = new();

        /// <summary>本 Applier を生成する。</summary>
        public SlotSettingsApplier(
            IOutputCommandDispatcher dispatcher,
            SlotManager slotManager,
            IAvatarSettingsAdapter settingsAdapter,
            SlotErrorTranslator errorTranslator,
            IDiagnosticsLogger logger)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _slotManager = slotManager ?? throw new ArgumentNullException(nameof(slotManager));
            _settingsAdapter = settingsAdapter ?? throw new ArgumentNullException(nameof(settingsAdapter));
            _errorTranslator = errorTranslator ?? throw new ArgumentNullException(nameof(errorTranslator));
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary>
        /// schema が解決されたとき、対応する <paramref name="slotId"/> × <paramref name="avatarKey"/> の各 settingKey に対する
        /// 動的ハンドラ登録を行う。
        /// </summary>
        public void OnSchemaResolved(string slotId, string avatarKey, AvatarSettingsSchemaPayload schema)
        {
            if (string.IsNullOrEmpty(slotId) || string.IsNullOrEmpty(avatarKey) || schema?.Settings == null) return;
            foreach (var entry in schema.Settings)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;
                // command kind は state ハンドラを登録しない
                if (string.Equals(entry.Kind, "command", StringComparison.Ordinal)) continue;
                RegisterSetting(slotId, avatarKey, entry.Key);
            }
        }

        /// <summary>
        /// <see cref="SlotCatalogPublisher"/> 由来の Slot 状態変化をフックして、保留バッファ flush / 破棄を実行する。
        /// </summary>
        public void OnSlotStateChanged(string slotId, SlotState previous, SlotState next, string avatarKey)
        {
            if (next == SlotState.Active)
            {
                FlushPending(slotId, avatarKey);
            }
            else if (next == SlotState.Disposed)
            {
                if (!string.IsNullOrEmpty(avatarKey)) DropPending(slotId, avatarKey);
            }
        }

        /// <summary>
        /// アバター差替時に旧 (slotId, oldAvatarKey) のバッファを破棄し、(slotId, newAvatarKey) のバッファを新規開始する。
        /// </summary>
        public void OnAvatarKeyChanged(string slotId, string oldAvatarKey, string newAvatarKey)
        {
            if (!string.IsNullOrEmpty(oldAvatarKey)) DropPending(slotId, oldAvatarKey);
            // newAvatarKey 用バッファは初回 settings 受信時に lazy 生成するので、ここでは何もしない。
            // ハンドラ登録自体は OnSchemaResolved で更新される。
        }

        private void RegisterSetting(string slotId, string avatarKey, string settingKey)
        {
            var key = (slotId, avatarKey, settingKey);
            if (_registrations.ContainsKey(key)) return;

            var topic = CharacterTopics.SlotSettingValue(slotId, settingKey);
            var reg = _dispatcher.RegisterStateHandler<SlotSettingValuePayload>(topic,
                cmd => HandleState(slotId, avatarKey, cmd.Payload));
            _registrations[key] = reg;
        }

        private void HandleState(string slotId, string expectedAvatarKey, SlotSettingValuePayload payload)
        {
            if (payload == null) return;
            try
            {
                var settingKey = string.IsNullOrEmpty(payload.SettingKey) ? "" : payload.SettingKey;
                var receivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var pending = new PendingSettingValue(settingKey, payload.Type, payload.Value, receivedAt);

                if (_slotManager.TryGetSlotResources(slotId, out _, out var avatar) && avatar != null)
                {
                    ApplyOne(slotId, expectedAvatarKey, settingKey, payload.Type, payload.Value, avatar);
                }
                else
                {
                    // 非 Active: 保留
                    var buf = GetOrCreatePending(slotId, expectedAvatarKey);
                    buf[settingKey] = pending;
                    _logger.Log(AdapterLogLevel.Debug, AdapterLogCategories.Settings,
                        $"pending slot={slotId} key={settingKey} type={payload.Type}");
                }
            }
            catch (Exception ex)
            {
                _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.ApplyFailed,
                    $"{ex.GetType().Name}: {ex.Message}");
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Settings,
                    $"settings handler threw slot={slotId}", ex);
            }
        }

        private void ApplyOne(string slotId, string avatarKey, string settingKey, SettingType type,
            System.Text.Json.JsonElement rawValue, UnityEngine.GameObject avatar)
        {
            // SettingType 未知値は前方互換でスキップ（Requirement 3.5）
            object decoded;
            try
            {
                decoded = SettingValueDecoder.Decode(type, rawValue);
            }
            catch (InvalidOperationException ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Settings,
                    $"decode failed slot={slotId} key={settingKey} type={type}: {ex.Message}");
                return;
            }
            if (decoded == null && !IsKnownType(type))
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Settings,
                    $"unknown SettingType, skipped slot={slotId} key={settingKey} type={type}");
                return;
            }

            // 適用
            AdapterApplyResult result;
            try
            {
                result = _settingsAdapter.Apply(avatar, settingKey, type, rawValue);
            }
            catch (Exception ex)
            {
                _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.ApplyFailed,
                    $"{ex.GetType().Name}: {ex.Message}");
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Settings,
                    $"adapter Apply threw slot={slotId} key={settingKey}", ex);
                return;
            }

            switch (result)
            {
                case AdapterApplyResult.Applied:
                    _logger.Log(AdapterLogLevel.Trace, AdapterLogCategories.Settings,
                        $"applied slot={slotId} key={settingKey} type={type}");
                    break;
                case AdapterApplyResult.UnknownKey:
                    _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Adapter,
                        $"UnknownKey slot={slotId} avatar={avatarKey} key={settingKey} type={type}");
                    break;
                case AdapterApplyResult.OutOfRange:
                    _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Adapter,
                        $"OutOfRange slot={slotId} key={settingKey} type={type}");
                    break;
                case AdapterApplyResult.Failed:
                    _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.ApplyFailed,
                        $"AdapterApplyResult.Failed key={settingKey}");
                    break;
            }
        }

        private void FlushPending(string slotId, string avatarKey)
        {
            if (string.IsNullOrEmpty(avatarKey)) return;
            if (!_pending.TryGetValue((slotId, avatarKey), out var buf) || buf.Count == 0) return;

            if (!_slotManager.TryGetSlotResources(slotId, out _, out var avatar) || avatar == null)
            {
                return;
            }

            foreach (var kv in buf)
            {
                ApplyOne(slotId, avatarKey, kv.Key, kv.Value.Type, kv.Value.Value, avatar);
            }
            buf.Clear();
        }

        private void DropPending(string slotId, string avatarKey)
        {
            _pending.Remove((slotId, avatarKey));
            // 関連 Registration の解除
            var keysToRemove = new List<(string, string, string)>();
            foreach (var kv in _registrations)
            {
                if (kv.Key.slotId == slotId && kv.Key.avatarKey == avatarKey)
                    keysToRemove.Add(kv.Key);
            }
            foreach (var k in keysToRemove)
            {
                _registrations[k].Dispose();
                _registrations.Remove(k);
            }
        }

        private Dictionary<string, PendingSettingValue> GetOrCreatePending(string slotId, string avatarKey)
        {
            var key = (slotId, avatarKey);
            if (!_pending.TryGetValue(key, out var buf))
            {
                buf = new Dictionary<string, PendingSettingValue>();
                _pending[key] = buf;
            }
            return buf;
        }

        private static bool IsKnownType(SettingType type)
        {
            return type == SettingType.Float
                || type == SettingType.Int
                || type == SettingType.Bool
                || type == SettingType.Color
                || type == SettingType.Enum
                || type == SettingType.Vector3;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var kv in _registrations) kv.Value.Dispose();
            _registrations.Clear();
            _pending.Clear();
        }
    }
}
