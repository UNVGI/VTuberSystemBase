using System.Collections.Generic;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.RacMainOutputAdapter.Internal;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// <see cref="IAdapterMessageSink"/> のテストダブル。送信履歴を保持し、テストから検証する。
    /// 任意で <see cref="InMemoryDispatcher"/> へ送信履歴を委譲して 1 箇所に集約することも可能。
    /// </summary>
    public sealed class RecordingMessageSink : IAdapterMessageSink
    {
        private readonly InMemoryDispatcher _forward;
        private readonly List<Sent> _entries = new();

        /// <summary>送信履歴。</summary>
        public IReadOnlyList<Sent> Entries => _entries;

        /// <summary>
        /// 任意で <paramref name="forward"/> を指定すると、送信履歴をそちらにも記録する。
        /// </summary>
        public RecordingMessageSink(InMemoryDispatcher forward = null)
        {
            _forward = forward;
        }

        /// <inheritdoc/>
        public void PublishState<TPayload>(string topic, TPayload payload)
        {
            _entries.Add(new Sent(topic, MessageKind.State, payload));
            _forward?.RecordSent(topic, MessageKind.State, payload);
        }

        /// <inheritdoc/>
        public void PublishEvent<TPayload>(string topic, TPayload payload)
        {
            _entries.Add(new Sent(topic, MessageKind.Event, payload));
            _forward?.RecordSent(topic, MessageKind.Event, payload);
        }

        /// <summary>送信履歴 1 件のレコード。</summary>
        public readonly record struct Sent(string Topic, MessageKind Kind, object Payload);
    }
}
