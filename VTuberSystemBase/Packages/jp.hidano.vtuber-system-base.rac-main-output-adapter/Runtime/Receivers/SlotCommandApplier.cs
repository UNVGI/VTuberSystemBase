using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RealtimeAvatarController.Core;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.Domain;
using VTuberSystemBase.RacMainOutputAdapter.Senders;

namespace VTuberSystemBase.RacMainOutputAdapter.Receivers
{
    /// <summary>
    /// <c>slot/{id}/command</c> event を Reset / Reload / PresetApply に分岐して処理する Applier
    /// （Requirement 4.1〜4.7）。
    /// </summary>
    internal sealed class SlotCommandApplier : IDisposable
    {
        private readonly IOutputCommandDispatcher _dispatcher;
        private readonly SlotManager _slotManager;
        private readonly SlotAssignmentApplier _assignmentApplier;
        private readonly SlotStatusPublisher _statusPublisher;
        private readonly SlotErrorTranslator _errorTranslator;
        private readonly IDiagnosticsLogger _logger;

        private readonly Dictionary<string, OutputCommandHandlerRegistration> _registrations = new();

        /// <summary>本 Applier を生成する。</summary>
        public SlotCommandApplier(
            IOutputCommandDispatcher dispatcher,
            SlotManager slotManager,
            SlotAssignmentApplier assignmentApplier,
            SlotStatusPublisher statusPublisher,
            SlotErrorTranslator errorTranslator,
            IDiagnosticsLogger logger)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _slotManager = slotManager ?? throw new ArgumentNullException(nameof(slotManager));
            _assignmentApplier = assignmentApplier ?? throw new ArgumentNullException(nameof(assignmentApplier));
            _statusPublisher = statusPublisher ?? throw new ArgumentNullException(nameof(statusPublisher));
            _errorTranslator = errorTranslator ?? throw new ArgumentNullException(nameof(errorTranslator));
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary><paramref name="slotId"/> 用の動的ハンドラを登録する。</summary>
        public void RegisterDynamic(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return;
            if (_registrations.ContainsKey(slotId)) return;
            var topic = CharacterTopics.SlotCommand(slotId);
            var reg = _dispatcher.RegisterEventHandler<SlotCommandPayload>(topic,
                cmd => HandleEvent(slotId, cmd.Payload));
            _registrations[slotId] = reg;
        }

        /// <summary><paramref name="slotId"/> 用の動的ハンドラを解除する。</summary>
        public void UnregisterDynamic(string slotId)
        {
            if (_registrations.TryGetValue(slotId, out var reg))
            {
                reg.Dispose();
                _registrations.Remove(slotId);
            }
        }

        private void HandleEvent(string slotId, SlotCommandPayload payload)
        {
            try
            {
                if (payload == null)
                {
                    _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Command,
                        $"null payload slot={slotId}");
                    return;
                }
                var kind = payload.Kind ?? "Reset";
                var argument = payload.Argument;
                _logger.Log(AdapterLogLevel.Info, AdapterLogCategories.Command,
                    $"command slot={slotId} kind={kind} argument={argument}");

                switch (kind)
                {
                    case "Reset":
                        ResetAsync(slotId).Forget();
                        break;
                    case "Reload":
                        _assignmentApplier.ReloadAsync(slotId).Forget();
                        break;
                    case "PresetApply":
                        // Requirement 4.4: 情報ログのみ no-op
                        _logger.Log(AdapterLogLevel.Info, AdapterLogCategories.Command,
                            $"PresetApply received but no-op slot={slotId} argument={argument}");
                        break;
                    default:
                        _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Command,
                            $"unknown kind, skipped slot={slotId} kind={kind}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.Unknown,
                    $"command kind={(payload?.Kind ?? "(null)")}: {ex.GetType().Name}: {ex.Message}");
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Command,
                    $"command handler threw slot={slotId}", ex);
            }
        }

        private async UniTask ResetAsync(string slotId)
        {
            try
            {
                var handle = _slotManager.GetSlot(slotId);
                if (handle != null && handle.State != SlotState.Disposed)
                {
                    await _slotManager.RemoveSlotAsync(slotId);
                }
                _statusPublisher.Publish(slotId, SlotStateMapper.Empty);
            }
            catch (Exception ex)
            {
                _errorTranslator.PublishError(slotId, SlotErrorCodeMapper.Unknown,
                    $"Reset: {ex.GetType().Name}: {ex.Message}");
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Command,
                    $"Reset threw slot={slotId}", ex);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var kv in _registrations) kv.Value.Dispose();
            _registrations.Clear();
        }
    }
}
