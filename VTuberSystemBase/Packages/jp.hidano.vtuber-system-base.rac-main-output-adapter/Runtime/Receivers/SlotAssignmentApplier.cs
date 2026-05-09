using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RealtimeAvatarController.Core;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.Domain;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;
using VTuberSystemBase.RacMainOutputAdapter.Senders;

namespace VTuberSystemBase.RacMainOutputAdapter.Receivers
{
    /// <summary>
    /// <c>slot/{id}/assignment</c> state を受信し、RAC <see cref="SlotManager.AddSlotAsync"/> /
    /// <see cref="SlotManager.RemoveSlotAsync"/> に翻訳する Applier（Requirement 2.1〜2.9, 4.3 Reload 委譲）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 同一 <c>slotId</c> への並行 assignment は <see cref="SemaphoreSlim"/> で直列化し、差替（別 AvatarKey 受信）は
    /// <c>RemoveSlotAsync → AddSlotAsync</c> の連続実行とする（RA-5）。
    /// </para>
    /// <para>
    /// アバター差替時の保留 settings バッファ破棄は <see cref="SlotSettingsApplier.OnAvatarKeyChanged"/> 経由で通知。
    /// </para>
    /// </remarks>
    internal sealed class SlotAssignmentApplier : IDisposable
    {
        private readonly IOutputCommandDispatcher _dispatcher;
        private readonly SlotManager _slotManager;
        private readonly IAvatarKeyResolver _keyResolver;
        private readonly IMoCapSourceConfigFactory _mocapFactory;
        private readonly SlotStatusPublisher _statusPublisher;
        private readonly SlotErrorTranslator _errorTranslator;
        private readonly IDiagnosticsLogger _logger;

        private readonly Dictionary<string, OutputCommandHandlerRegistration> _registrations = new();
        private readonly Dictionary<string, SemaphoreSlim> _semaphores = new();
        private readonly Dictionary<string, string> _currentAvatarKeys = new();

        /// <summary>Slot 上のアバターキー変化通知（slotId, oldAvatarKey, newAvatarKey）。<see cref="SlotSettingsApplier"/> が購読する。</summary>
        public event Action<string, string, string> OnAvatarKeyChanged;

        /// <summary>本 Applier を生成する。</summary>
        public SlotAssignmentApplier(
            IOutputCommandDispatcher dispatcher,
            SlotManager slotManager,
            IAvatarKeyResolver keyResolver,
            IMoCapSourceConfigFactory mocapFactory,
            SlotStatusPublisher statusPublisher,
            SlotErrorTranslator errorTranslator,
            IDiagnosticsLogger logger)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _slotManager = slotManager ?? throw new ArgumentNullException(nameof(slotManager));
            _keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
            _mocapFactory = mocapFactory ?? throw new ArgumentNullException(nameof(mocapFactory));
            _statusPublisher = statusPublisher ?? throw new ArgumentNullException(nameof(statusPublisher));
            _errorTranslator = errorTranslator ?? throw new ArgumentNullException(nameof(errorTranslator));
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary>
        /// <paramref name="slotId"/> 用の動的ハンドラを登録する。catalog 受信時に新規 Slot に対して呼ばれる想定。
        /// </summary>
        public void RegisterDynamic(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return;
            if (_registrations.ContainsKey(slotId))
            {
                _logger.Log(AdapterLogLevel.Debug, AdapterLogCategories.Assignment,
                    $"RegisterDynamic skipped (already registered) slot={slotId}");
                return;
            }
            var topic = CharacterTopics.SlotAssignment(slotId);
            var reg = _dispatcher.RegisterStateHandler<SlotAssignmentPayload>(topic,
                cmd => HandleStateAsync(slotId, cmd.Payload).Forget());
            _registrations[slotId] = reg;
            _semaphores[slotId] = new SemaphoreSlim(1, 1);
        }

        /// <summary><paramref name="slotId"/> 用の動的ハンドラを解除する。</summary>
        public void UnregisterDynamic(string slotId)
        {
            if (_registrations.TryGetValue(slotId, out var reg))
            {
                reg.Dispose();
                _registrations.Remove(slotId);
            }
            if (_semaphores.TryGetValue(slotId, out var sem))
            {
                sem.Dispose();
                _semaphores.Remove(slotId);
            }
            _currentAvatarKeys.Remove(slotId);
        }

