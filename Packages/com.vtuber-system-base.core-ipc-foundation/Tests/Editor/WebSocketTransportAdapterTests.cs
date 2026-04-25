#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class WebSocketTransportAdapterTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

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

        private static WebSocketServerOptions FastServerOptions() =>
            new WebSocketServerOptions
            {
                MaxConcurrentClients = 4,
                PingInterval = TimeSpan.Zero,
                CloseTimeout = TimeSpan.FromSeconds(2),
                HandshakeTimeout = TimeSpan.FromSeconds(5),
            };

        private static WebSocketClientOptions FastClientOptions() =>
            new WebSocketClientOptions
            {
                CloseTimeout = TimeSpan.FromSeconds(2),
            };

        private static IMessageCodec NewCodec() => new SystemTextJsonCodec();

        [Test]
        public void Constructor_NullCodec_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(
                () => new WebSocketTransportAdapter(null!));
        }

        [Test]
        public async Task StartServerAsync_OnFreePort_TransitionsToRunning()
        {
            int port = FindFreeTcpPort();
            await using var adapter = new WebSocketTransportAdapter(
                NewCodec(), FastServerOptions(), FastClientOptions());

            using var cts = new CancellationTokenSource(TestTimeout);
            await adapter.StartServerAsync(
                new ServerBindOptions("127.0.0.1", port), cts.Token);

            Assert.IsTrue(adapter.IsServerRunning);
            Assert.AreEqual(port, adapter.BoundPort);
        }

        [Test]
        public async Task StartServerAsync_TwiceOnSameInstance_ThrowsInvalidOperation()
        {
            int port = FindFreeTcpPort();
            await using var adapter = new WebSocketTransportAdapter(
                NewCodec(), FastServerOptions(), FastClientOptions());

            using var cts = new CancellationTokenSource(TestTimeout);
            await adapter.StartServerAsync(
                new ServerBindOptions("127.0.0.1", port), cts.Token);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await adapter.StartServerAsync(
                    new ServerBindOptions("127.0.0.1", port), cts.Token));
        }

        [Test]
        public async Task StartServerAsync_OnHeldPort_ThrowsTransportExceptionWithPortInUse()
        {
            int port = FindFreeTcpPort();

            var blocker = new TcpListener(IPAddress.Loopback, port);
            blocker.Server.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ExclusiveAddressUse,
                true);
            blocker.Start();
            try
            {
                await using var adapter = new WebSocketTransportAdapter(
                    NewCodec(), FastServerOptions(), FastClientOptions());

                using var cts = new CancellationTokenSource(TestTimeout);

                CoreIpcTransportException? caught = null;
                try
                {
                    await adapter.StartServerAsync(
                        new ServerBindOptions("127.0.0.1", port), cts.Token);
                }
                catch (CoreIpcTransportException ex)
                {
                    caught = ex;
                }

                Assert.IsNotNull(caught, "Expected CoreIpcTransportException to be thrown.");
                Assert.IsInstanceOf<CoreIpcError.PortInUse>(caught!.IpcError);
                Assert.IsFalse(adapter.IsServerRunning);
            }
            finally
            {
                blocker.Stop();
            }
        }

        [Test]
        public async Task ConnectClientAsync_AgainstAdapterServer_Succeeds()
        {
            int port = FindFreeTcpPort();
            await using var adapter = new WebSocketTransportAdapter(
                NewCodec(), FastServerOptions(), FastClientOptions());

            var connectedTcs = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            adapter.ClientConnected += c => connectedTcs.TrySetResult(c);

            using var cts = new CancellationTokenSource(TestTimeout);
            await adapter.StartServerAsync(
                new ServerBindOptions("127.0.0.1", port), cts.Token);

            var clientConnection = await adapter.ConnectClientAsync(
                new ClientBindOptions("127.0.0.1", port, TimeSpan.FromSeconds(5)),
                cts.Token);

            Assert.IsNotNull(clientConnection);

            // Both ends should also see the server-side connection.
            var completed = await Task.WhenAny(
                connectedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.AreSame(connectedTcs.Task, completed,
                "ClientConnected was not raised within the test timeout.");
        }

        [Test]
        public async Task ConnectClientAsync_NoServer_ThrowsTransportException()
        {
            int port = FindFreeTcpPort();
            await using var adapter = new WebSocketTransportAdapter(
                NewCodec(),
                FastServerOptions(),
                new WebSocketClientOptions { CloseTimeout = TimeSpan.FromSeconds(1) });

            using var cts = new CancellationTokenSource(TestTimeout);

            CoreIpcTransportException? caught = null;
            try
            {
                await adapter.ConnectClientAsync(
                    new ClientBindOptions("127.0.0.1", port, TimeSpan.FromSeconds(1)),
                    cts.Token);
            }
            catch (CoreIpcTransportException ex)
            {
                caught = ex;
            }

            Assert.IsNotNull(caught, "Expected CoreIpcTransportException for connect failure.");
            Assert.IsInstanceOf<CoreIpcError.TransportFailure>(caught!.IpcError);
        }

        [Test]
        public async Task SendReceive_AcrossAdapter_RoundTripsTextPayload()
        {
            int port = FindFreeTcpPort();
            await using var adapter = new WebSocketTransportAdapter(
                NewCodec(), FastServerOptions(), FastClientOptions());

            var connectedTcs = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            adapter.ClientConnected += c => connectedTcs.TrySetResult(c);

            using var cts = new CancellationTokenSource(TestTimeout);
            await adapter.StartServerAsync(
                new ServerBindOptions("127.0.0.1", port), cts.Token);

            var client = await adapter.ConnectClientAsync(
                new ClientBindOptions("127.0.0.1", port, TimeSpan.FromSeconds(5)),
                cts.Token);

            var serverConnection = await connectedTcs.Task;

            byte[] payload = Encoding.UTF8.GetBytes("envelope-bytes-as-text-frame");
            var receiveTask = Task.Run(async () =>
            {
                await foreach (var p in serverConnection
                    .ReceiveAsync(cts.Token).WithCancellation(cts.Token))
                {
                    return p;
                }
                throw new InvalidOperationException("Server connection completed without payload.");
            });

            await client.SendAsync(payload, cts.Token);

            var received = await receiveTask;
            Assert.AreEqual(
                Encoding.UTF8.GetString(payload),
                Encoding.UTF8.GetString(received.ToArray()));
        }

        [Test]
        public async Task DisposeAsync_IsIdempotent_AndReleasesPort()
        {
            int port = FindFreeTcpPort();
            var adapter = new WebSocketTransportAdapter(
                NewCodec(), FastServerOptions(), FastClientOptions());

            using var cts = new CancellationTokenSource(TestTimeout);
            await adapter.StartServerAsync(
                new ServerBindOptions("127.0.0.1", port), cts.Token);

            await adapter.DisposeAsync();
            // Second dispose should be a no-op.
            await adapter.DisposeAsync();

            // After dispose, calling APIs should reject.
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await adapter.StartServerAsync(
                    new ServerBindOptions("127.0.0.1", port), CancellationToken.None));

            // Port should now be re-bindable by another listener.
            var rebinder = new TcpListener(IPAddress.Loopback, port);
            try
            {
                rebinder.Start();
                Assert.Pass();
            }
            finally
            {
                rebinder.Stop();
            }
        }
    }
}
