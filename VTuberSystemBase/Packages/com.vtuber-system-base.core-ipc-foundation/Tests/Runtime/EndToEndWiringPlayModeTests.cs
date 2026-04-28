#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class EndToEndWiringPlayModeTests
    {
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan AssertTimeout = TimeSpan.FromSeconds(10);

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

        private static int FindFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static CoreIpcRuntimeHost NewWebSocketHost(int port)
        {
            ITransportAdapter Factory(CoreIpcOptions opts) =>
                new WebSocketTransportAdapter(
                    new SystemTextJsonCodec(opts),
                    new WebSocketServerOptions
                    {
                        MaxConcurrentClients = 4,
                        PingInterval = TimeSpan.Zero,
                        PongTimeout = TimeSpan.FromSeconds(60),
                        CloseTimeout = TimeSpan.FromSeconds(2),
                        HandshakeTimeout = TimeSpan.FromSeconds(5),
                    },
                    new WebSocketClientOptions
                    {
                        CloseTimeout = TimeSpan.FromSeconds(2),
                    });

            return new CoreIpcRuntimeHost(
                transportFactory: Factory,
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: (delay, ct) =>
                    Task.Delay(TimeSpan.FromMilliseconds(20), ct));
        }

        private static CoreIpcOptions FastOptions(int port) => new()
        {
            Host = "127.0.0.1",
            Port = port,
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
            ReconnectMaxAttempts = 3,
            DefaultRequestTimeout = TimeSpan.FromSeconds(5),
        };

        private static IEnumerator AwaitTask(Task task, TimeSpan timeout)
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

        private static IEnumerator WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
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

        private static IEnumerator WaitForConnected(CoreIpcRuntimeHost host, TimeSpan timeout)
        {
            yield return WaitFor(
                () => host.Bus.Diagnostics.CurrentState == ConnectionState.Connected,
                timeout,
                "Runtime never reached ConnectionState.Connected within the timeout " +
                $"(current state was {host.Bus.Diagnostics.CurrentState}).");
        }

        [UnityTest]
        public IEnumerator PublishState_DeliveredToSubscriber_OverWebSocketLoopback()
        {
            int port = FindFreeTcpPort();
            var host = NewWebSocketHost(port);

            var initCts = new CancellationTokenSource(StartupTimeout);
            var initTask = host.InitializeAsync(FastOptions(port), initCts.Token);
            yield return AwaitTask(initTask, StartupTimeout);
            initCts.Dispose();

            Assert.AreEqual(RuntimeState.Running, host.State);

            yield return WaitForConnected(host, StartupTimeout);

            SamplePayload? received = null;
            using var subscription = host.Bus.SubscribeState<SamplePayload>(
                "topic/state",
                payload => received = payload);

            var publishResult = host.Bus.PublishState("topic/state", new SamplePayload(42, "alpha"));
            Assert.IsTrue(publishResult.Success,
                "PublishState should succeed when client is connected; got " + publishResult.Error);

            yield return WaitFor(
                () => received is not null,
                AssertTimeout,
                "State payload was not delivered to the subscriber within the timeout.");

            Assert.AreEqual(42, received!.Id);
            Assert.AreEqual("alpha", received.Name);

            host.Dispose();
            Assert.AreEqual(RuntimeState.Disposed, host.State);
        }

        [UnityTest]
        public IEnumerator PublishEvent_FifoOrderPreservedAcrossFrames_OverWebSocketLoopback()
        {
            int port = FindFreeTcpPort();
            var host = NewWebSocketHost(port);

            var initCts = new CancellationTokenSource(StartupTimeout);
            var initTask = host.InitializeAsync(FastOptions(port), initCts.Token);
            yield return AwaitTask(initTask, StartupTimeout);
            initCts.Dispose();

            yield return WaitForConnected(host, StartupTimeout);

            const int total = 32;
            var received = new List<int>();
            using var subscription = host.Bus.SubscribeEvent<int>(
                "topic/event",
                payload => received.Add(payload));

            for (int i = 0; i < total; i++)
            {
                var r = host.Bus.PublishEvent("topic/event", i);
                Assert.IsTrue(r.Success, "PublishEvent " + i + " failed: " + r.Error);
            }

            yield return WaitFor(
                () => received.Count >= total,
                AssertTimeout,
                $"Expected {total} events, but only received {received.Count} within the timeout.");

            Assert.AreEqual(total, received.Count);
            for (int i = 0; i < total; i++)
            {
                Assert.AreEqual(i, received[i],
                    "Events must arrive in FIFO order; index " + i + " was " + received[i]);
            }

            host.Dispose();
        }

        [UnityTest]
        public IEnumerator RequestAsync_ReceivesResponseFromMainThreadHandler_OverWebSocketLoopback()
        {
            int port = FindFreeTcpPort();
            var host = NewWebSocketHost(port);

            var initCts = new CancellationTokenSource(StartupTimeout);
            var initTask = host.InitializeAsync(FastOptions(port), initCts.Token);
            yield return AwaitTask(initTask, StartupTimeout);
            initCts.Dispose();

            yield return WaitForConnected(host, StartupTimeout);

            using var registration = host.Bus.RegisterRequestHandler<SamplePayload, SamplePayload>(
                "topic/rpc",
                (req, _) => Task.FromResult(new SamplePayload(req.Id * 2, req.Name + "!")));

            var requestTask = host.Bus.RequestAsync<SamplePayload, SamplePayload>(
                "topic/rpc",
                new SamplePayload(7, "hello"));

            yield return AwaitTask(requestTask, AssertTimeout);

            var result = requestTask.Result;
            Assert.IsTrue(result.Success,
                "RequestAsync should succeed end-to-end; got error " + result.Error);
            Assert.AreEqual(14, result.Value!.Id);
            Assert.AreEqual("hello!", result.Value!.Name);

            host.Dispose();
        }
    }
}
