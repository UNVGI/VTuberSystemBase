using System;
using System.Collections.Generic;
using RealtimeAvatarController.Core;
using UniRx;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.Internal;

namespace VTuberSystemBase.RacMainOutputAdapter.Senders
{
    /// <summary>
    /// <c>slots/catalog</c> の発行と、Slot 増減/状態変化を購読層に通知する責務を担う
    /// （Requirement 6.1 / 6.3 / 6.5 / 6.7）。
    /// </summary>
    internal sealed class SlotCatalogPublisher : IDisposable
    {
        private readonly IAdapterMessageSink _sink;
        private readonly SlotManager _slotManager;
        private readonly PendingPublishQueue _pendingQueue;
        private readonly IDiagnosticsLogger _logger;

        private IDisposable _subscription;
        private readonly HashSet<string> _knownSlotIds = new();

        /// <summary>新規 Slot 追加（<c>Created</c> 観測）。</summary>
        public event Action<string> OnSlotAdded;

        /// <summary>Slot 削除（<c>Disposed</c> 観測）。</summary>
        public event Action<string> OnSlotRemoved;

        /// <summary>Slot 状態遷移（<paramref name="slotId"/>, prev, next, avatarKey）。</summary>
        public event Action<string, SlotState, SlotState, string> OnSlotStateChanged;

        /// <summary>本 publisher を生成する。</summary>
        public SlotCatalogPublisher(
            IAdapterMessageSink sink,
            SlotManager slotManager,
            PendingPublishQueue pendingQueue,
            IDiagnosticsLogger logger)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _slotManager = slotManager ?? throw new ArgumentNullException(nameof(slotManager));
            _pendingQueue = pendingQueue ?? throw new ArgumentNullException(nameof(pendingQueue));
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary><see cref="SlotManager.OnSlotStateChanged"/> 購読開始。</summary>
        public void StartObserving()
        {
            _subscription = _slotManager.OnSlotStateChanged.Subscribe(OnStateChanged);
            // 初回 publish は PendingPublishQueue に委ねる（IPC 受信開始後に flush）。
            _pendingQueue.EnqueueOrExecute(_sink, sink => PublishCatalog(sink));
        }

        /// <summary>現在の SlotManager のスナップショットから <c>slots/catalog</c> を publish する。</summary>
        public void PublishCatalog(IAdapterMessageSink sink = null)
        {
            sink ??= _sink;
            try
            {
                var slots = _slotManager.GetSlots();
                var entries = new List<SlotCatalogEntry>(slots.Count);
                for (int i = 0; i < slots.Count; i++)
                {
                    var h = slots[i];
                    entries.Add(new SlotCatalogEntry
                    {
                        SlotId = h.SlotId,
                        DisplayName = h.DisplayName,
                        OrderHint = i,
                    });
                }
                sink.PublishState(CharacterTopics.SlotsCatalog, new SlotCatalogPayload { Slots = entries });
                _logger.Log(AdapterLogLevel.Debug, AdapterLogCategories.Catalog,
                    $"slots/catalog publish count={entries.Count}");
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Catalog,
                    "slots/catalog publish failed (will retry on next state change).", ex);
            }
        }

        private void OnStateChanged(SlotStateChangedEvent ev)
        {
            try
            {
                var slotId = ev.SlotId;
                var prev = ev.PreviousState;
                var next = ev.NewState;
                // SlotManager.AddSlotAsync は registry 登録（Created）→ TransitionState(Active) を
                // 1 イベント (Created → Active) で発火するため、`next == Created` では検知できない。
                // 「Disposed 以外への遷移で初めて見る slotId」を「追加」、`Disposed` を「削除」と判定する。
                if (next != SlotState.Disposed)
                {
                    if (_knownSlotIds.Add(slotId)) OnSlotAdded?.Invoke(slotId);
                }
                else
                {
                    if (_knownSlotIds.Remove(slotId)) OnSlotRemoved?.Invoke(slotId);
                }

                // avatarKey は SlotHandle.Settings.avatarProviderDescriptor から推定不可（descriptor は ProviderConfig 参照）。
                // 上位（SlotAssignmentApplier）が assignment 受信時に保持する。
                OnSlotStateChanged?.Invoke(slotId, prev, next, null);

                // catalog の coalesce は上流 D-7 に委ねる：状態変化のたびに publish する。
                _pendingQueue.EnqueueOrExecute(_sink, sink => PublishCatalog(sink));
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Catalog,
                    $"OnStateChanged handler failed (slot={ev?.SlotId})", ex);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _subscription?.Dispose();
            _subscription = null;
        }
    }
}
