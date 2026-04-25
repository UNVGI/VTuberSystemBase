#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Subscription;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class TopicSubscriptionRegistryTests
    {
        private static MessageEnvelope BuildEnvelope(MessageKind kind, string topic, int payloadInt)
        {
            using var doc = JsonDocument.Parse(payloadInt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: kind,
                Topic: topic,
                CorrelationId: null,
                TimestampUnixMs: 0L,
                Payload: doc.RootElement.Clone());
        }

        [Test]
        public void Register_Then_TryGetHandlers_ReturnsHandler()
        {
            var registry = new TopicSubscriptionRegistry();
            DispatchHandler handler = _ => { };

            var token = registry.Register("topic/x", MessageKind.State, handler);

            Assert.IsTrue(registry.TryGetHandlers("topic/x", MessageKind.State, out var handlers));
            Assert.AreEqual(1, handlers.Count);
            Assert.AreSame(handler, handlers[0]);
            Assert.AreEqual(1, registry.RegistrationCount);
            Assert.AreEqual(1, registry.RegisteredKeyCount);
            Assert.AreEqual(1, registry.CountFor("topic/x", MessageKind.State));

            token.Dispose();
        }

        [Test]
        public void Register_DispatchedHandler_ReceivesEnvelope_AfterRegistrationOnly()
        {
            var registry = new TopicSubscriptionRegistry();
            var queue = new MainThreadDispatchQueue();
            queue.SetHandlerLookup(registry);

            var received = new List<int>();
            var token = registry.Register(
                "topic/event",
                MessageKind.Event,
                env => received.Add(env.Payload.GetInt32()));

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 2));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { 1, 2 }, received,
                "Registered handler must receive enqueued envelopes via the dispatch queue.");

            token.Dispose();

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 3));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { 1, 2 }, received,
                "After Dispose, the subscription must no longer receive envelopes.");
        }

        [Test]
        public void TryGetHandlers_AfterDispose_ReturnsFalse()
        {
            var registry = new TopicSubscriptionRegistry();
            var token = registry.Register("topic/x", MessageKind.State, _ => { });

            Assert.IsTrue(registry.TryGetHandlers("topic/x", MessageKind.State, out _));

            token.Dispose();

            Assert.IsFalse(registry.TryGetHandlers("topic/x", MessageKind.State, out var handlers),
                "After disposing the only token for the key, lookup must return false.");
            Assert.AreEqual(0, handlers.Count);
            Assert.AreEqual(0, registry.RegistrationCount);
            Assert.AreEqual(0, registry.RegisteredKeyCount);
        }

        [Test]
        public void SubscriptionToken_Dispose_IsIdempotent()
        {
            var registry = new TopicSubscriptionRegistry();
            int callbackInvocations = 0;

            DispatchHandler handler = _ => { };
            var token = registry.Register("topic/x", MessageKind.State, handler);

            // Register a separate token to observe the registry mutations: we will
            // dispose `token` twice and assert the registry only loses one entry.
            var probeToken = registry.Register("topic/x", MessageKind.State, _ => callbackInvocations++);

            Assert.AreEqual(2, registry.CountFor("topic/x", MessageKind.State));

            token.Dispose();
            token.Dispose(); // multi-Dispose must be a no-op
            token.Dispose();

            Assert.AreEqual(1, registry.CountFor("topic/x", MessageKind.State),
                "Second / third Dispose calls must not remove additional registrations.");

            probeToken.Dispose();
            Assert.AreEqual(0, registry.RegistrationCount);
        }

        [Test]
        public void Register_SameTopic_MultipleHandlers_AllInvokedInRegistrationOrder()
        {
            var registry = new TopicSubscriptionRegistry();
            var queue = new MainThreadDispatchQueue();
            queue.SetHandlerLookup(registry);

            var trace = new List<string>();
            var t1 = registry.Register("topic/x", MessageKind.State,
                env => trace.Add("a:" + env.Payload.GetInt32()));
            var t2 = registry.Register("topic/x", MessageKind.State,
                env => trace.Add("b:" + env.Payload.GetInt32()));
            var t3 = registry.Register("topic/x", MessageKind.State,
                env => trace.Add("c:" + env.Payload.GetInt32()));

            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/x", 7));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { "a:7", "b:7", "c:7" }, trace,
                "Multiple handlers registered for the same topic must all run, in registration order.");

            // Disposing the middle handler removes only that one
            t2.Dispose();
            trace.Clear();
            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/x", 9));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { "a:9", "c:9" }, trace,
                "After disposing the middle handler, the surviving handlers must still run in order.");

            t1.Dispose();
            t3.Dispose();
            Assert.AreEqual(0, registry.RegistrationCount);
        }

        [Test]
        public void Register_DistinctKindsForSameTopic_AreIsolated()
        {
            var registry = new TopicSubscriptionRegistry();
            var queue = new MainThreadDispatchQueue();
            queue.SetHandlerLookup(registry);

            var stateValues = new List<int>();
            var eventValues = new List<int>();

            registry.Register("topic/x", MessageKind.State, env => stateValues.Add(env.Payload.GetInt32()));
            registry.Register("topic/x", MessageKind.Event, env => eventValues.Add(env.Payload.GetInt32()));

            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/x", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/x", 2));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/x", 3));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { 1 }, stateValues);
            CollectionAssert.AreEqual(new[] { 2, 3 }, eventValues);
        }

        [Test]
        public void Register_DistinctTopicsForSameKind_AreIsolated()
        {
            var registry = new TopicSubscriptionRegistry();
            var queue = new MainThreadDispatchQueue();
            queue.SetHandlerLookup(registry);

            var aValues = new List<int>();
            var bValues = new List<int>();
            registry.Register("topic/a", MessageKind.Event, env => aValues.Add(env.Payload.GetInt32()));
            registry.Register("topic/b", MessageKind.Event, env => bValues.Add(env.Payload.GetInt32()));

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/a", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/b", 2));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/a", 3));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { 1, 3 }, aValues);
            CollectionAssert.AreEqual(new[] { 2 }, bValues);
        }

        [Test]
        public void TryGetHandlers_UnknownTopicOrKind_ReturnsFalseAndEmptyList()
        {
            var registry = new TopicSubscriptionRegistry();
            registry.Register("topic/x", MessageKind.State, _ => { });

            Assert.IsFalse(registry.TryGetHandlers("topic/missing", MessageKind.State, out var h1));
            Assert.AreEqual(0, h1.Count);

            Assert.IsFalse(registry.TryGetHandlers("topic/x", MessageKind.Event, out var h2),
                "TryGetHandlers must return false for the same topic but a different kind.");
            Assert.AreEqual(0, h2.Count);
        }

        [Test]
        public void TryGetHandlers_NullTopic_ReturnsFalse()
        {
            var registry = new TopicSubscriptionRegistry();
            Assert.IsFalse(registry.TryGetHandlers(null!, MessageKind.State, out var handlers));
            Assert.AreEqual(0, handlers.Count);
        }

        [Test]
        public void TryGetHandlers_ReturnsSnapshot_NotLiveView()
        {
            var registry = new TopicSubscriptionRegistry();
            registry.Register("topic/x", MessageKind.Event, _ => { });

            Assert.IsTrue(registry.TryGetHandlers("topic/x", MessageKind.Event, out var firstSnapshot));
            Assert.AreEqual(1, firstSnapshot.Count);

            registry.Register("topic/x", MessageKind.Event, _ => { });

            Assert.AreEqual(1, firstSnapshot.Count,
                "Existing snapshot must not reflect later mutations to the registry.");

            Assert.IsTrue(registry.TryGetHandlers("topic/x", MessageKind.Event, out var secondSnapshot));
            Assert.AreEqual(2, secondSnapshot.Count);
        }

        [Test]
        public void Register_RejectsNullTopicNullHandlerOrEmptyTopic()
        {
            var registry = new TopicSubscriptionRegistry();

            Assert.Throws<ArgumentNullException>(
                () => registry.Register(null!, MessageKind.State, _ => { }));
            Assert.Throws<ArgumentException>(
                () => registry.Register(string.Empty, MessageKind.State, _ => { }));
            Assert.Throws<ArgumentNullException>(
                () => registry.Register("topic/x", MessageKind.State, null!));
        }

        [Test]
        public void Register_PayloadTypeOverload_StoresMetadataWithoutAffectingDispatch()
        {
            // The optional payloadType parameter is metadata for higher layers
            // (CoreIpcBus typed wrapping). It must not change dispatch semantics.
            var registry = new TopicSubscriptionRegistry();

            DispatchHandler h1 = _ => { };
            DispatchHandler h2 = _ => { };

            registry.Register("topic/x", MessageKind.State, h1, payloadType: typeof(int));
            registry.Register("topic/x", MessageKind.State, h2, payloadType: null);

            Assert.IsTrue(registry.TryGetHandlers("topic/x", MessageKind.State, out var handlers));
            Assert.AreEqual(2, handlers.Count);
            Assert.AreSame(h1, handlers[0]);
            Assert.AreSame(h2, handlers[1]);
        }

        [Test]
        public void Register_SameDelegateInstanceTwice_RegistersIndependently()
        {
            var registry = new TopicSubscriptionRegistry();
            int invocations = 0;
            DispatchHandler shared = _ => invocations++;

            var t1 = registry.Register("topic/x", MessageKind.Event, shared);
            var t2 = registry.Register("topic/x", MessageKind.Event, shared);

            Assert.AreEqual(2, registry.CountFor("topic/x", MessageKind.Event),
                "Re-registering the same delegate must add a second registration.");

            t1.Dispose();
            Assert.AreEqual(1, registry.CountFor("topic/x", MessageKind.Event),
                "Disposing one token must remove exactly one of the two registrations.");

            t2.Dispose();
            Assert.AreEqual(0, registry.CountFor("topic/x", MessageKind.Event));
        }

        [Test]
        public async Task Register_ConcurrentRegistrationsForSameTopic_AreAllVisible()
        {
            var registry = new TopicSubscriptionRegistry();
            const int writers = 8;
            const int perWriter = 200;

            var tokens = new System.Collections.Concurrent.ConcurrentBag<ISubscriptionToken>();

            var tasks = new List<Task>();
            for (int w = 0; w < writers; w++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < perWriter; i++)
                    {
                        var token = registry.Register("topic/x", MessageKind.Event, _ => { });
                        tokens.Add(token);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            Assert.AreEqual(writers * perWriter,
                registry.CountFor("topic/x", MessageKind.Event),
                "All concurrent registrations must be visible after writers complete.");

            // Concurrent unregistration through Dispose
            var disposeTasks = new List<Task>();
            foreach (var token in tokens)
            {
                disposeTasks.Add(Task.Run(() => token.Dispose()));
            }
            await Task.WhenAll(disposeTasks);

            Assert.AreEqual(0, registry.CountFor("topic/x", MessageKind.Event),
                "All registrations must be removed after concurrent Dispose.");
            Assert.AreEqual(0, registry.RegistrationCount);
            Assert.AreEqual(0, registry.RegisteredKeyCount);
        }

        [Test]
        public void Registry_IsUsableAsHandlerLookup_ForDispatchQueue()
        {
            // Verifies the registry implements IDispatchHandlerLookup contract that
            // MainThreadDispatchQueue depends on, end-to-end.
            var registry = new TopicSubscriptionRegistry();
            IDispatchHandlerLookup lookup = registry;

            DispatchHandler handler = _ => { };
            registry.Register("topic/x", MessageKind.Event, handler);

            Assert.IsTrue(lookup.TryGetHandlers("topic/x", MessageKind.Event, out var handlers));
            Assert.AreEqual(1, handlers.Count);
            Assert.AreSame(handler, handlers[0]);
        }

        [Test]
        public void Register_AllKinds_CanCoexistForSameTopic()
        {
            var registry = new TopicSubscriptionRegistry();
            registry.Register("topic/x", MessageKind.State, _ => { });
            registry.Register("topic/x", MessageKind.Event, _ => { });
            registry.Register("topic/x", MessageKind.Request, _ => { });
            registry.Register("topic/x", MessageKind.Response, _ => { });

            Assert.AreEqual(4, registry.RegistrationCount);
            Assert.AreEqual(4, registry.RegisteredKeyCount);
            Assert.IsTrue(registry.TryGetHandlers("topic/x", MessageKind.Request, out _));
            Assert.IsTrue(registry.TryGetHandlers("topic/x", MessageKind.Response, out _));
        }

        [Test]
        public void Dispose_AfterFlushIsInProgress_DoesNotAffectInFlightDispatch()
        {
            // Snapshot semantics ensure that disposing a token mid-flush does not
            // mutate the list MainThreadDispatchQueue is iterating.
            var registry = new TopicSubscriptionRegistry();
            var queue = new MainThreadDispatchQueue();
            queue.SetHandlerLookup(registry);

            var trace = new List<string>();
            ISubscriptionToken? secondToken = null;

            registry.Register("topic/x", MessageKind.Event, env =>
            {
                trace.Add("a:" + env.Payload.GetInt32());
                // Dispose a sibling subscription mid-dispatch — it must not affect
                // the snapshot the queue is iterating for the current envelope.
                secondToken?.Dispose();
            });
            secondToken = registry.Register("topic/x", MessageKind.Event,
                env => trace.Add("b:" + env.Payload.GetInt32()));

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/x", 1));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { "a:1", "b:1" }, trace,
                "Mid-flush Dispose must not remove handlers from the active dispatch snapshot.");

            // But on the next flush, the disposed handler is gone.
            trace.Clear();
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/x", 2));
            queue.Flush();

            CollectionAssert.AreEqual(new[] { "a:2" }, trace,
                "After Dispose has been observed, subsequent flushes must not invoke it.");
        }
    }
}
