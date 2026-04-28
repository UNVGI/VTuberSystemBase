#nullable enable
using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Transport.Loopback;

namespace VTuberSystemBase.CoreIpc.Tests.TestSupport
{
    internal static class LoopbackIntegrationHarness
    {
        public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan AssertTimeout = TimeSpan.FromSeconds(10);

        public static CoreIpcRuntimeHost NewLoopbackHost()
        {
            return new CoreIpcRuntimeHost(
                transportFactory: _ => new InMemoryLoopbackTransport(),
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: (delay, ct) =>
                    Task.Delay(TimeSpan.FromMilliseconds(20), ct));
        }

        public static CoreIpcOptions FastOptions(TimeSpan? defaultRequestTimeout = null) => new()
        {
            Host = "loopback",
            Port = 0,
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
            ReconnectMaxAttempts = 3,
            DefaultRequestTimeout = defaultRequestTimeout ?? TimeSpan.FromSeconds(5),
        };

        public static IEnumerator AwaitTask(Task task, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!task.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException(
                        $"Awaited task did not complete within {timeout}.");
                }
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? task.Exception!;
            }
        }

        public static IEnumerator WaitFor(
            Func<bool> condition,
            TimeSpan timeout,
            string failureMessage)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new AssertionException(failureMessage);
                }
                yield return null;
            }
        }

        public static IEnumerator WaitForConnected(
            CoreIpcRuntimeHost host,
            TimeSpan timeout)
        {
            yield return WaitFor(
                () => host.Bus.Diagnostics.CurrentState == ConnectionState.Connected,
                timeout,
                "Runtime never reached ConnectionState.Connected within the timeout " +
                $"(current state was {host.Bus.Diagnostics.CurrentState}).");
        }

        public static IEnumerator InitializeAndAwaitConnected(
            CoreIpcRuntimeHost host,
            CoreIpcOptions options)
        {
            var initTask = host.InitializeAsync(options);
            yield return AwaitTask(initTask, StartupTimeout);
            Assert.AreEqual(RuntimeState.Running, host.State);
            yield return WaitForConnected(host, StartupTimeout);
        }
    }
}
