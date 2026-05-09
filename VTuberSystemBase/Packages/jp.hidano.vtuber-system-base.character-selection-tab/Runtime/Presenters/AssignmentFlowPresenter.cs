#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Ipc;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Presenters
{
    public enum AssignmentOperation
    {
        Reset,
        Reload,
    }

    /// <summary>
    /// Drives the 2-step Slot ↔ Avatar assignment UX. (task 5.3.) Holds the
    /// "selected slot" intent (set by SlotListPresenter) and converts the
    /// follow-up <see cref="RequestAssignment"/> into an
    /// <c>IUiCommandClient.PublishState</c> with an <see cref="IClock"/>-driven
    /// timeout. Status state replies via the IpcBinder release the in-flight
    /// lock; timeout / failure surface through diagnostic logs and the
    /// <see cref="OnAssignmentFailed"/> callback so the host UI can warn the
    /// operator without crashing the panel.
    /// </summary>
    public sealed class AssignmentFlowPresenter : IDisposable
    {
        private readonly ICharacterTabStateStore _store;
        private readonly ICharacterTabIpcBinder _binder;
        private readonly IClock _clock;
        private readonly TimeSpan _timeout;
        private readonly IDiagnosticsLogger? _log;
        private readonly Dictionary<string, PendingAssignment> _pending =
            new Dictionary<string, PendingAssignment>(StringComparer.Ordinal);
        private bool _disposed;

        /// <summary>Invoked when an in-flight assignment ends (timed out / failed / succeeded).</summary>
        public Action<string, InFlightOutcome, string?>? OnAssignmentFailed { get; set; }

        public AssignmentFlowPresenter(
            ICharacterTabStateStore store,
            ICharacterTabIpcBinder binder,
            IClock clock,
            TimeSpan timeout,
            IDiagnosticsLogger? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            _timeout = timeout;
            _log = logger;
            _store.OnChanged += OnStoreChanged;
            _clock.OnTick += OnClockTick;
        }

        public string? SelectedSlotId => _store.SelectedSlotId;

        public void SelectSlot(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) throw new ArgumentException("slotId required", nameof(slotId));
            if (_store.GetSlot(slotId) is null)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"AssignmentFlow.SelectSlot: unknown slot '{slotId}'.");
                return;
            }
            _store.SetSelectedSlot(slotId);
        }

        public void ClearSelectedSlot()
        {
            _store.SetSelectedSlot(null);
        }

        /// <summary>
        /// Starts an assignment for the currently-selected slot. Suppressed when
        /// no slot is selected, when the avatar key is not in the catalog, or
        /// when the slot already has an in-flight operation.
        /// </summary>
        public SendResult RequestAssignment(string avatarKey)
        {
            if (string.IsNullOrEmpty(avatarKey)) throw new ArgumentException("avatarKey required", nameof(avatarKey));
            var slotId = _store.SelectedSlotId;
            if (string.IsNullOrEmpty(slotId))
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    "AssignmentFlow.RequestAssignment: no slot selected, ignored.");
                return SendResult.Fail(new SendError(SendErrorCode.ShellNotRunning, "no slot selected"));
            }

            // Avatar must be in the catalog (Req 9.2 / design.md Validation).
            bool found = false;
            foreach (var e in _store.AvatarCatalog)
            {
                if (string.Equals(e.AvatarKey, avatarKey, StringComparison.Ordinal)) { found = true; break; }
            }
            if (!found)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"AssignmentFlow.RequestAssignment: avatarKey '{avatarKey}' not in catalog, suppressing send.");
                OnAssignmentFailed?.Invoke(slotId!, InFlightOutcome.Failed, "avatar not in catalog");
                return SendResult.Fail(new SendError(SendErrorCode.TopicInvalid, "avatar not in catalog"));
            }

            if (!_store.TryBeginInFlight(slotId!, InFlightOperationKind.Assignment, out var token))
            {
                _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                    $"AssignmentFlow.RequestAssignment: slot '{slotId}' already has an in-flight op.");
                return SendResult.Fail(new SendError(SendErrorCode.ShellNotRunning, "slot busy"));
            }

            var result = _binder.PublishAssignment(slotId!, avatarKey);
            if (!result.Success)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"Ipc.SendSlotAssignment failed slot={slotId} avatar={avatarKey} error={result.Error?.Code}",
                    new { slotId, avatarKey, error = result.Error?.Code.ToString() });
                _store.EndInFlight(token, InFlightOutcome.Failed);
                OnAssignmentFailed?.Invoke(slotId!, InFlightOutcome.Failed, result.Error?.Detail);
                return result;
            }

            _pending[slotId!] = new PendingAssignment
            {
                Token = token,
                AvatarKey = avatarKey,
                DeadlineAt = _clock.UtcNow + _timeout,
            };
            _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                $"Assign.Start slot={slotId} avatar={avatarKey}");
            return result;
        }

        /// <summary>
        /// Sends a <c>Reset</c> or <c>Reload</c> command for the given slot.
        /// </summary>
        public SendResult RequestOperation(string slotId, AssignmentOperation operation)
        {
            if (string.IsNullOrEmpty(slotId)) throw new ArgumentException("slotId required", nameof(slotId));
            var kind = operation == AssignmentOperation.Reset
                ? InFlightOperationKind.Reset
                : InFlightOperationKind.Reload;
            if (!_store.TryBeginInFlight(slotId, kind, out var token))
            {
                _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                    $"AssignmentFlow.RequestOperation '{operation}': slot '{slotId}' already busy.");
                return SendResult.Fail(new SendError(SendErrorCode.ShellNotRunning, "slot busy"));
            }
            var payload = new SlotCommandPayload { Kind = operation.ToString() };
            var result = _binder.PublishSlotCommand(slotId, payload);
            if (!result.Success)
            {
                _store.EndInFlight(token, InFlightOutcome.Failed);
                OnAssignmentFailed?.Invoke(slotId, InFlightOutcome.Failed, result.Error?.Detail);
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"Ipc.SendSlotCommand failed slot={slotId} kind={operation} error={result.Error?.Code}");
                return result;
            }
            _pending[slotId] = new PendingAssignment
            {
                Token = token,
                DeadlineAt = _clock.UtcNow + _timeout,
            };
            _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                $"Assign.{operation}.Start slot={slotId}");
            return result;
        }

        /// <summary>Async wrapper kept for the IAssignmentFlowPresenter contract in design.md.</summary>
        public Task<SendResult> RequestOperationAsync(string slotId, AssignmentOperation operation)
            => Task.FromResult(RequestOperation(slotId, operation));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _store.OnChanged -= OnStoreChanged;
            _clock.OnTick -= OnClockTick;
            _pending.Clear();
        }

        // ---------- private ----------

        private void OnStoreChanged(StateChangeScope scope)
        {
            // Status update or error completes the in-flight lock.
            if ((scope & (StateChangeScope.SlotStatus | StateChangeScope.SlotError)) == 0) return;
            // Drain any slot whose status reached terminal value.
            var toRelease = new List<string>();
            foreach (var kv in _pending)
            {
                var slot = _store.GetSlot(kv.Key);
                if (slot is null) { toRelease.Add(kv.Key); continue; }
                if (slot.Status == SlotStatus.Assigned || slot.Status == SlotStatus.Empty)
                {
                    if (_store.EndInFlight(kv.Value.Token, InFlightOutcome.CompletedOk))
                    {
                        _log?.Log(LogLevel.Info, LogCategory.TabSpec,
                            $"Assign.Complete slot={kv.Key} status={slot.Status}");
                    }
                    toRelease.Add(kv.Key);
                }
                else if (slot.Status == SlotStatus.Error)
                {
                    if (_store.EndInFlight(kv.Value.Token, InFlightOutcome.Failed))
                    {
                        _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                            $"Assign.Failed slot={kv.Key} detail={slot.StatusDetail}");
                    }
                    OnAssignmentFailed?.Invoke(kv.Key, InFlightOutcome.Failed, slot.StatusDetail);
                    toRelease.Add(kv.Key);
                }
            }
            foreach (var k in toRelease) _pending.Remove(k);
        }

        private void OnClockTick(DateTimeOffset now)
        {
            if (_pending.Count == 0) return;
            var toRelease = new List<string>();
            foreach (var kv in _pending)
            {
                if (now >= kv.Value.DeadlineAt)
                {
                    if (_store.EndInFlight(kv.Value.Token, InFlightOutcome.TimedOut))
                    {
                        _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                            $"Assign.TimedOut slot={kv.Key} avatar={kv.Value.AvatarKey}");
                    }
                    OnAssignmentFailed?.Invoke(kv.Key, InFlightOutcome.TimedOut, "timeout");
                    toRelease.Add(kv.Key);
                }
            }
            foreach (var k in toRelease) _pending.Remove(k);
        }

        private sealed class PendingAssignment
        {
            public InFlightToken Token;
            public string? AvatarKey;
            public DateTimeOffset DeadlineAt;
        }
    }
}
