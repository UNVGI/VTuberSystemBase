#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Transport.Loopback;
using VTuberSystemBase.CoreIpc.Tests.TestSupport;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class MessageSizeLimitTests
    {
        private const long TestMaxMessageSizeBytes = 4096L;

        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
        }

        private static CoreIpcOptions OptionsWithSizeLimit(long maxBytes) => new()
        {
            Host = "loopback",
            Port = 0,
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
            ReconnectMaxAttempts = 3,
            DefaultRequestTimeout = TimeSpan.FromSeconds(2),
            MaxMessageSizeBytes = maxBytes,
        };

        [UnityTest]
        public IEnumerator PublishState_PayloadExceedsLimit_ReturnsSizeLimitExceeded()
        {
            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, OptionsWithSizeLimit(TestMaxMessageSizeBytes));

            var oversize = new string('x', (int)TestMaxMessageSizeBytes + 1024);

            var result = host.Bus.PublishState("topic/oversize", oversize);

            Assert.IsFalse(result.Success,
                "PublishState must reject payloads whose encoded form exceeds " +
                "MaxMessageSizeBytes.");
            Assert.IsInstanceOf<CoreIpcError.SizeLimitExceeded>(result.Error,
                "Send-side rejection must surface as SizeLimitExceeded; was " + result.Error);

            var sizeError = (CoreIpcError.SizeLimitExceeded)result.Error!;
            Assert.AreEqual(TestMaxMessageSizeBytes, sizeError.LimitBytes,
                "LimitBytes should reflect the configured MaxMessageSizeBytes.");
            Assert.Greater(sizeError.ActualBytes, TestMaxMessageSizeBytes,
                "ActualBytes must exceed the limit.");

            host.Dispose();
        }

        [UnityTest]
        public IEnumerator PublishState_PayloadAtBoundary_StillSucceeds()
        {
            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, OptionsWithSizeLimit(TestMaxMessageSizeBytes));

            string received = string.Empty;
            using var subscription = host.Bus.SubscribeState<string>(
                "topic/under-limit",
                payload => received = payload);

            var smallPayload = new string('y', 64);

            var result = host.Bus.PublishState("topic/under-limit", smallPayload);
            Assert.IsTrue(result.Success,
                "Small payloads must succeed; got " + result.Error);

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received.Length == smallPayload.Length,
                LoopbackIntegrationHarness.AssertTimeout,
                "Subscriber should observe the under-limit payload.");

            Assert.AreEqual(smallPayload, received);

            host.Dispose();
        }

        [UnityTest]
        public IEnumerator InboundFrame_OverSizeLimit_DroppedAndLogged()
        {
            var transport = new InMemoryLoopbackTransport();

            IClientConnection? serverSide = null;
            transport.ClientConnected += conn => serverSide = conn;

            var warningSync = new object();
            var warnings = new List<string>();

            var host = new CoreIpcRuntimeHost(
                transportFactory: _ => transport,
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: (delay, ct) =>
                    Task.Delay(TimeSpan.FromMilliseconds(20), ct),
                logWarning: msg =>
                {
                    lock (warningSync) warnings.Add(msg);
                });

            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, OptionsWithSizeLimit(TestMaxMessageSizeBytes));

            Assert.IsNotNull(serverSide,
                "Server-side connection must be captured for inbound injection.");

            int handlerInvocations = 0;
            using var subscription = host.Bus.SubscribeEvent<int>(
                "topic/inbound-oversize",
                _ => Interlocked.Increment(ref handlerInvocations));

            var oversize = new byte[TestMaxMessageSizeBytes + 1024];
            for (int i = 0; i < oversize.Length; i++)
            {
                oversize[i] = (byte)('a' + (i % 26));
            }

            var sendTask = serverSide!.SendAsync(oversize, CancellationToken.None).AsTask();
            yield return LoopbackIntegrationHarness.AwaitTask(
                sendTask, LoopbackIntegrationHarness.AssertTimeout);

            yield return LoopbackIntegrationHarness.WaitFor(
                () =>
                {
                    lock (warningSync)
                    {
                        foreach (var msg in warnings)
                        {
                            if (msg.Contains("dropping inbound message")
                                && msg.Contains("Message size"))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                },
                LoopbackIntegrationHarness.AssertTimeout,
                "An over-size inbound frame must produce a warning log entry mentioning " +
                "'dropping inbound message' and the size error.");

            // Pump the dispatch loop a few additional frames to confirm the dropped frame
            // never reaches the subscriber.
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, Volatile.Read(ref handlerInvocations),
                "Subscribers must not be invoked for dropped over-size frames.");

            host.Dispose();
        }
    }
}