        /// <summary>
        /// <see cref="SlotCommandApplier"/> から呼ばれる Reload 委譲。現在の AvatarKey を保持したまま Remove → Add する。
        /// </summary>
        public async UniTask ReloadAsync(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return;
            if (!_currentAvatarKeys.TryGetValue(slotId, out var avatarKey) || string.IsNullOrEmpty(avatarKey))
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Command,
                    $"Reload no-op (slot is empty) slot={slotId}");
                return;
            }
            await HandleAssignment(slotId, avatarKey, isReload: true);
        }

        private async UniTask HandleStateAsync(string slotId, SlotAssignmentPayload payload)
        {
            try
            {
                if (payload == null)
                {
                    _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Assignment,
                        $"null payload slot={slotId}");
                    return;
                }
                var newKey = payload.AvatarKey;

                // null AvatarKey: Slot 解除（Empty 化）
                if (newKey == null)
                {
                    await HandleUnassign(slotId);
                    return;
                }

                // 値あり: validate
                if (!AvatarKeyValidator.Validate(newKey))
                {
                    _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.KeyNotFound,
                        $"InvalidAvatarKey: '{newKey}'");
                    return;
                }

                await HandleAssignment(slotId, newKey, isReload: false);
            }
            catch (Exception ex)
            {
                _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.Unknown,
                    $"{ex.GetType().Name}: {ex.Message}");
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Assignment,
                    $"HandleStateAsync threw slot={slotId}", ex);
            }
        }

        private async UniTask HandleUnassign(string slotId)
        {
            if (!_semaphores.TryGetValue(slotId, out var sem)) return;
            await sem.WaitAsync();
            try
            {
                var prevKey = _currentAvatarKeys.TryGetValue(slotId, out var k) ? k : null;
                _currentAvatarKeys.Remove(slotId);

                var handle = _slotManager.GetSlot(slotId);
                if (handle != null && handle.State != SlotState.Disposed)
                {
                    await _slotManager.RemoveSlotAsync(slotId);
                }
                _statusPublisher.Publish(slotId, SlotStateMapper.Empty);

                if (!string.IsNullOrEmpty(prevKey))
                {
                    OnAvatarKeyChanged?.Invoke(slotId, prevKey, null);
                }
                _logger.Log(AdapterLogLevel.Info, AdapterLogCategories.Assignment,
                    $"unassign slot={slotId}");
            }
            finally
            {
                sem.Release();
            }
        }

        private async UniTask HandleAssignment(string slotId, string avatarKey, bool isReload)
        {
            if (!_semaphores.TryGetValue(slotId, out var sem)) return;
            await sem.WaitAsync();
            try
            {
                _statusPublisher.Publish(slotId, SlotStateMapper.Assigning,
                    isReload ? "reload" : null);

                var prevKey = _currentAvatarKeys.TryGetValue(slotId, out var k) ? k : null;
                var existing = _slotManager.GetSlot(slotId);

                // 既に Active な Slot は Remove → Add の直列に変換（RA-5）
                if (existing != null && existing.State != SlotState.Disposed)
                {
                    await _slotManager.RemoveSlotAsync(slotId);
                    if (!string.IsNullOrEmpty(prevKey))
                    {
                        OnAvatarKeyChanged?.Invoke(slotId, prevKey, null);
                    }
                }

                // AvatarKey 解決
                var providerDescriptor = _keyResolver.Resolve(avatarKey);
                if (providerDescriptor == null)
                {
                    _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.KeyNotFound, avatarKey);
                    return;
                }

                // SlotSettings 構築
                var settings = ScriptableObject.CreateInstance<SlotSettings>();
                settings.name = $"SlotSettings_{slotId}";
                settings.slotId = slotId;
                settings.displayName = slotId;
                settings.weight = 1.0f;
                settings.avatarProviderDescriptor = providerDescriptor;
                settings.moCapSourceDescriptor = _mocapFactory.Build(slotId);

                _currentAvatarKeys[slotId] = avatarKey;
                try
                {
                    await _slotManager.AddSlotAsync(settings);
                }
                catch (Exception ex)
                {
                    // AddSlotAsync 内部の例外は通常 ISlotErrorChannel 経由で SlotErrorTranslator が拾うが、
                    // 同期 throw された場合のフェイルセーフ。
                    _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.Map(SlotErrorCategory.InitFailure, ex),
                        $"{ex.GetType().Name}: {ex.Message}");
                    _currentAvatarKeys.Remove(slotId);
                    return;
                }

                // AddSlotAsync 後、Slot が Active なら Assigned、そうでなければ何もしない
                // （Disposed の場合は ISlotErrorChannel が既にエラーを発行している）。
                var post = _slotManager.GetSlot(slotId);
                if (post != null && post.State == SlotState.Active)
                {
                    _statusPublisher.Publish(slotId, SlotStateMapper.Assigned);
                    OnAvatarKeyChanged?.Invoke(slotId, prevKey, avatarKey);
                    _logger.Log(AdapterLogLevel.Info, AdapterLogCategories.Assignment,
                        $"assigned slot={slotId} avatar={avatarKey} reload={isReload}");
                }
                else
                {
                    // Init 失敗で Disposed に遷移済み。currentAvatarKeys を巻き戻す。
                    _currentAvatarKeys.Remove(slotId);
                }
            }
            finally
            {
                sem.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var kv in _registrations) kv.Value.Dispose();
            _registrations.Clear();
            foreach (var kv in _semaphores) kv.Value.Dispose();
            _semaphores.Clear();
            _currentAvatarKeys.Clear();
        }
    }
}
