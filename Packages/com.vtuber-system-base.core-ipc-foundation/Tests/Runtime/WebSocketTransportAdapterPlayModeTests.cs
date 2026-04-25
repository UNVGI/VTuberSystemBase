#nullable enable
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class WebSocketTransportAdapterPlayModeTests
    {
        private const int RoundTripMessageCount = 1000;
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

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
                PongTimeout = TimeSpan.FromSeconds(60),
                CloseTimeout = TimeSpan.FromSeconds(2),
                HandshakeTimeout = TimeSpan.FromSeconds(5),
            };

        private static WebSocketClientOptions FastClientOptions() =>
            new WebSocketClientOptions
            {
                CloseTimeout = TimeSpan.FromSeconds(2),
            };

        [UnityTest]
        public IEnumerator RoundTrip_OneThousandMessages_AllReceivedInOrder()
        {
            var task = Task.Run(() => RoundTripCoreAsync(RoundTripMessageCount));

            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception!.InnerException ?? task.Exception!;
            }
        }

        private static async Task RoundTripCoreAsync(int messageCount)
        {
            int port = FindFreeTcpPort();
            var codec = new SystemTextJsonCodec();

            await using var adapter = new WebSocketTransportAdapter(
                codec,
                FastServerOptions(),
                FastClientOptions());

            var connectedTcs = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            adapter.ClientConnected += c => connectedTcs.TrySetResult(c);

            using var cts = new CancellationTokenSource(TestTimeout);

            await adapter.StartServerAsync(
                new ServerBindOptions("127.0.0.1", port), cts.Token).ConfigureAwait(false);

            var clientConnection = await adapter.ConnectClientAsync(
                new ClientBindOptions("127.0.0.1", port, TimeSpan.FromSeconds(5)),
                cts.Token).ConfigureAwait(false);

            var serverConnection =
                await WaitForFirstConnectionAsync(connectedTcs, cts.Token).ConfigureAwait(false);

            var echoTask = Task.Run(async () =>
            {
                int echoed = 0;
                await foreach (var payload in serverConnection
                    .ReceiveAsync(cts.Token).WithCancellation(cts.Token).ConfigureAwait(false))
                {
                    await serverConnection.SendAsync(payload, cts.Token).ConfigureAwait(false);
                    echoed++;
                    if (echoed >= messageCount) break;
                }
                return echoed;
            }, cts.Token);

            int received = 0;
            var receiveTask = Task.Run(async () =>
            {
                await foreach (var payload in clientConnection
                    .ReceiveAsync(cts.Token).WithCancellation(cts.Token).ConfigureAwait(false))
                {
                    int n = int.Parse(Encoding.UTF8.GetString(payload.ToArray()));
                    Assert.AreEqual(received, n,
                        $"Out-of-order: at index {received}, payload={n}");
                    received++;
                    if (received >= messageCount) break;
                }
                return received;
            }, cts.Token);

            for (int i = 0; i < messageCount; i++)
            {
                byte[] payload = Encoding.UTF8.GetBytes(i.ToString());
                await clientConnection.SendAsync(payload, cts.Token).ConfigureAwait(false);
            }

            int echoedCount = await echoTask.ConfigureAwait(false);
            int receivedCount = await receiveTask.ConfigureAwait(false);

            Assert.AreEqual(messageCount, echoedCount,
                "Server-side echo did not process all messages.");
            Assert.AreEqual(messageCount, receivedCount,
                "Client did not receive all echoed messages without loss.");
        }

        private static async Task<IClientConnection> WaitForFirstConnectionAsync(
            TaskCompletionSource<IClientConnection> tcs,
            CancellationToken ct)
        {
            var completed = await Task.WhenAny(
                tcs.Task,
                Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            if (completed != tcs.Task)
            {
                throw new TimeoutException(
                    "Server-side ClientConnected did not fire within the test timeout.");
            }
            return await tcs.Task.ConfigureAwait(false);
        }
    }
}
