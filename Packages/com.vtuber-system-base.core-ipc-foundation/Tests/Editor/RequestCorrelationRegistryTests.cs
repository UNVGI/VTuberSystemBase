#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Correlation;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class RequestCorrelationRegistryTests
    {
        private static JsonElement Json(string text)
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        [Test]
        public void AllocateCorrelationId_ProducesUniqueGuidLikeStrings()
        {
            using var registry = new RequestCorrelationRegistry();
            var ids = new HashSet<string>();
            for (int i = 0; i < 1000; i++)
            {
                var id = registry.AllocateCorrelationId();
                Assert.IsFalse(string.IsNullOrEmpty(id), "id must be non-empty.");
                Assert.IsTrue(ids.Add(id), "id collision detected: " + id);
                Assert.IsTrue(Guid.TryParseExact(id, "N", out _), "id must be a 32-char hex GUID without dashes.");
            }
        }

        [Test]
        public void DefaultTimeout_MatchesCoreIpcOptionsDefaultOfFiveSeconds()
        {
            using var registry = new RequestCorrelationRegistry();
            Assert.AreEqual(TimeSpan.FromSeconds(5), registry.DefaultTimeout);
        }

        [Test]
        public void Constructor_RejectsNullOptions()
        {
            Assert.Throws<ArgumentNullException>(() => new RequestCorrelationRegistry(null!));
        }

        [Test]
        public void Constructor_RejectsNegativeDefaultTimeout()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RequestCorrelationRegistry(new CoreIpcOptions
                {
                    DefaultRequestTimeout = TimeSpan.FromMilliseconds(-1),
                }));
        }

        [Test]
        public async Task RegisterPending_MatchResponse_CompletesWithOkPayload()
        {
            using var registry = new RequestCorrelationRegistry();
            var id = registry.AllocateCorrelationId();
            var task = registry.RegisterPending(id, TimeSpan.FromSeconds(10));

            Assert.AreEqual(1, registry.PendingRequestCount);
            Assert.IsFalse(task.IsCompleted, "pending task should not complete before MatchResponse.");

            var matched = registry.MatchResponse(id, Json("{\"value\":42}"));

            Assert.IsTrue(matched, "MatchResponse must return true for a registered correlation id.");
            var result = await task.ConfigureAwait(false);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(42, result.Value.GetProperty("value").GetInt32());
            Assert.AreEqual(0, registry.PendingRequestCount, "pending must be removed after match.");
        }

        [Test]
        public void MatchResponse_UnknownCorrelationId_ReturnsFalseAndIsNoOp()
        {
            using var registry = new RequestCorrelationRegistry();

            var matched = registry.MatchResponse("not-registered", Json("null"));

            Assert.IsFalse(matched);
            Assert.AreEqual(0, registry.PendingRequestCount);
        }

        [Test]
        public async Task RegisterPending_DefaultTimeoutFromOptions_FiresWhenNoResponse()
        {
            var options = new CoreIpcOptions
            {
                DefaultRequestTimeout = TimeSpan.FromMilliseconds(75),
            };
            using var registry = new RequestCorrelationRegistry(options);
            var id = registry.AllocateCorrelationId();

            var task = registry.RegisterPending(id);

            var result = await task.ConfigureAwait(false);

            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.RequestTimeout>(result.Error);
            var timeoutError = (CoreIpcError.RequestTimeout)result.Error!;
            Assert.AreEqual(TimeSpan.FromMilliseconds(75), timeoutError.Elapsed);
            Assert.AreEqual(0, registry.PendingRequestCount, "pending must be removed after timeout fires.");
        }

        [Test]
        public async Task RegisterPending_ExplicitTimeoutOverride_OverridesOptionsDefault()
        {
            // Default in options is intentionally large so that the explicit override is what triggers the timeout.
            var options = new CoreIpcOptions
            {
                DefaultRequestTimeout = TimeSpan.FromMinutes(10),
            };
            using var registry = new RequestCorrelationRegistry(options);
            var id = registry.AllocateCorrelationId();

            var explicitTimeout = TimeSpan.FromMilliseconds(60);
            var task = registry.RegisterPending(id, explicitTimeout);

            var result = await task.ConfigureAwait(false);

            Assert.IsFalse(result.Success);
            var timeoutError = result.Error as CoreIpcError.RequestTimeout;
            Assert.IsNotNull(timeoutError, "Explicit override must produce RequestTimeout.");
            Assert.AreEqual(explicitTimeout, timeoutError!.Elapsed,
                "RequestTimeout.Elapsed must reflect the explicit override, not the options default.");
        }

        [Test]
        public async Task MatchResponse_BeforeTimeoutFires_PreventsTimeoutCompletion()
        {
            using var registry = new RequestCorrelationRegistry(new CoreIpcOptions
            {
                DefaultRequestTimeout = TimeSpan.FromMilliseconds(150),
            });
            var id = registry.AllocateCorrelationId();
            var task = registry.RegisterPending(id);

            // Match immediately, before the timer would fire.
            registry.MatchResponse(id, Json("\"ok\""));
            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);

            var result = await task.ConfigureAwait(false);
            Assert.IsTrue(result.Success, "Match should win the race against a slow timer.");
            Assert.AreEqual("ok", result.Value.GetString());
            Assert.AreEqual(0, registry.PendingRequestCount);
        }

        [Test]
        public async Task FailPending_CompletesPendingWithProvidedError()
        {
            using var registry = new RequestCorrelationRegistry();
            var id = registry.AllocateCorrelationId();
            var task = registry.RegisterPending(id, TimeSpan.FromSeconds(10));

            var error = new CoreIpcError.TransportFailure("socket reset");
            var ok = registry.FailPending(id, error);

            Assert.IsTrue(ok);
            var result = await task.ConfigureAwait(false);
            Assert.IsFalse(result.Success);
            Assert.AreSame(error, result.Error);
            Assert.AreEqual(0, registry.PendingRequestCount);
        }

        [Test]
        public void FailPending_NullError_Throws()
        {
            using var registry = new RequestCorrelationRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.FailPending("abc", null!));
        }

        [Test]
        public async Task Dispose_CompletesAllPendingWithNotConnected()
        {
            var registry = new RequestCorrelationRegistry();
            var ids = new List<string>();
            var tasks = new List<Task<IpcResult<JsonElement>>>();
            for (int i = 0; i < 25; i++)
            {
                var id = registry.AllocateCorrelationId();
                ids.Add(id);
                tasks.Add(registry.RegisterPending(id, TimeSpan.FromMinutes(10)));
            }
            Assert.AreEqual(25, registry.PendingRequestCount);

            registry.Dispose();

            for (int i = 0; i < tasks.Count; i++)
            {
                var result = await tasks[i].ConfigureAwait(false);
                Assert.IsFalse(result.Success, "Pending #" + i + " must not succeed after dispose.");
                Assert.IsInstanceOf<CoreIpcError.NotConnected>(result.Error,
                    "Pending #" + i + " must be completed with NotConnected.");
            }
            Assert.AreEqual(0, registry.PendingRequestCount);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var registry = new RequestCorrelationRegistry();
            registry.Dispose();
            Assert.DoesNotThrow(() => registry.Dispose());
        }

        [Test]
        public async Task RegisterPending_AfterDispose_FailsImmediatelyWithNotConnected()
        {
            var registry = new RequestCorrelationRegistry();
            registry.Dispose();

            var task = registry.RegisterPending(registry.AllocateCorrelationId(), TimeSpan.FromSeconds(10));

            Assert.IsTrue(task.IsCompleted);
            var result = await task.ConfigureAwait(false);
            Assert.IsFalse(result.Success);
            Assert.IsInstanceOf<CoreIpcError.NotConnected>(result.Error);
        }

        [Test]
        public void RegisterPending_RejectsNullOrEmptyCorrelationId()
        {
            using var registry = new RequestCorrelationRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.RegisterPending(null!, TimeSpan.FromSeconds(1)));
            Assert.Throws<ArgumentException>(() => registry.RegisterPending(string.Empty, TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void RegisterPending_RejectsDuplicateCorrelationId()
        {
            using var registry = new RequestCorrelationRegistry();
            var id = registry.AllocateCorrelationId();
            _ = registry.RegisterPending(id, TimeSpan.FromMinutes(10));

            Assert.Throws<InvalidOperationException>(() =>
                registry.RegisterPending(id, TimeSpan.FromMinutes(10)));
        }

        [Test]
        public void RegisterPending_RejectsNegativeTimeoutOtherThanInfinite()
        {
            using var registry = new RequestCorrelationRegistry();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                registry.RegisterPending("abc", TimeSpan.FromMilliseconds(-2)));
        }

        [Test]
        public async Task RegisterPending_AlreadyCancelledToken_CompletesAsCancelledImmediately()
        {
            using var registry = new RequestCorrelationRegistry();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = registry.RegisterPending(registry.AllocateCorrelationId(),
                TimeSpan.FromSeconds(10), cts.Token);

            Assert.IsTrue(task.IsCanceled || task.IsCompleted);
            var ex = Assert.CatchAsync<OperationCanceledException>(async () => await task.ConfigureAwait(false));
            Assert.IsNotNull(ex);
            Assert.AreEqual(0, registry.PendingRequestCount);
        }

        [Test]
        public async Task RegisterPending_CancellationDuringWait_CompletesAsCancelledAndCleansUp()
        {
            using var registry = new RequestCorrelationRegistry();
            using var cts = new CancellationTokenSource();
            var id = registry.AllocateCorrelationId();
            var task = registry.RegisterPending(id, TimeSpan.FromSeconds(10), cts.Token);

            Assert.AreEqual(1, registry.PendingRequestCount);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await task.ConfigureAwait(false));

            // Allow the cleanup callback to settle even if it ran on a thread-pool thread.
            for (int i = 0; i < 50 && registry.PendingRequestCount > 0; i++)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.AreEqual(0, registry.PendingRequestCount);
        }

        [Test]
        public async Task CompleteOnMainThread_DelegateIsInvokedForResolution()
        {
            var posted = new ConcurrentQueue<Action>();
            using var registry = new RequestCorrelationRegistry(
                new CoreIpcOptions(),
                completeOnMainThread: posted.Enqueue);

            var id = registry.AllocateCorrelationId();
            var task = registry.RegisterPending(id, TimeSpan.FromSeconds(10));

            registry.MatchResponse(id, Json("\"hi\""));

            // The TCS completion is buffered into the "main thread" queue and not yet applied.
            Assert.IsFalse(task.IsCompleted, "Without draining the main thread, the task must remain pending.");
            Assert.AreEqual(1, posted.Count, "MatchResponse must post exactly one action to the main thread.");

            // Drain the main thread queue.
            while (posted.TryDequeue(out var action)) action();

            var result = await task.ConfigureAwait(false);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("hi", result.Value.GetString());
        }

        [Test]
        public async Task ConcurrentRequests_ResponseTimeoutAndCancel_AllResolveWithoutLeaks()
        {
            const int total = 100;
            var options = new CoreIpcOptions
            {
                DefaultRequestTimeout = TimeSpan.FromMilliseconds(500),
            };
            using var registry = new RequestCorrelationRegistry(options);

            var ids = new string[total];
            var tasks = new Task<IpcResult<JsonElement>>[total];
            var ctsList = new CancellationTokenSource[total];

            // i % 3 == 0: matched response
            // i % 3 == 1: timeout
            // i % 3 == 2: cancellation
            for (int i = 0; i < total; i++)
            {
                ids[i] = registry.AllocateCorrelationId();
                ctsList[i] = new CancellationTokenSource();
                tasks[i] = registry.RegisterPending(ids[i],
                    TimeSpan.FromMilliseconds(500), ctsList[i].Token);
            }

            Assert.AreEqual(total, registry.PendingRequestCount);

            // Settle: matches and cancels concurrently, leave timeouts to fire.
            var settle = new List<Task>();
            for (int i = 0; i < total; i++)
            {
                int idx = i;
                if (idx % 3 == 0)
                {
                    settle.Add(Task.Run(() => registry.MatchResponse(ids[idx], Json(idx.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
                }
                else if (idx % 3 == 2)
                {
                    settle.Add(Task.Run(() => ctsList[idx].Cancel()));
                }
            }
            await Task.WhenAll(settle).ConfigureAwait(false);

            // Wait for every task to terminate (success / fail / canceled).
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            for (int i = 0; i < total; i++)
            {
                while (!tasks[i].IsCompleted && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10).ConfigureAwait(false);
                }
                Assert.IsTrue(tasks[i].IsCompleted, "Task #" + i + " did not terminate within 5s.");
            }

            int responded = 0, timedOut = 0, cancelled = 0;
            for (int i = 0; i < total; i++)
            {
                if (tasks[i].IsCanceled)
                {
                    cancelled++;
                    continue;
                }
                Assert.IsFalse(tasks[i].IsFaulted, "Task #" + i + " faulted unexpectedly.");
                var r = await tasks[i].ConfigureAwait(false);
                if (r.Success) responded++;
                else if (r.Error is CoreIpcError.RequestTimeout) timedOut++;
                else Assert.Fail("Task #" + i + " resolved with unexpected error " + r.Error);
            }

            Assert.AreEqual(total / 3 + (total % 3 > 0 ? 1 : 0), responded, "Responded count mismatch.");
            Assert.AreEqual(total / 3 + (total % 3 > 1 ? 1 : 0), timedOut, "Timed-out count mismatch.");
            Assert.AreEqual(total / 3, cancelled, "Cancelled count mismatch.");
            Assert.AreEqual(0, registry.PendingRequestCount,
                "Every pending must be removed once it resolves (no leaks).");

            for (int i = 0; i < total; i++) ctsList[i].Dispose();
        }
    }
}
