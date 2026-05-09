#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="IUiSubscriptionClient"/>. Stores callbacks per
    /// (topic, payload type) tuple; tests dispatch synthetic envelopes via
    /// <see cref="Emit{TPayload}"/>.
    /// </summary>
    public sealed class FakeUiSubscriptionClient : IUiSubscriptionClient
    {
        private readonly List<Subscription> _subs = new List<Subscription>();

        public IReadOnlyList<Subscription> Subscriptions => _subs;

        public ISubscriptionToken Subscribe<TPayload>(string topic, MessageKind kind, Action<MessageEnvelope<TPayload>> callback)
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

        public int Emit<TPayload>(string topic, TPayload payload, MessageKind kind = MessageKind.State)
        {
            var env = new MessageEnvelope<TPayload>(topic, kind, null, payload, DateTimeOffset.UtcNow);
            object boxed = env;
            int count = 0;
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

        public sealed class Subscription
        {
            public string Topic { get; init; } = "";
            public MessageKind Kind { get; init; }
            public Type PayloadType { get; init; } = typeof(object);
            public Action<object> Invoke { get; init; } = _ => { };
            public bool IsActive { get; internal set; } = true;
        }

        private sealed class Token : ISubscriptionToken
        {
            private readonly FakeUiSubscriptionClient _owner;
            private readonly Subscription _sub;

            public Token(FakeUiSubscriptionClient owner, Subscription sub)
            {
                _owner = owner;
                _sub = sub;
            }

            public void Dispose()
            {
                _sub.IsActive = false;
                _owner._subs.Remove(_sub);
            }
        }
    }
}
