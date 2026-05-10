#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities
{
    /// <summary>
    /// Compact assertions over <see cref="FakeCoreIpcBus"/> publish history.
    /// </summary>
    public static class AssertEnvelope
    {
        public static void StatePublishedExactlyOnce<TPayload>(FakeCoreIpcBus bus, string topic)
        {
            var matches = bus.PublishedStates
                .Where(s => s.Topic == topic && s.PayloadType == typeof(TPayload))
                .ToArray();
            Assert.That(matches.Length, Is.EqualTo(1),
                $"Expected exactly one PublishState<{typeof(TPayload).Name}>({topic}), got {matches.Length}.");
        }

        public static TPayload SingleStatePayload<TPayload>(FakeCoreIpcBus bus, string topic)
        {
            var matches = bus.PublishedStates
                .Where(s => s.Topic == topic && s.PayloadType == typeof(TPayload))
                .ToArray();
            Assert.That(matches.Length, Is.EqualTo(1),
                $"Expected exactly one PublishState<{typeof(TPayload).Name}>({topic}), got {matches.Length}.");
            return (TPayload)matches[0].Payload!;
        }

        public static void EventPublishedExactlyOnce<TPayload>(FakeCoreIpcBus bus, string topic)
        {
            var matches = bus.PublishedEvents
                .Where(e => e.Topic == topic && e.PayloadType == typeof(TPayload))
                .ToArray();
            Assert.That(matches.Length, Is.EqualTo(1),
                $"Expected exactly one PublishEvent<{typeof(TPayload).Name}>({topic}), got {matches.Length}.");
        }

        public static TPayload SingleEventPayload<TPayload>(FakeCoreIpcBus bus, string topic)
        {
            var matches = bus.PublishedEvents
                .Where(e => e.Topic == topic && e.PayloadType == typeof(TPayload))
                .ToArray();
            Assert.That(matches.Length, Is.EqualTo(1),
                $"Expected exactly one PublishEvent<{typeof(TPayload).Name}>({topic}), got {matches.Length}.");
            return (TPayload)matches[0].Payload!;
        }

        public static int CountStates(FakeCoreIpcBus bus, string topic)
            => bus.PublishedStates.Count(s => s.Topic == topic);

        public static int CountEvents(FakeCoreIpcBus bus, string topic)
            => bus.PublishedEvents.Count(e => e.Topic == topic);
    }
}
