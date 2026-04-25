#nullable enable
using System;
using System.Collections;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Transport.Loopback;
using VTuberSystemBase.CoreIpc.Tests.TestSupport;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class SchemaEvolutionTests
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

        private sealed class EvolvedPayload
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        [UnityTest]
        public IEnumerator InboundEnvelope_WithUnknownEnvelopeFields_DecodesAndDeliversKnownFields()
        {
            var transport = new InMemoryLoopbackTransport();

            IClientConnection? serverSide = null;
            transport.ClientConnected += conn => serverSide = conn;

            var host = new CoreIpcRuntimeHost(
                transportFactory: _ => transport,
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: (_, ct) =>
                    Task.Delay(TimeSpan.FromMilliseconds(20), ct));

            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, LoopbackIntegrationHarness.FastOptions());

            Assert.IsNotNull(serverSide,
                "Server-side connection must be captured for inbound injection.");

            EvolvedPayload? received = null;
            using var subscription = host.Bus.SubscribeEvent<EvolvedPayload>(
                "topic/evolution",
                payload => received = payload);

            const string envelopeJson =
                "{\"protocolVersion\":\"1.0\"," +
                "\"kind\":\"event\"," +
                "\"topic\":\"topic/evolution\"," +
                "\"correlationId\":null," +
                "\"timestampUnixMs\":12345," +
                "\"payload\":{\"id\":7,\"name\":\"alpha\",\"futurePayloadField\":42}," +
                "\"futureEnvelopeField\":\"forward-compat\"," +
                "\"anotherFutureField\":[1,2,3]}";
            var bytes = Encoding.UTF8.GetBytes(envelopeJson);

            var sendTask = serverSide!.SendAsync(bytes, CancellationToken.None).AsTask();
            yield return LoopbackIntegrationHarness.AwaitTask(
                sendTask, LoopbackIntegrationHarness.AssertTimeout);

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received is not null,
                LoopbackIntegrationHarness.AssertTimeout,
                "Subscriber should receive the decoded payload despite unknown envelope " +
                "and payload fields.");

            Assert.AreEqual(7, received!.Id,
                "Known envelope payload field 'id' must round-trip into the typed payload.");
            Assert.AreEqual("alpha", received.Name,
                "Known envelope payload field 'name' must round-trip into the typed payload.");

            host.Dispose();
        }

        [UnityTest]
        public IEnumerator InboundEnvelope_WithFutureMinorProtocolVersion_StillDecodes()
        {
            var transport = new InMemoryLoopbackTransport();

            IClientConnection? serverSide = null;
            transport.ClientConnected += conn => serverSide = conn;

            var host = new CoreIpcRuntimeHost(
                transportFactory: _ => transport,
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: (_, ct) =>
                    Task.Delay(TimeSpan.FromMilliseconds(20), ct));

            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, LoopbackIntegrationHarness.FastOptions());

            Assert.IsNotNull(serverSide);

            int? received = null;
            using var subscription = host.Bus.SubscribeEvent<int>(
                "topic/minor-bump",
                payload => received = payload);

            const string envelopeJson =
                "{\"protocolVersion\":\"1.5\"," +
                "\"kind\":\"event\"," +
                "\"topic\":\"topic/minor-bump\"," +
                "\"correlationId\":null," +
                "\"timestampUnixMs\":99," +
                "\"payload\":11," +
                "\"newSiblingField\":{\"reserved\":true}}";

            var sendTask = serverSide!
                .SendAsync(Encoding.UTF8.GetBytes(envelopeJson), CancellationToken.None)
                .AsTask();
            yield return LoopbackIntegrationHarness.AwaitTask(
                sendTask, LoopbackIntegrationHarness.AssertTimeout);

            yield return LoopbackIntegrationHarness.WaitFor(
                () => received.HasValue,
                LoopbackIntegrationHarness.AssertTimeout,
                "Future-minor-version envelopes (e.g., 1.5) must remain compatible " +
                "with the current major-1 codec.");

            Assert.AreEqual(11, received!.Value);

            host.Dispose();
        }
    }
}
