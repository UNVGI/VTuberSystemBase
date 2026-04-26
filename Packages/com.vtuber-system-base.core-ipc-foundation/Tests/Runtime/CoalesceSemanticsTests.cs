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
    public sealed class CoalesceSemanticsTests
    {
        [SetUp]
        public void SetUp()
        {
            // RuntimeBootstrap.OnBeforeSceneLoad auto-starts a CoreIpcRuntime and
            // installs an IpcDispatchStep into PlayerLoop on every PlayMode entry.
            // In batch test runs this leaked state races with the loopback host
            // this fixture sets up — leaving the test's bus undispatched and the
            // first PlayMode test hanging. Wipe both before every test so we get
            // the same clean slate single-test runs already enjoy.
            CoreIpcRuntime.ResetForTesting();
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
        }

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
        public IEnumerator StateCoalesce_OneHundredRapidPublishes_DeliversLatestAndDropsIntermediate()
        {
            const int total = 100;
            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, LoopbackIntegrationHarness.FastOptions());

            var received = new List<int>();
            using var subscription = host.Bus.SubscribeState<int>(
                "topic/coalesce",
                payload => received.Add(payload));

            for (int i = 0; i < total; i++)
            {
                var r = host.Bus.PublishState("topic/coalesce", i);
                Assert.IsTrue(r.Success, "PublishState " + i + " failed: " + r.Error);
            }

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received.Count > 0 && received[received.Count - 1] == total - 1,
                LoopbackIntegrationHarness.AssertTimeout,
                "Latest state value (" + (total - 1) + ") was not delivered within the timeout. " +
                "received=" + string.Join(",", received));

            Assert.AreEqual(total - 1, received[received.Count - 1],
                "The most recent state delivery must carry the latest published value.");
            Assert.Less(received.Count, total,
                "Coalesce semantics must drop intermediate updates; expected fewer than " +
                total + " deliveries but observed " + received.Count + ".");

            host.Dispose();
        }
    }
}
