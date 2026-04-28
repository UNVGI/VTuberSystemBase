#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Transport.Loopback;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class InMemoryLoopbackTransportTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        private static ServerBindOptions ServerBind() =>
            new ServerBindOptions("loopback", 0);

        private static ClientBindOptions ClientBind() =>
            new ClientBindOptions("loopback", 0, TimeSpan.FromSeconds(1));

        private static MessageEnvelope BuildEnvelope(
            MessageKind kind,
            string topic,
            string? correlationId,
            string payloadJson)
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: kind,
                Topic: topic,
                CorrelationId: correlationId,
                TimestampUnixMs: 1_745_539_200_000L,
                Payload: doc.RootElement.Clone());
        }

        private static async Task<MessageEnvelope> ReceiveOneAsync(
            IClientConnection connection,
            IMessageCodec codec,
            CancellationToken cancellationToken)
        {
            await foreach (var bytes in connection.ReceiveAsync(cancellationToken)
                .WithCancellation(cancellationToken))
            {
                var decoded = codec.Decode(bytes);
                if (!decoded.Success)
                {
                    throw new InvalidOperationException(
                        $"Decode failed: {decoded.Error?.Message}");
                }
                return decoded.Value;
            }
            throw new InvalidOperationException("Stream completed without payload.");
        }

        [Test]
        public async Task StartServer_ThenConnectClient_RaisesClientConnected()
        {
            await using var transport = new InMemoryLoopbackTransport();

            IClientConnection? observed = null;
            transport.ClientConnected += c => observed = c;

            using var cts = new CancellationTokenSource(TestTimeout);
            await transport.StartServerAsync(ServerBind(), cts.Token);

            var client = await transport.ConnectClientAsync(ClientBind(), cts.Token);

            Assert.IsNotNull(client);
            Assert.IsNotNull(observed, "ClientConnected was not raised.");
            Assert.AreEqual(1, transport.ConnectedClientCount);
            Assert.IsTrue(transport.IsServerRunning);
        }

        [Test]
        public async Task ConnectClient_BeforeStartServer_Throws()
        {
            await using var transport = new InMemoryLoopbackTransport();

            using var cts = new CancellationTokenSource(TestTimeout);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await transport.ConnectClientAsync(ClientBind(), cts.Token));
        }

        [Test]
        public async Task StartServer_Twice_Throws()
        {
            await using var transport = new InMemoryLoopbackTransport();

            using var cts = new CancellationTokenSource(TestTimeout);
            await transport.StartServerAsync(ServerBind(), cts.Token);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await transport.StartServerAsync(ServerBind(), cts.Token));
        }

        [Test]
        public async Task SelfLoop_RoundTripsStateEnvelope()
        {
            await using var transport = new InMemoryLoopbackTransport();
            var codec = new SystemTextJsonCodec();

            using var cts = new CancellationTokenSource(TestTimeout);
            await transport.StartServerAsync(ServerBind(), cts.Token);

            IClientConnection? serverSide = null;
            var serverReady = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            transport.ClientConnected += c =>
            {
                serverSide = c;
                serverReady.TrySetResult(c);
            };

            var client = await transport.ConnectClientAsync(ClientBind(), cts.Token);
            var server = await serverReady.Task;

            var envelope = BuildEnvelope(
                MessageKind.State,
                "slot/1/assignment",
                correlationId: null,
                payloadJson: "{\"slotId\":1,\"asset\":\"a.png\"}");

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success);

            await client.SendAsync(encoded.Value, cts.Token);
            var received = await ReceiveOneAsync(server, codec, cts.Token);

            Assert.AreEqual(envelope.Kind, received.Kind);
            Assert.AreEqual(envelope.Topic, received.Topic);
            Assert.AreEqual(envelope.Payload.GetRawText(), received.Payload.GetRawText());
        }

        [Test]
        public async Task SelfLoop_RoundTripsEventEnvelope()
        {
            await using var transport = new InMemoryLoopbackTransport();
            var codec = new SystemTextJsonCodec();

            using var cts = new CancellationTokenSource(TestTimeout);
            await transport.StartServerAsync(ServerBind(), cts.Token);

            var serverReady = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            transport.ClientConnected += c => serverReady.TrySetResult(c);

            var client = await transport.ConnectClientAsync(ClientBind(), cts.Token);
            var server = await serverReady.Task;

            var envelope = BuildEnvelope(
                MessageKind.Event,
                "lighting/preset",
                correlationId: null,
                payloadJson: "{\"preset\":\"warm\"}");

            var encoded = codec.Encode(envelope);
            Assert.IsTrue(encoded.Success);

            await client.SendAsync(encoded.Value, cts.Token);
            var received = await ReceiveOneAsync(server, codec, cts.Token);

            Assert.AreEqual(MessageKind.Event, received.Kind);
            Assert.AreEqual(envelope.Topic, received.Topic);
            Assert.AreEqual(envelope.Payload.GetRawText(), received.Payload.GetRawText());
        }

        [Test]
        public async Task SelfLoop_RoundTripsRequestThenResponse()
        {
            await using var transport = new InMemoryLoopbackTransport();
            var codec = new SystemTextJsonCodec();

            using var cts = new CancellationTokenSource(TestTimeout);
            await transport.StartServerAsync(ServerBind(), cts.Token);

            var serverReady = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            transport.ClientConnected += c => serverReady.TrySetResult(c);

            var client = await transport.ConnectClientAsync(ClientBind(), cts.Token);
            var server = await serverReady.Task;

            string correlation = Guid.NewGuid().ToString("N");

            var request = BuildEnvelope(
                MessageKind.Request,
                "echo/ping",
                correlationId: correlation,
                payloadJson: "{\"value\":42}");
            var requestBytes = codec.Encode(request);
            Assert.IsTrue(requestBytes.Success);

            await client.SendAsync(requestBytes.Value, cts.Token);
            var receivedRequest = await ReceiveOneAsync(server, codec, cts.Token);

            Assert.AreEqual(MessageKind.Request, receivedRequest.Kind);
            Assert.AreEqual(correlation, receivedRequest.CorrelationId);

            var response = BuildEnvelope(
                MessageKind.Response,
                "echo/ping",
                correlationId: correlation,
                payloadJson: "{\"value\":42,\"ok\":true}");
            var responseBytes = codec.Encode(response);
            Assert.IsTrue(responseBytes.Success);

            await server.SendAsync(responseBytes.Value, cts.Token);
            var receivedResponse = await ReceiveOneAsync(client, codec, cts.Token);

            Assert.AreEqual(MessageKind.Response, receivedResponse.Kind);
            Assert.AreEqual(correlation, receivedResponse.CorrelationId);
            Assert.AreEqual(response.Payload.GetRawText(), receivedResponse.Payload.GetRawText());
        }

        [Test]
        public async Task SelfLoop_PreservesFifoOrderAcrossManyEvents()
        {
            await using var transport = new InMemoryLoopbackTransport();

            using var cts = new CancellationTokenSource(TestTimeout);
            await transport.StartServerAsync(ServerBind(), cts.Token);

            var serverReady = new TaskCompletionSource<IClientConnection>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            transport.ClientConnected += c => serverReady.TrySetResult(c);

            var client = await transport.ConnectClientAsync(ClientBind(), cts.Token);
            var server = await serverReady.Task;

            const int Count = 256;
            var receiveTask = Task.Run(async () =>
            {
                var seen = new List<int>(Count);
                await foreach (var bytes in server.ReceiveAsync(cts.Token)
                    .WithCancellation(cts.Token))
                {
                    seen.Add(int.Parse(Encoding.UTF8.GetString(bytes.ToArray())));
                    if (seen.Count == Count) return seen;
                }
                return seen;
            });

            for (int i = 0; i < Count; i++)
            {
                await client.SendAsync(Encoding.UTF8.GetBytes(i.ToString()), cts.Token);
            }

            var seen = await receiveTask;
            Assert.AreEqual(Count, seen.Count);
            for (int i = 0; i < Count; i++)
            {
                Assert.AreEqual(i, seen[i], $"Out-of-order at index {i}.");
            }
        }

        [Test]
        public async Task DisposeAsync_IsIdempotent_AndReleasesPair()
        {
            var transport = new InMemoryLoopbackTransport();

            using var cts = new CancellationTokenSource(TestTimeout);
            await transport.StartServerAsync(ServerBind(), cts.Token);

            int disconnectedCount = 0;
            transport.ClientDisconnected += _ => Interlocked.Increment(ref disconnectedCount);

            var client = await transport.ConnectClientAsync(ClientBind(), cts.Token);
            Assert.AreEqual(1, transport.ConnectedClientCount);

            await transport.DisposeAsync();
            await transport.DisposeAsync();

            Assert.AreEqual(0, transport.ConnectedClientCount);
            Assert.IsFalse(transport.IsServerRunning);

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await transport.StartServerAsync(ServerBind(), CancellationToken.None));
        }
    }
}
