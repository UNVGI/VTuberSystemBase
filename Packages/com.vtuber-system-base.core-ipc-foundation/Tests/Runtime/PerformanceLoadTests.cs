#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Tests.TestSupport;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class PerformanceLoadTests
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
        [Timeout(120_000)]
        public IEnumerator StateCoalesce_HundredHzForTenSeconds_DropsIntermediateAndBoundsMemory()
        {
            const int publishesPerBurst = 10;
            const int totalBursts = 100;
            const int totalPublishes = publishesPerBurst * totalBursts;
            const int burstIntervalMs = 100;

            var options = new CoreIpcOptions
            {
                Host = "loopback",
                Port = 0,
                ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
                ReconnectMaxAttempts = 3,
                DefaultRequestTimeout = TimeSpan.FromSeconds(5),
                EventQueueWarningThresholdPerTopic = totalPublishes + 1,
            };

            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(host, options);

            int receivedCount = 0;
            int latestReceivedValue = -1;
            using var subscription = host.Bus.SubscribeState<int>(
                "topic/perf/state",
                payload =>
                {
                    receivedCount++;
                    latestReceivedValue = payload;
                });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long memoryStart = GC.GetTotalMemory(true);

            var stopwatch = Stopwatch.StartNew();
            int published = 0;
            for (int b = 0; b < totalBursts; b++)
            {
                for (int j = 0; j < publishesPerBurst; j++)
                {
                    var r = host.Bus.PublishState("topic/perf/state", published);
                    Assert.IsTrue(r.Success,
                        "PublishState " + published + " failed: " + r.Error);
                    published++;
                }

                var nextDeadlineMs = (long)(b + 1) * burstIntervalMs;
                while (stopwatch.ElapsedMilliseconds < nextDeadlineMs)
                {
                    yield return null;
                }
            }

            yield return LoopbackIntegrationHarness.WaitFor(
                () => latestReceivedValue == totalPublishes - 1,
                LoopbackIntegrationHarness.AssertTimeout,
                "Latest state value (" + (totalPublishes - 1) +
                ") was not delivered within the timeout. latest=" + latestReceivedValue);

            // Mirror the start-of-test GC discipline: the budget is a *retention* bound,
            // not an allocation bound, so we must let the collector reclaim the gen-0
            // garbage produced by JSON encoding / envelope construction before snapshotting.
            // Using GetTotalMemory(false) here was bundling that ephemeral garbage into the
            // delta and pushing the test ~18 MB over a 10 MB retention budget.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long memoryEnd = GC.GetTotalMemory(true);
            long memoryDelta = memoryEnd - memoryStart;

            Assert.AreEqual(totalPublishes - 1, latestReceivedValue,
                "The most recent state delivery must carry the latest published value.");
            Assert.Less(receivedCount, totalPublishes,
                "Coalesce semantics must drop intermediate state updates; " +
                "expected fewer than " + totalPublishes + " deliveries but observed " +
                receivedCount + ".");

            const long memoryBudgetBytes = 10L * 1024L * 1024L;
            Assert.Less(memoryDelta, memoryBudgetBytes,
                "Memory growth across the high-frequency state run must remain under 10 MB " +
                "(observed delta " + memoryDelta + " bytes, " + receivedCount +
                " deliveries for " + totalPublishes + " publishes).");

            host.Dispose();
        }

        [UnityTest]
        [Timeout(180_000)]
        public IEnumerator EventThroughput_SixThousandEvents_AllDeliveredInFifoOrder()
        {
            const int total = 6000;
            const int batchSize = 100;

            var options = new CoreIpcOptions
            {
                Host = "loopback",
                Port = 0,
                ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
                ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
                ReconnectMaxAttempts = 3,
                DefaultRequestTimeout = TimeSpan.FromSeconds(5),
                EventQueueWarningThresholdPerTopic = total + 1,
            };

            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(host, options);

            var received = new List<int>(total);
            using var subscription = host.Bus.SubscribeEvent<int>(
                "topic/perf/event",
                payload => received.Add(payload));

            for (int i = 0; i < total; i += batchSize)
            {
                int end = i + batchSize;
                if (end > total) end = total;
                for (int j = i; j < end; j++)
                {
                    var r = host.Bus.PublishEvent("topic/perf/event", j);
                    Assert.IsTrue(r.Success, "PublishEvent " + j + " failed: " + r.Error);
                }
                yield return null;
            }

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received.Count >= total,
                TimeSpan.FromSeconds(120),
                "Expected " + total + " events but only received " + received.Count + " in time.");

            Assert.AreEqual(total, received.Count,
                "All " + total + " events must be delivered exactly once.");
            for (int i = 0; i < total; i++)
            {
                Assert.AreEqual(i, received[i],
                    "FIFO ordering must be preserved under sustained throughput; " +
                    "mismatch at index " + i + " (got " + received[i] + ").");
            }

            host.Dispose();
        }

        [UnityTest]
        [Timeout(60_000)]
        public IEnumerator HundredConcurrentRequests_AllResolveBeforeFiveSecondTimeout()
        {
            const int concurrency = 100;
            var perRequestTimeout = TimeSpan.FromSeconds(5);

            var options = LoopbackIntegrationHarness.FastOptions(perRequestTimeout);

            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(host, options);

            using var registration = host.Bus.RegisterRequestHandler<int, int>(
                "topic/perf/rpc",
                (req, _) => Task.FromResult(req * 2));

            var tasks = new Task<IpcResult<int>>[concurrency];
            for (int i = 0; i < concurrency; i++)
            {
                tasks[i] = host.Bus.RequestAsync<int, int>(
                    "topic/perf/rpc",
                    i,
                    options: new RequestOptions(perRequestTimeout));
            }

            var allTasks = Task.WhenAll(tasks);
            yield return LoopbackIntegrationHarness.AwaitTask(
                allTasks, perRequestTimeout + TimeSpan.FromSeconds(15));

            int succeeded = 0;
            int timedOut = 0;
            for (int i = 0; i < concurrency; i++)
            {
                Assert.IsTrue(tasks[i].IsCompleted,
                    "Request " + i + " did not complete (no leak allowed).");

                var result = tasks[i].Result;
                if (result.Success)
                {
                    succeeded++;
                    Assert.AreEqual(i * 2, result.Value,
                        "Concurrent request " + i + " returned the wrong correlated value.");
                }
                else
                {
                    Assert.IsInstanceOf<CoreIpcError.RequestTimeout>(result.Error,
                        "Failure for request " + i + " must be RequestTimeout (got " +
                        result.Error + ").");
                    timedOut++;
                }
            }

            Assert.AreEqual(concurrency, succeeded + timedOut,
                "All " + concurrency +
                " concurrent requests must terminate (success or timeout) without leaks.");

            host.Dispose();
        }
    }
}
