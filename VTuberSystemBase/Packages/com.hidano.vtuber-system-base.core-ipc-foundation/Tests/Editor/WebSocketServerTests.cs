#nullable enable
using System;
using System.Collections.Concurrent;
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
    public sealed class WebSocketServerTests
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

        private static WebSocketServerOptions FastTestOptions(int maxClients = 16) =>
            new WebSocketServerOptions
            {
                MaxConcurrentClients = maxClients,
                PingInterval = TimeSpan.Zero,
                PongTimeout = TimeSpan.FromSeconds(60),
                CloseTimeout = TimeSpan.FromSeconds(2),
                HandshakeTimeout = TimeSpan.FromSeconds(5),
            };

        [Test]
        public async Task StartAsync_OnAvailablePort_Succeeds()
        {
            int port = FindFreeTcpPort();
            var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastTestOptions());
            await using (server)
            {
                var result = await server.StartAsync(CancellationToken.None);
                Assert.IsTrue(result.Success, $"StartAsync failed: {result.Error?.Code}");
                Assert.IsTrue(server.IsRunning);
                Assert.AreEqual(port, server.BoundPort);
            }
        }

        [Test]
        public async Task StartAsync_OnAlreadyBoundPort_ReturnsPortInUseError()
        {
            int port = FindFreeTcpPort();

            // Hold the port using SO_EXCLUSIVEADDRUSE so the WebSocketServer's bind cannot
            // hijack it on Windows even with SO_REUSEADDR set.
            var blocker = new TcpListener(IPAddress.Loopback, port);
            blocker.Server.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ExclusiveAddressUse,
                true);
            blocker.Start();
            try
            {
                var server = new WebSocketServer(
                    new ServerBindOptions("127.0.0.1", port),
                    FastTestOptions());
                await using (server)
                {
                    var result = await server.StartAsync(CancellationToken.None);

                    Assert.IsFalse(result.Success, "Expected StartAsync to fail when port is held.");
                    Assert.IsInstanceOf<CoreIpcError.PortInUse>(result.Error);
                    var portInUse = (CoreIpcError.PortInUse)result.Error!;
                    Assert.AreEqual(port, portInUse.Port);
                    Assert.IsFalse(server.IsRunning);
                }
            }
            finally
            {
                blocker.Stop();
            }
        }

        [Test]
        public async Task ClientWebSocket_CanConnectExchangeTextAndDisconnect()
        {
            int port = FindFreeTcpPort();
            var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastTestOptions());

            var connected = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var disconnected = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            server.ClientConnected += c => connected.TrySetResult(c);
            server.ClientDisconnected += c => disconnected.TrySetResult(c);

            await using (server)
            {
                var startResult = await server.StartAsync(CancellationToken.None);
                Assert.IsTrue(startResult.Success, $"StartAsync failed: {startResult.Error?.Code}");

                using var client = new ClientWebSocket();
                using var connectCts = new CancellationTokenSource(TestTimeout);
                await client.ConnectAsync(
                    new Uri($"ws://127.0.0.1:{port}/"), connectCts.Token);

                Assert.AreEqual(WebSocketState.Open, client.State);

                using var connectedWaitCts = new CancellationTokenSource(TestTimeout);
                var serverConnection = await WaitForAsync(connected.Task, connectedWaitCts.Token);

                Assert.IsNotNull(serverConnection);
                Assert.IsTrue(server.ConnectedClientCount >= 1);

                // Drive a full text exchange: client → server, then server → client.
                using var receiveDriver = new CancellationTokenSource(TestTimeout);

                var serverInboundTask = ReceiveFirstPayloadAsync(serverConnection!, receiveDriver.Token);

                byte[] clientPayload = Encoding.UTF8.GetBytes("hello-from-client");
                await client.SendAsync(
                    new ArraySegment<byte>(clientPayload),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    receiveDriver.Token);

                ReadOnlyMemory<byte> received = await serverInboundTask;
                Assert.AreEqual(
                    "hello-from-client",
                    Encoding.UTF8.GetString(received.ToArray()));

                byte[] serverPayload = Encoding.UTF8.GetBytes("hello-from-server");
                await serverConnection!.SendAsync(serverPayload, receiveDriver.Token);

                byte[] buffer = new byte[1024];
                var clientReceive = await client.ReceiveAsync(
                    new ArraySegment<byte>(buffer), receiveDriver.Token);
                Assert.AreEqual(WebSocketMessageType.Text, clientReceive.MessageType);
                Assert.IsTrue(clientReceive.EndOfMessage);
                Assert.AreEqual(
                    "hello-from-server",
                    Encoding.UTF8.GetString(buffer, 0, clientReceive.Count));

                // Client closes; server should observe disconnect within timeout.
                await client.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "bye", receiveDriver.Token);

                using var disconnectedWaitCts = new CancellationTokenSource(TestTimeout);
                var disconnectedConnection = await WaitForAsync(disconnected.Task, disconnectedWaitCts.Token);
                Assert.AreSame(serverConnection, disconnectedConnection);
            }
        }

        [Test]
        public async Task MultipleClients_CanConnectAndReceiveBroadcast()
        {
            int port = FindFreeTcpPort();
            var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastTestOptions(maxClients: 4));

            var connections = new ConcurrentBag<IClientConnection>();
            var allConnected = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int target = 3;
            server.ClientConnected += c =>
            {
                connections.Add(c);
                if (connections.Count >= target) allConnected.TrySetResult(true);
            };

            await using (server)
            {
                var startResult = await server.StartAsync(CancellationToken.None);
                Assert.IsTrue(startResult.Success, $"StartAsync failed: {startResult.Error?.Code}");

                var clients = new ClientWebSocket[target];
                try
                {
                    using var connectCts = new CancellationTokenSource(TestTimeout);
                    for (int i = 0; i < target; i++)
                    {
                        clients[i] = new ClientWebSocket();
                        await clients[i].ConnectAsync(
                            new Uri($"ws://127.0.0.1:{port}/"), connectCts.Token);
                    }

                    using var allConnectedCts = new CancellationTokenSource(TestTimeout);
                    await WaitForAsync(allConnected.Task, allConnectedCts.Token);

                    Assert.AreEqual(target, connections.Count);
                    Assert.AreEqual(target, server.ConnectedClientCount);

                    // Broadcast a payload from the server to each client and verify receipt.
                    using var broadcastCts = new CancellationTokenSource(TestTimeout);
                    byte[] payload = Encoding.UTF8.GetBytes("broadcast");
                    foreach (var serverConnection in connections)
                    {
                        await serverConnection.SendAsync(payload, broadcastCts.Token);
                    }

                    foreach (var client in clients)
                    {
                        byte[] buffer = new byte[64];
                        var receive = await client.ReceiveAsync(
                            new ArraySegment<byte>(buffer), broadcastCts.Token);
                        Assert.AreEqual(WebSocketMessageType.Text, receive.MessageType);
                        Assert.AreEqual(
                            "broadcast",
                            Encoding.UTF8.GetString(buffer, 0, receive.Count));
                    }
                }
                finally
                {
                    using var closeCts = new CancellationTokenSource(TestTimeout);
                    foreach (var client in clients)
                    {
                        if (client == null) continue;
                        try
                        {
                            if (client.State == WebSocketState.Open)
                            {
                                await client.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure, "test-end", closeCts.Token);
                            }
                        }
                        catch
                        {
                            // best-effort cleanup
                        }
                        client.Dispose();
                    }
                }
            }
        }

        [Test]
        public async Task StopAsync_AfterStart_DisposesListenerAndDoesNotThrow()
        {
            int port = FindFreeTcpPort();
            var server = new WebSocketServer(
                new ServerBindOptions("127.0.0.1", port),
                FastTestOptions());

            var startResult = await server.StartAsync(CancellationToken.None);
            Assert.IsTrue(startResult.Success);
            Assert.IsTrue(server.IsRunning);

            await server.StopAsync();
            Assert.IsFalse(server.IsRunning);

            await server.DisposeAsync();
        }

        // -- helpers ----------------------------------------------------------

        private static async Task<ReadOnlyMemory<byte>> ReceiveFirstPayloadAsync(
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

        private static async Task<T> WaitForAsync<T>(Task<T> task, CancellationToken ct)
        {
            var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, ct));
            if (completed != task)
            {
                throw new TimeoutException(
                    "Timed out waiting for task to complete within the test deadline.");
            }
            return await task;
        }
    }
}
