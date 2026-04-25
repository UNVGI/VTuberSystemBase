#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Dispatch;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class IpcDispatchStepTests
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

        [Test]
        public void Constructor_NullQueue_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new IpcDispatchStep(null!));
        }

        [Test]
        public void Tick_InvokesQueueFlush_AndDispatchesHandlers()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var received = new List<int>();
            lookup.Register("topic/x", MessageKind.State, env => received.Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            var step = new IpcDispatchStep(queue);

            queue.Enqueue(BuildEnvelope(MessageKind.State, "topic/x", 42));
            step.Tick();

            CollectionAssert.AreEqual(new[] { 42 }, received);
            Assert.AreEqual(0, queue.StateSlotCount);
        }

        [Test]
        public void Tick_HandlerExceptionIsIsolatedByQueue_AndStepDoesNotThrow()
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
                if (env.Payload.GetInt32() == 2) throw new Exception("boom");
                received.Add(env.Payload.GetInt32());
            });
            queue.SetHandlerLookup(lookup);
            var step = new IpcDispatchStep(queue);

            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 2));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "topic/event", 3));

            Assert.DoesNotThrow(() => step.Tick());

            CollectionAssert.AreEqual(new[] { 1, 3 }, received);
            Assert.AreEqual(1, errorCount);
        }

        [Test]
        public void Install_RegistersStepUnderPreUpdate()
        {
            var queue = new MainThreadDispatchQueue();
            var step = new IpcDispatchStep(queue);

            Assert.IsFalse(step.IsInstalled);

            step.Install();

            Assert.IsTrue(step.IsInstalled);
            Assert.AreEqual(1, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()));
        }

        [Test]
        public void Install_DrivenByPlayerLoopUpdateDelegate_FlushesQueue()
        {
            var queue = new MainThreadDispatchQueue();
            var lookup = new FakeHandlerLookup();
            var stateReceived = new List<int>();
            var eventReceived = new List<int>();
            lookup.Register("t/state", MessageKind.State, env => stateReceived.Add(env.Payload.GetInt32()));
            lookup.Register("t/event", MessageKind.Event, env => eventReceived.Add(env.Payload.GetInt32()));
            queue.SetHandlerLookup(lookup);

            var step = new IpcDispatchStep(queue);
            step.Install();

            queue.Enqueue(BuildEnvelope(MessageKind.State, "t/state", 1));
            queue.Enqueue(BuildEnvelope(MessageKind.State, "t/state", 2));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "t/event", 10));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "t/event", 11));
            queue.Enqueue(BuildEnvelope(MessageKind.Event, "t/event", 12));

            InvokeIpcDispatchStepDelegates(PlayerLoop.GetCurrentPlayerLoop());

            CollectionAssert.AreEqual(new[] { 2 }, stateReceived);
            CollectionAssert.AreEqual(new[] { 10, 11, 12 }, eventReceived);
        }

        [Test]
        public void Uninstall_RemovesStepFromPlayerLoop()
        {
            var queue = new MainThreadDispatchQueue();
            var step = new IpcDispatchStep(queue);

            step.Install();
            Assert.IsTrue(step.IsInstalled);

            step.Uninstall();

            Assert.IsFalse(step.IsInstalled);
            Assert.AreEqual(0, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()));
        }

        [Test]
        public void RepeatedInstallUninstall_RemainsSymmetric()
        {
            var queue = new MainThreadDispatchQueue();
            var step = new IpcDispatchStep(queue);

            for (int i = 0; i < 5; i++)
            {
                step.Install();
                Assert.AreEqual(1, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()), "Iter " + i);
                step.Uninstall();
                Assert.AreEqual(0, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()), "Iter " + i);
            }
        }

        [Test]
        public void Install_TwiceWithCustomLogWarning_RoutesToCaller()
        {
            var queue = new MainThreadDispatchQueue();
            var step = new IpcDispatchStep(queue);
            int warningCount = 0;

            step.Install(logWarning: _ => warningCount++);
            step.Install(logWarning: _ => warningCount++);

            Assert.AreEqual(1, warningCount, "Second install must route warning to provided callback.");
            Assert.AreEqual(1, CountIpcDispatchStepUnderPreUpdate(PlayerLoop.GetCurrentPlayerLoop()));
        }

        private static int CountIpcDispatchStepUnderPreUpdate(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null) return 0;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
                var children = loop.subSystemList[i].subSystemList;
                if (children is null) return 0;
                int count = 0;
                for (int j = 0; j < children.Length; j++)
                {
                    if (children[j].type == typeof(IpcDispatchStep)) count++;
                }
                return count;
            }
            return 0;
        }

        private static void InvokeIpcDispatchStepDelegates(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null) return;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
                var children = loop.subSystemList[i].subSystemList;
                if (children is null) return;
                for (int j = 0; j < children.Length; j++)
                {
                    if (children[j].type == typeof(IpcDispatchStep))
                    {
                        children[j].updateDelegate?.Invoke();
                    }
                }
                return;
            }
        }
    }
}
