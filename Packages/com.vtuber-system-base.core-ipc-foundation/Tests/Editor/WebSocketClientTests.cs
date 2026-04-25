#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class WebSocketClientTests
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

        private static WebSocketServerOptions FastServerOptions(int maxClients = 16) =>
            new WebSocketServerOptions
            {
                MaxConcurrentClients = maxClients,
                PingInterval = TimeSpan.Zero,
                PongTimeout = TimeSpan.FromSeconds(60),
                CloseTimeout = TimeSpan.FromSeconds(2),
                HandshakeTimeout = TimeSpan.FromSeconds(5),
            };

        private static ClientBindOptions BindOptions(int port, double connectSeconds = 5.0) =>
            new ClientBindOptions(
                "127.0.0.1",
                port,
                TimeSpan.FromSeconds(connectSeconds));

        private static async Task<IClientConnection> WaitForServerConnectionAsync(
            TaskCompletionSource<IClientConnection> tcs,
            CancellationToken ct)
        {
            var completed = await Task.WhenAny(
                tcs.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                throw new TimeoutException(
                    "Timed out waiting for server-side ClientConnected to fire.");
            }
            return await tcs.Task.ConfigureAwait(false);
        }

        private static async Task<ReadOnlyMemory<byte>> ReceiveFirstAsync(
            IClientConnection connection,
            CancellationToken ct)
        {
            await foreach (var payload in connection.ReceiveAsync(ct).WithCancellation(ct))
            {
                return payload;
            }
            throw new InvalidOperationException(
                "Connection completed without producing any payload.");
        }

        [Test]
        public async Task ConnectAsync_AgainstRunningServer_Succeeds()
        {
            int port = FindFreeTcpPort();
            await using var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastServerOptions());
            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success, $"server start failed: {startResult.Error?.Code}");

            await using var client = new WebSocketClient(BindOptions(port));
            using var cts = new CancellationTokenSource(TestTimeout);
            var connectResult = await client.ConnectAsync(cts.Token);

            Assert.IsTrue(connectResult.Success, $"connect failed: {connectResult.Error?.Code}");
            Assert.IsTrue(client.IsConnected);
            Assert.AreEqual($"127.0.0.1:{port}", client.RemoteEndpoint);
        }

        [Test]
        public async Task ConnectAsync_NoServer_ReturnsTransportFailure()
        {
            int port = FindFreeTcpPort();
            // Do NOT start a server on this port.
            await using var client = new WebSocketClient(BindOptions(port, connectSeconds: 1.0));
            using var cts = new CancellationTokenSource(TestTimeout);

            var connectResult = await client.ConnectAsync(cts.Token);

            Assert.IsFalse(connectResult.Success, "Expected ConnectAsync to fail when no server is listening.");
            Assert.IsInstanceOf<CoreIpcError.TransportFailure>(connectResult.Error);
            Assert.IsFalse(client.IsConnected);
        }

        [Test]
        public async Task ConnectAsync_TwiceOnSameInstance_ThrowsInvalidOperation()
        {
            int port = FindFreeTcpPort();
            await using var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastServerOptions());
            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success);

            await using var client = new WebSocketClient(BindOptions(port));
            using var cts = new CancellationTokenSource(TestTimeout);
            var first = await client.ConnectAsync(cts.Token);
            Assert.IsTrue(first.Success);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client.ConnectAsync(cts.Token));
        }

        [Test]
        public async Task SendReceive_TextRoundTrip_DeliversBothDirections()
        {
            int port = FindFreeTcpPort();
            await using var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastServerOptions());

            var connectedTcs = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            server.ClientConnected += c => connectedTcs.TrySetResult(c);

            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success);

            await using var client = new WebSocketClient(BindOptions(port));
            using var cts = new CancellationTokenSource(TestTimeout);
            var connectResult = await client.ConnectAsync(cts.Token);
            Assert.IsTrue(connectResult.Success);

            var serverConnection = await WaitForServerConnectionAsync(connectedTcs, cts.Token);

            // Client -> Server
            var serverInboundTask = ReceiveFirstAsync(serverConnection, cts.Token);
            byte[] clientPayload = Encoding.UTF8.GetBytes("hello-from-client");
            await client.SendAsync(clientPayload, cts.Token);
            var receivedByServer = await serverInboundTask;
            Assert.AreEqual(
                "hello-from-client",
                Encoding.UTF8.GetString(receivedByServer.ToArray()));

            // Server -> Client
            var clientInboundTask = ReceiveFirstAsync(client, cts.Token);
            byte[] serverPayload = Encoding.UTF8.GetBytes("hello-from-server");
            await serverConnection.SendAsync(serverPayload, cts.Token);
            var receivedByClient = await clientInboundTask;
            Assert.AreEqual(
                "hello-from-server",
                Encoding.UTF8.GetString(receivedByClient.ToArray()));
        }

        [Test]
        public async Task DisposeAsync_AfterConnect_ClosesConnectionGracefully()
        {
            int port = FindFreeTcpPort();
            await using var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastServerOptions());

            var connectedTcs = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var disconnectedTcs = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            server.ClientConnected += c => connectedTcs.TrySetResult(c);
            server.ClientDisconnected += c => disconnectedTcs.TrySetResult(c);

            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success);

            var client = new WebSocketClient(BindOptions(port));
            using var cts = new CancellationTokenSource(TestTimeout);

            var connectResult = await client.ConnectAsync(cts.Token);
            Assert.IsTrue(connectResult.Success);
            await WaitForServerConnectionAsync(connectedTcs, cts.Token);

            WebSocketClient.DisconnectReason? observed = null;
            client.Disconnected += reason => observed = reason;

            await client.DisposeAsync();

            Assert.IsFalse(client.IsConnected);
            // Server should observe disconnect from the closed client.
            var completed = await Task.WhenAny(
                disconnectedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.AreSame(disconnectedTcs.Task, completed,
                "Server did not observe disconnect within the test timeout.");
            Assert.IsTrue(observed.HasValue, "Disconnected event was not raised.");

            // Idempotent dispose
            await client.DisposeAsync();
        }

        [Test]
        public async Task ServerInitiatedClose_RaisesDisconnectedAndCompletesReceive()
        {
            int port = FindFreeTcpPort();
            await using var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastServerOptions());

            var connectedTcs = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            server.ClientConnected += c => connectedTcs.TrySetResult(c);

            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success);

            await using var client = new WebSocketClient(BindOptions(port));
            using var cts = new CancellationTokenSource(TestTimeout);

            var connectResult = await client.ConnectAsync(cts.Token);
            Assert.IsTrue(connectResult.Success);
            var serverConnection = await WaitForServerConnectionAsync(connectedTcs, cts.Token);

            var disconnectedTcs = new TaskCompletionSource<WebSocketClient.DisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            client.Disconnected += reason => disconnectedTcs.TrySetResult(reason);

            // Capture the receive iterator so we can assert it terminates.
            var receiveTask = Task.Run(async () =>
            {
                int count = 0;
                await foreach (var _ in client.ReceiveAsync(cts.Token).WithCancellation(cts.Token))
                {
                    count++;
                }
                return count;
            });

            await serverConnection.DisposeAsync();

            var completed = await Task.WhenAny(
                disconnectedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.AreSame(disconnectedTcs.Task, completed,
                "Disconnected event was not raised when server initiated close.");
            var reason = await disconnectedTcs.Task;
            Assert.That(
                reason,
                Is.EqualTo(WebSocketClient.DisconnectReason.PeerClose).Or
                    .EqualTo(WebSocketClient.DisconnectReason.TransportError));

            // Receive iterator should terminate within timeout once the queue completes.
            var receiveCount = await receiveTask;
            Assert.GreaterOrEqual(receiveCount, 0);
            Assert.IsFalse(client.IsConnected);
        }

        [Test]
        public async Task SendAsync_BeforeConnect_ThrowsInvalidOperation()
        {
            int port = FindFreeTcpPort();
            await using var client = new WebSocketClient(BindOptions(port));
            byte[] payload = Encoding.UTF8.GetBytes("never-sent");
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client.SendAsync(payload, CancellationToken.None));
        }

        [Test]
        public async Task SendAsync_AfterDispose_ThrowsObjectDisposed()
        {
            int port = FindFreeTcpPort();
            await using var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastServerOptions());
            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success);

            var client = new WebSocketClient(BindOptions(port));
            using var cts = new CancellationTokenSource(TestTimeout);
            var connectResult = await client.ConnectAsync(cts.Token);
            Assert.IsTrue(connectResult.Success);

            await client.DisposeAsync();

            byte[] payload = Encoding.UTF8.GetBytes("after-dispose");
            Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await client.SendAsync(payload, CancellationToken.None));
        }

        [Test]
        public async Task ReconnectFlow_NewInstancePerAttempt_Works()
        {
            int port = FindFreeTcpPort();
            await using var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastServerOptions());
            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success);

            // First connection
            var client1 = new WebSocketClient(BindOptions(port));
            using (var cts1 = new CancellationTokenSource(TestTimeout))
            {
                var r = await client1.ConnectAsync(cts1.Token);
                Assert.IsTrue(r.Success);
                Assert.IsTrue(client1.IsConnected);
            }
            await client1.DisposeAsync();
            Assert.IsFalse(client1.IsConnected);

            // Second connection (new instance) – design says reuse is forbidden;
            // creating a new instance should still succeed against the same server.
            var client2 = new WebSocketClient(BindOptions(port));
            using (var cts2 = new CancellationTokenSource(TestTimeout))
            {
                var r = await client2.ConnectAsync(cts2.Token);
                Assert.IsTrue(r.Success);
                Assert.IsTrue(client2.IsConnected);
            }
            await client2.DisposeAsync();
        }

        [Test]
        public async Task ConnectAsync_RespectsConnectTimeout_OnUnreachableEndpoint()
        {
            // Use a port we know nobody listens on (fresh random free port,
            // immediately reused by no one because we never start a server).
            int port = FindFreeTcpPort();

            await using var client = new WebSocketClient(
                new ClientBindOptions("127.0.0.1", port, TimeSpan.FromMilliseconds(250)));
            using var cts = new CancellationTokenSource(TestTimeout);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var connectResult = await client.ConnectAsync(cts.Token);
            sw.Stop();

            Assert.IsFalse(connectResult.Success);
            Assert.IsInstanceOf<CoreIpcError.TransportFailure>(connectResult.Error);
            // Sanity: should fail well before the test deadline; we don't hard-bound
            // the lower edge to avoid CI timing flakiness, just the upper edge.
            Assert.Less(sw.Elapsed, TestTimeout);
        }
    }
}
