#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Dispatch;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class IpcDispatchStepPlayModeTests
    {
        private PlayerLoopSystem _originalLoop;

        [SetUp]
        public void SetUp()
        {
            _originalLoop = PlayerLoop.GetCurrentPlayerLoop();
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
            PlayerLoop.SetPlayerLoop(_originalLoop);
        }

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

            public bool TryGetHandlers(string topic, MessageKind kind, out IReadOnlyList<DispatchHandler> handlers)
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

        [UnityTest]
        public IEnumerator PublishedState_DeliveredWithinOneFrame_ToSubscribedHandler()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            int? received = null;
            lookup.Register("topic/state", MessageKind.State, env => received = env.Payload.GetInt32());
            queue.SetHandlerLookup(lookup);

            var step = new IpcDispatchStep(queue);
            step.Install();

            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/state", 42));

            yield return null; // PreUpdate of the next frame should drive the dispatch.

            Assert.IsTrue(received.HasValue, "State handler must be invoked within one frame.");
            Assert.AreEqual(42, received!.Value);
        }

        [UnityTest]
        public IEnumerator PublishedEvents_PreserveFifoOrder_AcrossFrame()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var received = new List<int>();
            lookup.Register("topic/event", MessageKind.Event, env => received.Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            var step = new IpcDispatchStep(queue);
            step.Install();

            const int total = 50;
            for (int i = 0; i < total; i++)
            {
                queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", i));
            }

            yield return null;

            Assert.AreEqual(total, received.Count, "All events must be delivered within one frame.");
            for (int i = 0; i < total; i++)
            {
                Assert.AreEqual(i, received[i], "FIFO order must be preserved at index " + i);
            }
        }

        [UnityTest]
        public IEnumerator CoalescedState_SingleDeliveryPerFrame_ForSameTopic()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var received = new List<int>();
            lookup.Register("topic/state", MessageKind.State, env => received.Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            var step = new IpcDispatchStep(queue);
            step.Install();

            for (int i = 1; i <= 10; i++)
            {
                queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/state", i));
            }

            yield return null;

            CollectionAssert.AreEqual(new[] { 10 }, received,
                "Coalesce semantics must deliver only the latest state value per frame.");
        }

        [UnityTest]
        public IEnumerator HandlerException_DuringFrameTick_DoesNotPropagate()
        {
            int errorCount = 0;
            var queue = new MainThreadDispatchQueue(
                new CoreIpcOptions(),
                logWarning: null,
                logError: (_, _) => errorCount++);
            var lookup = new FakeHandlerLookup();
            var received = new List<int>();
            lookup.Register("topic/event", MessageKind.Event, env =>
            {
                if (env.Payload.GetInt32() == 7) throw new InvalidOperationException("boom");
                received.Add(env.Payload.GetInt32());
            });
            queue.SetHandlerLookup(lookup);

            var step = new IpcDispatchStep(queue);
            step.Install();

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 6));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 7));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 8));

            LogAssert.ignoreFailingMessages = true;
            yield return null;

            CollectionAssert.AreEqual(new[] { 6, 8 }, received,
                "Non-throwing event handlers must still fire after sibling raises.");
            Assert.AreEqual(1, errorCount, "Handler exception should be logged exactly once.");
        }
    }
}
