#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Dispatch;

namespace VTuberSystemBase.CoreIpc.Core.Subscription
{
    public sealed class TopicSubscriptionRegistry : IDispatchHandlerLookup
    {
        private readonly object _sync = new();
        private readonly Dictionary<(string Topic, MessageKind Kind), List<Registration>> _registrations = new();

        public int RegistrationCount
        {
            get
            {
                lock (_sync)
                {
                    int total = 0;
                    foreach (var list in _registrations.Values)
                    {
                        total += list.Count;
                    }
                    return total;
                }
            }
        }

        public int RegisteredKeyCount
        {
            get
            {
                lock (_sync)
                {
                    return _registrations.Count;
                }
            }
        }

        public ISubscriptionToken Register(
            string topic,
            MessageKind kind,
            DispatchHandler handler,
            Type? payloadType = null)
        {
            if (topic is null) throw new ArgumentNullException(nameof(topic));
            if (topic.Length == 0)
            {
                throw new ArgumentException("topic must be a non-empty string.", nameof(topic));
            }
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            var key = (topic, kind);
            var registration = new Registration(handler, payloadType);

            lock (_sync)
            {
                if (!_registrations.TryGetValue(key, out var list))
                {
                    list = new List<Registration>();
                    _registrations[key] = list;
                }
                list.Add(registration);
            }

            return new SubscriptionToken(() => Unregister(key, registration));
        }

        public bool TryGetHandlers(
            string topic,
            MessageKind kind,
            out IReadOnlyList<DispatchHandler> handlers)
        {
            if (topic is null)
            {
                handlers = Array.Empty<DispatchHandler>();
                return false;
            }

            var key = (topic, kind);
            lock (_sync)
            {
                if (_registrations.TryGetValue(key, out var list) && list.Count > 0)
                {
                    var snapshot = new DispatchHandler[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        snapshot[i] = list[i].Handler;
                    }
                    handlers = snapshot;
                    return true;
                }
            }

            handlers = Array.Empty<DispatchHandler>();
            return false;
        }

        public int CountFor(string topic, MessageKind kind)
        {
            if (topic is null) return 0;
            var key = (topic, kind);
            lock (_sync)
            {
                return _registrations.TryGetValue(key, out var list) ? list.Count : 0;
            }
        }

        private void Unregister((string Topic, MessageKind Kind) key, Registration registration)
        {
            lock (_sync)
            {
                if (!_registrations.TryGetValue(key, out var list)) return;

                for (int i = 0; i < list.Count; i++)
                {
                    if (ReferenceEquals(list[i], registration))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }

                if (list.Count == 0)
                {
                    _registrations.Remove(key);
                }
            }
        }

        private sealed class Registration
        {
            public DispatchHandler Handler { get; }
            public Type? PayloadType { get; }

            public Registration(DispatchHandler handler, Type? payloadType)
            {
                Handler = handler;
                PayloadType = payloadType;
            }
        }
    }
}
