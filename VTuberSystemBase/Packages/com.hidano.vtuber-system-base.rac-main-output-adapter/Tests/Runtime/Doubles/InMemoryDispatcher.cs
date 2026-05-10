using System;
using System.Collections.Generic;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// <see cref="IOutputCommandDispatcher"/> のメモリ実装テストダブル（Requirement 11.1, 8.6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 内部に <c>(topic, kind) → Delegate</c> 辞書を持ち、テストから <see cref="EmitState{T}"/> /
    /// <see cref="EmitEvent{T}"/> / <see cref="EmitRequest{TReq, TRes}"/> でハンドラを駆動する。
    /// </para>
    /// <para>
    /// PublishState / PublishEvent 相当の送信履歴は本ダブルに直接 <see cref="RecordSent"/> する形で記録される。
    /// 実コードでは <see cref="ICoreIpcBus"/> 経由で送信されるが、Tests では本ダブル内部に集約する。
    /// </para>
    /// </remarks>
    public sealed class InMemoryDispatcher : IOutputCommandDispatcher
    {
        private readonly Dictionary<(string topic, MessageKind kind), Delegate> _handlers = new();
        private readonly List<SentMessage> _sentMessages = new();
        private bool _disposed;

        /// <summary>送信履歴。</summary>
        public IReadOnlyList<SentMessage> SentMessages => _sentMessages;

        /// <summary>登録ハンドラ件数。</summary>
        public int RegisteredHandlerCount => _handlers.Count;

        /// <inheritdoc/>
        public OutputCommandHandlerRegistration RegisterStateHandler<TPayload>(string topic, Action<StateCommand<TPayload>> handler)
        {
            ThrowIfDisposed();
            ValidateRegistration(topic, handler);
            var key = (topic, MessageKind.State);
            if (_handlers.ContainsKey(key))
                throw new InvalidOperationException($"Handler already registered for ({topic}, State).");
            _handlers[key] = handler;
            return new OutputCommandHandlerRegistration(() => _handlers.Remove(key));
        }

        /// <inheritdoc/>
        public OutputCommandHandlerRegistration RegisterEventHandler<TPayload>(string topic, Action<EventCommand<TPayload>> handler)
        {
            ThrowIfDisposed();
            ValidateRegistration(topic, handler);
            var key = (topic, MessageKind.Event);
            if (_handlers.ContainsKey(key))
                throw new InvalidOperationException($"Handler already registered for ({topic}, Event).");
            _handlers[key] = handler;
            return new OutputCommandHandlerRegistration(() => _handlers.Remove(key));
        }

        /// <inheritdoc/>
        public OutputCommandHandlerRegistration RegisterRequestHandler<TRequest, TResponse>(string topic, Func<RequestCommand<TRequest>, TResponse> handler)
        {
            ThrowIfDisposed();
            ValidateRegistration(topic, handler);
            var key = (topic, MessageKind.Request);
            if (_handlers.ContainsKey(key))
                throw new InvalidOperationException($"Handler already registered for ({topic}, Request).");
            _handlers[key] = handler;
            return new OutputCommandHandlerRegistration(() => _handlers.Remove(key));
        }

        /// <summary>登録済みの state ハンドラへ <paramref name="payload"/> を送信する。</summary>
        public bool EmitState<TPayload>(string topic, TPayload payload)
        {
            if (!_handlers.TryGetValue((topic, MessageKind.State), out var d)) return false;
            if (d is Action<StateCommand<TPayload>> handler)
            {
                handler(new StateCommand<TPayload>
                {
                    Topic = topic,
                    Payload = payload,
                    ReceivedAtTicks = DateTime.UtcNow.Ticks,
                });
                return true;
            }
            throw new InvalidCastException($"Handler for ({topic}, State) is not Action<StateCommand<{typeof(TPayload).Name}>>.");
        }

        /// <summary>登録済みの event ハンドラへ <paramref name="payload"/> を送信する。</summary>
        public bool EmitEvent<TPayload>(string topic, TPayload payload)
        {
            if (!_handlers.TryGetValue((topic, MessageKind.Event), out var d)) return false;
            if (d is Action<EventCommand<TPayload>> handler)
            {
                handler(new EventCommand<TPayload>
                {
                    Topic = topic,
                    Payload = payload,
                    ReceivedAtTicks = DateTime.UtcNow.Ticks,
                });
                return true;
            }
            throw new InvalidCastException($"Handler for ({topic}, Event) is not Action<EventCommand<{typeof(TPayload).Name}>>.");
        }

        /// <summary>登録済みの request ハンドラを呼び出して同期応答を取得する。</summary>
        /// <returns>ハンドラが登録されていれば true、そうでなければ false。</returns>
        public bool EmitRequest<TRequest, TResponse>(string topic, TRequest payload, out TResponse response)
        {
            if (!_handlers.TryGetValue((topic, MessageKind.Request), out var d))
            {
                response = default;
                return false;
            }
            if (d is Func<RequestCommand<TRequest>, TResponse> handler)
            {
                response = handler(new RequestCommand<TRequest>
                {
                    Topic = topic,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    Payload = payload,
                    ReceivedAtTicks = DateTime.UtcNow.Ticks,
                });
                return true;
            }
            throw new InvalidCastException($"Handler for ({topic}, Request) is not Func<RequestCommand<{typeof(TRequest).Name}>, {typeof(TResponse).Name}>.");
        }

        /// <summary>送信履歴を記録する。本 spec の Senders から呼び出される（PublishState / PublishEvent の代替）。</summary>
        public void RecordSent(string topic, MessageKind kind, object payload)
        {
            _sentMessages.Add(new SentMessage(topic, kind, payload));
        }

        /// <summary><paramref name="topic"/> 宛て送信メッセージを抽出する（kind 指定可）。</summary>
        public IReadOnlyList<SentMessage> GetSent(string topic, MessageKind? kind = null)
        {
            var list = new List<SentMessage>();
            foreach (var m in _sentMessages)
            {
                if (m.Topic != topic) continue;
                if (kind.HasValue && m.Kind != kind.Value) continue;
                list.Add(m);
            }
            return list;
        }

        /// <summary>送信履歴をクリアする。</summary>
        public void ClearSent() => _sentMessages.Clear();

        /// <summary>指定 (topic, kind) のハンドラ未登録ならば true。</summary>
        public bool HasHandler(string topic, MessageKind kind) => _handlers.ContainsKey((topic, kind));

        /// <inheritdoc/>
        public void Dispose()
        {
            _disposed = true;
            _handlers.Clear();
            _sentMessages.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new InvalidOperationException("Dispatcher is disposed.");
        }

        private static void ValidateRegistration(string topic, Delegate handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must not be null/empty.", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>送信履歴の 1 エントリ。</summary>
        public readonly record struct SentMessage(string Topic, MessageKind Kind, object Payload);
    }
}
