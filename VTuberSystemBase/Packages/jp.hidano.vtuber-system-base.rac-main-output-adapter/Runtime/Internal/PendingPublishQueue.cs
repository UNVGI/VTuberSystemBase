using System;
using System.Collections.Generic;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;

namespace VTuberSystemBase.RacMainOutputAdapter.Internal
{
    /// <summary>
    /// IPC 受信開始前の publish を保留するキュー。<see cref="Initialize"/> 完了で
    /// <see cref="Flush"/> されて取りこぼしを防ぐ（Requirement 6.1, 6.2）。
    /// </summary>
    internal sealed class PendingPublishQueue
    {
        private readonly Queue<Action<IAdapterMessageSink>> _queue = new();
        private readonly int _capacity;
        private readonly IDiagnosticsLogger _logger;
        private bool _flushed;

        /// <summary>キューを生成する。</summary>
        public PendingPublishQueue(int capacity, IDiagnosticsLogger logger)
        {
            _capacity = capacity > 0 ? capacity : 16;
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary>flush 後は true。新規 enqueue は即時実行される（<see cref="EnqueueOrExecute"/>）。</summary>
        public bool IsFlushed => _flushed;

        /// <summary>publish 動作を保留キューに追加する。</summary>
        public void Enqueue(Action<IAdapterMessageSink> publishAction)
        {
            if (publishAction == null) throw new ArgumentNullException(nameof(publishAction));
            if (_queue.Count >= _capacity)
            {
                _queue.Dequeue();
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Catalog,
                    $"PendingPublishQueue overflow (capacity={_capacity}), dropped oldest entry.");
            }
            _queue.Enqueue(publishAction);
        }

        /// <summary>flush 済みなら即時実行、未 flush ならキュー追加する。</summary>
        public void EnqueueOrExecute(IAdapterMessageSink sink, Action<IAdapterMessageSink> publishAction)
        {
            if (_flushed)
            {
                publishAction(sink);
                return;
            }
            Enqueue(publishAction);
        }

        /// <summary>保留分を <paramref name="sink"/> 経由で順次 publish する。1 回のみ flush を許す。</summary>
        public void Flush(IAdapterMessageSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            while (_queue.Count > 0)
            {
                var action = _queue.Dequeue();
                try
                {
                    action(sink);
                }
                catch (Exception ex)
                {
                    _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Catalog,
                        "PendingPublishQueue entry threw during Flush.", ex);
                }
            }
            _flushed = true;
        }
    }
}
