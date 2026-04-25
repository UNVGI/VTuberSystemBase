#nullable enable
using System.Collections;
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
    public sealed class LoopbackRoundTripTests
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

        private sealed class SamplePayload
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;

            public SamplePayload() { }

            public SamplePayload(int id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        [UnityTest]
        public IEnumerator StateRoundTrip_DeliversToSubscriber_OverInMemoryLoopback()
        {
            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, LoopbackIntegrationHarness.FastOptions());

            SamplePayload? received = null;
            using var subscription = host.Bus.SubscribeState<SamplePayload>(
                "topic/state",
                payload => received = payload);

            var publishResult = host.Bus.PublishState(
                "topic/state",
                new SamplePayload(101, "alpha"));
            Assert.IsTrue(publishResult.Success,
                "PublishState must succeed when client is connected; got " + publishResult.Error);

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received is not null,
                LoopbackIntegrationHarness.AssertTimeout,
                "State payload was not delivered to the subscriber within the timeout.");

            Assert.AreEqual(101, received!.Id);
            Assert.AreEqual("alpha", received.Name);

            host.Dispose();
            Assert.AreEqual(RuntimeState.Disposed, host.State);
        }

        [UnityTest]
        public IEnumerator EventRoundTrip_DeliversToSubscriber_OverInMemoryLoopback()
        {
            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, LoopbackIntegrationHarness.FastOptions());

            int? received = null;
            using var subscription = host.Bus.SubscribeEvent<int>(
                "topic/event",
                payload => received = payload);

            var publishResult = host.Bus.PublishEvent("topic/event", 7);
            Assert.IsTrue(publishResult.Success,
                "PublishEvent must succeed when client is connected; got " + publishResult.Error);

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received.HasValue,
                LoopbackIntegrationHarness.AssertTimeout,
                "Event payload was not delivered to the subscriber within the timeout.");

            Assert.AreEqual(7, received!.Value);

            host.Dispose();
        }

        [UnityTest]
        public IEnumerator RequestResponseRoundTrip_CompletesViaCorrelation_OverInMemoryLoopback()
        {
            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, LoopbackIntegrationHarness.FastOptions());

            using var registration = host.Bus.RegisterRequestHandler<SamplePayload, SamplePayload>(
                "topic/rpc",
                (req, _) => Task.FromResult(new SamplePayload(req.Id * 3, req.Name + "-pong")));

            var requestTask = host.Bus.RequestAsync<SamplePayload, SamplePayload>(
                "topic/rpc",
                new SamplePayload(11, "ping"));

            yield return LoopbackIntegrationHarness.AwaitTask(
                requestTask, LoopbackIntegrationHarness.AssertTimeout);

            var result = requestTask.Result;
            Assert.IsTrue(result.Success,
                "RequestAsync should succeed end-to-end via in-memory loopback; got error " +
                result.Error);
            Assert.AreEqual(33, result.Value!.Id);
            Assert.AreEqual("ping-pong", result.Value!.Name);

            host.Dispose();
        }
    }
}
