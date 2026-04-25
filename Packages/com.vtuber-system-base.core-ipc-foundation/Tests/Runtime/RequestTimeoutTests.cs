#nullable enable
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Tests.TestSupport;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class RequestTimeoutTests
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

        [Test]
        public void DefaultRequestTimeoutValue_IsFiveSeconds()
        {
            var options = new CoreIpcOptions();
            Assert.AreEqual(TimeSpan.FromSeconds(5), options.DefaultRequestTimeout,
                "CoreIpcOptions.DefaultRequestTimeout must default to 5 seconds.");
        }

        [UnityTest]
        public IEnumerator RequestAsync_WithNoOverride_FiresTimeoutFromConfiguredDefault()
        {
            var defaultTimeout = TimeSpan.FromMilliseconds(300);
            var options = LoopbackIntegrationHarness.FastOptions(defaultTimeout);

            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(host, options);

            var requestTask = host.Bus.RequestAsync<int, int>("topic/rpc/never-handled", 1);

            yield return LoopbackIntegrationHarness.AwaitTask(
                requestTask,
                defaultTimeout + TimeSpan.FromSeconds(2));

            var result = requestTask.Result;
            Assert.IsFalse(result.Success,
                "RequestAsync must fail when no handler is registered (timeout expected).");
            Assert.IsInstanceOf<CoreIpcError.RequestTimeout>(result.Error,
                "Failure error must be RequestTimeout but was " + result.Error);

            var timeoutError = (CoreIpcError.RequestTimeout)result.Error!;
            Assert.AreEqual(defaultTimeout, timeoutError.Elapsed,
                "Timeout duration must match the configured default request timeout.");

            host.Dispose();
        }

        [UnityTest]
        public IEnumerator RequestAsync_WithOverride_FiresTimeoutBeforeDefault()
        {
            var defaultTimeout = TimeSpan.FromSeconds(10);
            var overrideTimeout = TimeSpan.FromMilliseconds(250);

            var options = LoopbackIntegrationHarness.FastOptions(defaultTimeout);

            var host = LoopbackIntegrationHarness.NewLoopbackHost();
            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(host, options);

            var startedAt = DateTime.UtcNow;
            var requestTask = host.Bus.RequestAsync<int, int>(
                "topic/rpc/never-handled",
                1,
                options: new RequestOptions(overrideTimeout));

            yield return LoopbackIntegrationHarness.AwaitTask(
                requestTask,
                overrideTimeout + TimeSpan.FromSeconds(2));

            var elapsed = DateTime.UtcNow - startedAt;
            var result = requestTask.Result;

            Assert.IsFalse(result.Success,
                "RequestAsync must fail when no handler is registered (timeout expected).");
            Assert.IsInstanceOf<CoreIpcError.RequestTimeout>(result.Error,
                "Failure error must be RequestTimeout but was " + result.Error);

            var timeoutError = (CoreIpcError.RequestTimeout)result.Error!;
            Assert.AreEqual(overrideTimeout, timeoutError.Elapsed,
                "Timeout duration must match the per-request override.");
            Assert.Less(elapsed, defaultTimeout,
                "Override timeout must fire well before the configured default (default=" +
                defaultTimeout + ", elapsed=" + elapsed + ").");

            host.Dispose();
        }
    }
}
