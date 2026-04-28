#nullable enable
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Tests.TestSupport;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class FifoOrderingTests
    {
        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
        }

        [UnityTest]
        public IEnumerator EventFifo_OneThousandPublishes_DeliveredInOrder()
        {
            const int total = 1000;

            var fastOptions = new CoreIpcOptions
            {
                Host = "loopback",
                Port = 0,
                ReconnectInitialDelay = System.TimeSpan.FromMilliseconds(20),
                ReconnectMaxDelay = System.TimeSpan.FromMilliseconds(40),
                ReconnectMaxAttempts = 3,
                DefaultRequestTimeout = System.TimeSpan.FromSeconds(5),
                EventQueueWarningThresholdPerTopic = total + 1,
            };

            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(host, fastOptions);

            var received = new List<int>(total);
            using var subscription = host.Bus.SubscribeEvent<int>(
                "topic/event",
                payload => received.Add(payload));

            for (int i = 0; i < total; i++)
            {
                var r = host.Bus.PublishEvent("topic/event", i);
                Assert.IsTrue(r.Success, "PublishEvent " + i + " failed: " + r.Error);
            }

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received.Count >= total,
                LoopbackIntegrationHarness.AssertTimeout,
                "Expected " + total + " events but only received " + received.Count + " in time.");

            Assert.AreEqual(total, received.Count);
            for (int i = 0; i < total; i++)
            {
                Assert.AreEqual(i, received[i],
                    "Events must be delivered in FIFO order; mismatch at index " + i +
                    " (got " + received[i] + ").");
            }

            host.Dispose();
        }
    }
}
