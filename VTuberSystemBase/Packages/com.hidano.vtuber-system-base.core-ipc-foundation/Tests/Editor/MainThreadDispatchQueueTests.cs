#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Dispatch;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class MainThreadDispatchQueueTests
    {
        private static MessageEnvelope BuildEnvelope(
            MessageKind kind,
            string topic,
            int payloadInt,
            string? correlationId = null)
        {
            using var doc = JsonDocument.Parse(payloadInt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: kind,
                Topic: topic,
                CorrelationId: correlationId,
                TimestampUnixMs: 0L,
                Payload: doc.RootElement.Clone());
        }

        private sealed class FakeHandlerLookup : IDispatchHandlerLookup
        {
            private readonly Dictionary<(string topic, MessageKind kind), List<DispatchHandler>> _handlers = new();

            public void Register(string topic, MessageKind kind, DispatchHandler handler)
            {
                if (!_handlers.TryGetValue((topic, kind), out var list))
                {
                    list = new List<DispatchHandler>();
                    _handlers[(topic, kind)] = list;
                }
                list.Add(handler);
            }

            public bool TryGetHandlers(
                string topic,
                MessageKind kind,
                out IReadOnlyList<DispatchHandler> handlers)
            {
                if (_handlers.TryGetValue((topic, kind), out var list))
                {
                    handlers = list;
                    return true;
                }
                handlers = Array.Empty<DispatchHandler>();
                return false;
            }
        }

        [Test]
        public void Enqueue_StateMessages_SameTopic_CoalesceToLatestOnFlush()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var received = new List<int>();
            lookup.Register("topic/x", MessageKind.State, env => received.Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            for (int i = 1; i <= 10; i++)
            {
                queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/x", i));
            }

            Assert.AreEqual(1, queue.StateSlotCount, "All 10 state messages should coalesce into a single slot.");

            queue.Flush();

            CollectionAssert.AreEqual(new[] { 10 }, received,
                "Only the latest state value should be delivered after coalesce.");
            Assert.AreEqual(0, queue.StateSlotCount, "State slots must be cleared after flush.");
        }

        [Test]
        public void Enqueue_StateMessages_MultipleTopics_DeliverLatestPerTopic()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var receivedByTopic = new Dictionary<string, List<int>>
            {
                ["topic/a"] = new(),
                ["topic/b"] = new(),
            };
            lookup.Register("topic/a", MessageKind.State, env => receivedByTopic["topic/a"].Add(env.Payload.GetInt32()));
            lookup.Register("topic/b", MessageKind.State, env => receivedByTopic["topic/b"].Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/a", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/b", 2));
            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/a", 3));
            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/b", 4));

            queue.Flush();

            CollectionAssert.AreEqual(new[] { 3 }, receivedByTopic["topic/a"]);
            CollectionAssert.AreEqual(new[] { 4 }, receivedByTopic["topic/b"]);
        }

        [Test]
        public void Enqueue_EventMessages_PreserveFifoOrder_OnFlush()
        {
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions { EventQueueWarningThresholdPerTopic = 10_000 });
            var lookup = new FakeHandlerLookup();
            var received = new List<int>();
            lookup.Register("topic/event", MessageKind.Event, env => received.Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            const int total = 1000;
            for (int i = 0; i < total; i++)
            {
                queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", i));
            }

            Assert.AreEqual(total, queue.EventQueueCount);
            queue.Flush();

            Assert.AreEqual(total, received.Count, "All 1000 events must be delivered.");
            for (int i = 0; i < total; i++)
            {
                Assert.AreEqual(i, received[i], "Event order must match enqueue order at index " + i);
            }
            Assert.AreEqual(0, queue.EventQueueCount, "Event queue must be drained after flush.");
        }

        [Test]
        public void Enqueue_EventMessages_AcrossTopics_PreserveGlobalEnqueueOrder()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var trace = new List<string>();
            lookup.Register("topic/a", MessageKind.Event, env => trace.Add("a:" + env.Payload.GetInt32()));
            lookup.Register("topic/b", MessageKind.Event, env => trace.Add("b:" + env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/a", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/b", 2));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/a", 3));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/b", 4));

            queue.Flush();

            CollectionAssert.AreEqual(new[] { "a:1", "b:2", "a:3", "b:4" }, trace);
        }

        [Test]
        public void Flush_HandlerException_DoesNotStopSubsequentDeliveries()
        {
            string? loggedError = null;
            Exception? loggedException = null;
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions(),
                logWarning: null,
                logError: (msg, ex) =>
                {
                    loggedError = msg;
                    loggedException = ex;
                });

            var lookup = new FakeHandlerLookup();
            var received = new List<int>();
            lookup.Register("topic/event", MessageKind.Event, env => received.Add(env.Payload.GetInt32()));
            lookup.Register("topic/event", MessageKind.Event, env =>
            {
                if (env.Payload.GetInt32() == 2)
                {
                    throw new InvalidOperationException("boom");
                }
                received.Add(100 + env.Payload.GetInt32());
            });
            queue.SetHandlerLookup(lookup);

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 2));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 3));

            queue.Flush();

            CollectionAssert.AreEqual(new[] { 1, 101, 2, 3, 103 }, received,
                "All non-throwing handlers should fire even if a sibling handler throws.");
            Assert.IsNotNull(loggedError, "Handler exception should be logged.");
            StringAssert.Contains("topic/event", loggedError!);
            Assert.IsInstanceOf<InvalidOperationException>(loggedException);
        }

        [Test]
        public void Flush_StateHandlerException_DoesNotStopEventDelivery()
        {
            int errorCount = 0;
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions(),
                logWarning: null,
                logError: (_, _) => errorCount++);

            var lookup = new FakeHandlerLookup();
            var eventReceived = new List<int>();
            lookup.Register("topic/state", MessageKind.State, _ => throw new Exception("state handler died"));
            lookup.Register("topic/event", MessageKind.Event, env => eventReceived.Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/state", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 9));

            queue.Flush();

            Assert.AreEqual(1, errorCount, "Exactly one handler exception should have been logged.");
            CollectionAssert.AreEqual(new[] { 9 }, eventReceived,
                "Event handlers must run even after a state handler throws.");
        }

        [Test]
        public void Enqueue_EventQueue_OverThreshold_LogsWarningOnceWhenCrossing()
        {
            const int threshold = 5;
            var warnings = new List<string>();
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions { EventQueueWarningThresholdPerTopic = threshold },
                logWarning: warnings.Add,
                logError: null);
            var lookup = new FakeHandlerLookup();
            queue.SetHandlerLookup(lookup);

            // Push exactly the threshold count: no warning yet.
            for (int i = 0; i < threshold; i++)
            {
                queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/spam", i));
            }
            Assert.AreEqual(0, warnings.Count, "No warning while depth equals threshold.");

            // One more crosses the threshold: should log exactly one warning.
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/spam", threshold));
            Assert.AreEqual(1, warnings.Count, "Crossing the threshold should log a warning.");
            StringAssert.Contains("topic/spam", warnings[0]);
            StringAssert.Contains("threshold", warnings[0]);

            // Further enqueues past the threshold should NOT log additional warnings (no spam).
            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/spam", 1000 + i));
            }
            Assert.AreEqual(1, warnings.Count, "Subsequent enqueues over the threshold must not spam warnings.");
        }

        [Test]
        public void Enqueue_EventQueue_AtExactlyThousandThreshold_DoesNotWarn()
        {
            // Defaults: EventQueueWarningThresholdPerTopic = 1000.
            var warnings = new List<string>();
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions(),
                logWarning: warnings.Add,
                logError: null);
            queue.SetHandlerLookup(new FakeHandlerLookup());

            for (int i = 0; i < 1000; i++)
            {
                queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", i));
            }

            Assert.AreEqual(0, warnings.Count,
                "Queue depth equal to the threshold (1000) must not produce a warning.");
        }

        [Test]
        public void Flush_WithoutHandlerLookup_DrainsQueueWithoutThrowing()
        {
            var queue = new MainThreadDispatchQueue();
            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/x", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/x", 2));

            Assert.DoesNotThrow(() => queue.Flush());
            Assert.AreEqual(0, queue.StateSlotCount);
            Assert.AreEqual(0, queue.EventQueueCount);
        }

        [Test]
        public void Enqueue_ResponseKind_IsDroppedAndLoggedAsWarning()
        {
            var warnings = new List<string>();
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions(),
                logWarning: warnings.Add,
                logError: null);

            queue.Enqueue(BuildEnvelope(MessageKind.Response, "topic/req", 2, correlationId: "c-1"));

            Assert.AreEqual(0, queue.StateSlotCount);
            Assert.AreEqual(0, queue.EventQueueCount);
            Assert.AreEqual(0, queue.RequestQueueCount);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("Response", warnings[0]);
        }

        [Test]
        public void Enqueue_RequestKind_IsBufferedAndDispatchedOnFlush()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var received = new List<(int payload, string? cid)>();
            lookup.Register("topic/req", MessageKind.Request, env =>
                received.Add((env.Payload.GetInt32(), env.CorrelationId)));
            queue.SetHandlerLookup(lookup);

            queue.Enqueue(BuildEnvelope(MessageKind.Request, "topic/req", 1, correlationId: "c-1"));
            queue.Enqueue(BuildEnvelope(MessageKind.Request, "topic/req", 2, correlationId: "c-2"));

            Assert.AreEqual(2, queue.RequestQueueCount,
                "Request envelopes should be buffered FIFO until Flush.");

            queue.Flush();

            Assert.AreEqual(0, queue.RequestQueueCount, "Request queue must drain on Flush.");
            CollectionAssert.AreEqual(
                new[] { (1, (string?)"c-1"), (2, (string?)"c-2") },
                received,
                "Request envelopes must be dispatched in FIFO order on Flush.");
        }

        [Test]
        public void Flush_DeliversRequestBatchAfterStateAndEvent()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var trace = new List<string>();
            lookup.Register("t/state", MessageKind.State, env => trace.Add("state:" + env.Payload.GetInt32()));
            lookup.Register("t/event", MessageKind.Event, env => trace.Add("event:" + env.Payload.GetInt32()));
            lookup.Register("t/req", MessageKind.Request, env => trace.Add("req:" + env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            queue.Enqueue(BuildEnvelope(MessageKind.Request, "t/req", 5, correlationId: "c"));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "t/event", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.State, "t/state", 7));

            queue.Flush();

            CollectionAssert.AreEqual(
                new[] { "state:7", "event:1", "req:5" }, trace,
                "Flush must deliver state batch first, then events, then requests.");
        }

        [Test]
        public void Enqueue_NullTopic_LogsWarningAndDrops()
        {
            var warnings = new List<string>();
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions(),
                logWarning: warnings.Add,
                logError: null);

            using var doc = JsonDocument.Parse("0");
            var bad = new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: MessageKind.State,
                Topic: null!,
                CorrelationId: null,
                TimestampUnixMs: 0,
                Payload: doc.RootElement.Clone());

            queue.Enqueue(bad);

            Assert.AreEqual(0, queue.StateSlotCount);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains("null topic", warnings[0]);
        }

        [Test]
        public void Constructor_RejectsNullOptions()
        {
            Assert.Throws<ArgumentNullException>(() => new MainThreadDispatchQueue(null!));
        }

        [Test]
        public void Constructor_RejectsNegativeWarningThreshold()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MainThreadDispatchQueue(new CoreIpcOptions { EventQueueWarningThresholdPerTopic = -1 }));
        }

        [Test]
        public void Flush_DeliversStateBatchBeforeEventBatch()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var trace = new List<string>();
            lookup.Register("t/state", MessageKind.State, env => trace.Add("state:" + env.Payload.GetInt32()));
            lookup.Register("t/event", MessageKind.Event, env => trace.Add("event:" + env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "t/event", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.State, "t/state", 7));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "t/event", 2));

            queue.Flush();

            CollectionAssert.AreEqual(new[] { "state:7", "event:1", "event:2" }, trace,
                "Flush must dispatch state batch before event batch, while preserving event FIFO.");
        }

        [Test]
        public void Enqueue_FromMultipleThreads_StateRetainsLatestPerTopic_AndEventsArePreservedFifoPerTopic()
        {
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions { EventQueueWarningThresholdPerTopic = 1_000_000 });
            var lookup = new FakeHandlerLookup();
            var stateLatest = new ConcurrentDictionary<string, int>();
            var eventsByTopic = new ConcurrentDictionary<string, List<int>>();

            for (int t = 0; t < 4; t++)
            {
                var topic = "topic/" + t;
                eventsByTopic[topic] = new List<int>();
                lookup.Register(topic, MessageKind.State, env => stateLatest[topic] = env.Payload.GetInt32());
                lookup.Register(topic, MessageKind.Event, env => eventsByTopic[topic].Add(env.Payload.GetInt32()));
            }
            queue.SetHandlerLookup(lookup);

            const int perThread = 250;
            var tasks = new List<Task>();
            for (int t = 0; t < 4; t++)
            {
                int threadIndex = t;
                tasks.Add(Task.Run(() =>
                {
                    var topic = "topic/" + threadIndex;
                    for (int i = 0; i < perThread; i++)
                    {
                        queue.Enqueue(BuildEnvelope(MessageKind.Event, topic, i));
                        queue.Enqueue(BuildEnvelope(MessageKind.State, topic, i));
                    }
                }));
            }
            Task.WaitAll(tasks.ToArray());

            queue.Flush();

            for (int t = 0; t < 4; t++)
            {
                var topic = "topic/" + t;
                Assert.AreEqual(perThread - 1, stateLatest[topic],
                    "Latest state per topic must equal the highest enqueued value.");
                Assert.AreEqual(perThread, eventsByTopic[topic].Count,
                    "All events for topic " + topic + " must be delivered.");
                for (int i = 0; i < perThread; i++)
                {
                    Assert.AreEqual(i, eventsByTopic[topic][i],
                        "FIFO order must be preserved per topic at index " + i);
                }
            }
            Assert.AreEqual(0, queue.EventQueueCount);
            Assert.AreEqual(0, queue.StateSlotCount);
        }

        [Test]
        public void Enqueue_StateMessages_AreObservableImmediatelyViaStateSlotCount()
        {
            var queue = new MainThreadDispatchQueue();
            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/a", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/b", 2));
            Assert.AreEqual(2, queue.StateSlotCount);
        }
    }
}
