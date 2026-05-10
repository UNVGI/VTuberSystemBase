#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IUiCommandClient"/> + <see cref="IUiSubscriptionClient"/>
    /// double for stage-lighting-volume-tab tests. Records every send and lets the test
    /// inject the response for the next <c>RequestAsync</c> call. Tests dispatch
    /// synthetic envelopes via <see cref="Emit{TPayload}"/>.
    /// (Task 1.2, Requirement 12.1, 12.2)
    /// </summary>
    public sealed class FakeIpcClient : IUiCommandClient, IUiSubscriptionClient
    {
        // ---------- Command side ----------

        public sealed class SendRecord
        {
            public string Topic { get; init; } = "";
            public MessageKind Kind { get; init; }
            public object? Payload { get; init; }
        }

        public sealed class RequestRecord
        {
            public string Topic { get; init; } = "";
            public object? Request { get; init; }
            public Type ResponseType { get; init; } = typeof(object);
            public TimeSpan? Timeout { get; init; }
        }

        public List<SendRecord> Sent { get; } = new List<SendRecord>();
        public List<RequestRecord> Requests { get; } = new List<RequestRecord>();

        public Func<RequestRecord, object?>? RequestResponder { get; set; }
        public bool ForceFail { get; set; }
        public SendError? FailWith { get; set; }

        public SendResult PublishState<TPayload>(string topic, TPayload payload)
        {
            Sent.Add(new SendRecord { Topic = topic, Kind = MessageKind.State, Payload = payload });
            return ForceFail
                ? SendResult.Fail(FailWith ?? new SendError(SendErrorCode.NotConnected))
                : SendResult.Ok();
        }

        public SendResult PublishEvent<TPayload>(string topic, TPayload payload)
        {
            Sent.Add(new SendRecord { Topic = topic, Kind = MessageKind.Event, Payload = payload });
            return ForceFail
                ? SendResult.Fail(FailWith ?? new SendError(SendErrorCode.NotConnected))
                : SendResult.Ok();
        }

        public Task<RequestResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var record = new RequestRecord
            {
                Topic = topic,
                Request = payload,
                ResponseType = typeof(TResponse),
                Timeout = timeout,
            };
            Requests.Add(record);

            if (RequestResponder is null)
            {
                return Task.FromResult(
                    RequestResult<TResponse>.Fail(new RequestError(RequestErrorCode.Timeout)));
            }

            var resp = RequestResponder(record);
            if (resp is TResponse typed)
            {
                return Task.FromResult(RequestResult<TResponse>.Ok(typed));
            }

            return Task.FromResult(
                RequestResult<TResponse>.Fail(new RequestError(RequestErrorCode.SerializationFailed)));
        }

        // ---------- Subscription side ----------

        public sealed class Subscription
        {
            public string Topic { get; init; } = "";
            public MessageKind Kind { get; init; }
            public Type PayloadType { get; init; } = typeof(object);
            public Action<object> Invoke { get; init; } = _ => { };
            public bool IsActive { get; internal set; } = true;
        }

        private readonly List<Subscription> _subs = new List<Subscription>();

        public IReadOnlyList<Subscription> Subscriptions => _subs;

        public ISubscriptionToken Subscribe<TPayload>(
            string topic,
            MessageKind kind,
            Action<MessageEnvelope<TPayload>> callback)
        {
            var sub = new Subscription
            {
                Topic = topic,
                Kind = kind,
                PayloadType = typeof(TPayload),
                Invoke = env => callback((MessageEnvelope<TPayload>)env),
            };
            _subs.Add(sub);
            return new Token(this, sub);
        }

        /// <summary>
        /// Dispatches <paramref name="payload"/> as a synthetic envelope to every
        /// matching subscriber. Returns the number of callbacks invoked.
        /// </summary>
        public int Emit<TPayload>(string topic, TPayload payload, MessageKind kind = MessageKind.State, string? correlationId = null)
        {
            var env = new MessageEnvelope<TPayload>(topic, kind, correlationId, payload, DateTimeOffset.UtcNow);
            object boxed = env;
            int count = 0;
            // Use ToArray so subscribers can dispose during iteration without invalidating it.
            foreach (var sub in _subs.ToArray())
            {
                if (!sub.IsActive) continue;
                if (!string.Equals(sub.Topic, topic, StringComparison.Ordinal)) continue;
                if (sub.Kind != kind) continue;
                if (sub.PayloadType != typeof(TPayload)) continue;
                sub.Invoke(boxed);
                count++;
            }
            return count;
        }

        private sealed class Token : ISubscriptionToken
        {
            private readonly FakeIpcClient _owner;
            private readonly Subscription _sub;

            public Token(FakeIpcClient owner, Subscription sub)
            {
                _owner = owner;
                _sub = sub;
            }

            public string Topic => _sub.Topic;
            public bool IsActive => _sub.IsActive;

            public void Dispose()
            {
                _sub.IsActive = false;
                _owner._subs.Remove(_sub);
            }
        }
    }
}
