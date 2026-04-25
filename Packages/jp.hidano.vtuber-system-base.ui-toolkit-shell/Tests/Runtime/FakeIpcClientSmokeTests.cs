#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    [TestFixture]
    public sealed class FakeIpcClientSmokeTests
    {
        private sealed class CharacterPosePayload
        {
            public string PoseId { get; set; } = string.Empty;
            public float Intensity { get; set; }
        }

        [SetUp]
        public void SetUp()
        {
            MainThreadAffinity.Capture();
        }

        [TearDown]
        public void TearDown()
        {
            MainThreadAffinity.Reset();
        }

        [Test]
        public void RoundTrip_PublishState_To_Subscriber_DeliversPayloadOnMainThread()
        {
            var fake = new FakeIpcClient();
            fake.SetConnectionState(ConnectionState.Connected);

            var recorder = new MainThreadAffinity.Recorder();
            CharacterPosePayload? received = null;
            using var token = fake.SubscribeState<CharacterPosePayload>(
                "vsb/character/pose",
                payload =>
                {
                    received = payload;
                    recorder.Record();
                });

            // Outbound publish: should be recorded and return Ok while connected.
            var sendResult = fake.PublishState("vsb/character/pose", new CharacterPosePayload
            {
                PoseId = "wave",
                Intensity = 0.75f,
            });
            Assert.That(sendResult.Success, Is.True, "PublishState should succeed when fake is Connected");
            Assert.That(fake.SentMessages.Count, Is.EqualTo(1));
            Assert.That(fake.SentMessages[0].Topic, Is.EqualTo("vsb/character/pose"));
            Assert.That(fake.SentMessages[0].Kind, Is.EqualTo(MessageKind.State));

            // Inbound injection: subscribers fire synchronously on the injecting thread.
            fake.InjectState("vsb/character/pose", new CharacterPosePayload
            {
                PoseId = "bow",
                Intensity = 1.0f,
            });

            Assert.That(recorder.WasInvoked, Is.True);
            Assert.That(received, Is.Not.Null);
            Assert.That(received!.PoseId, Is.EqualTo("bow"));
            Assert.That(recorder.Matches(MainThreadAffinity.CapturedThreadId), Is.True,
                "Subscriber callback must fire on the captured main thread");
        }

        [Test]
        public void PublishState_WhileDisconnected_ReturnsNotConnectedError()
        {
            var fake = new FakeIpcClient();
            // Default state is Disconnected; do not connect.

            var result = fake.PublishState("vsb/lighting/intensity", payload: 0.42f);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.InstanceOf<CoreIpcError.NotConnected>());
        }

        [Test]
        public async Task RequestAsync_RespondsToCorrelationId()
        {
            var fake = new FakeIpcClient();
            fake.SetConnectionState(ConnectionState.Connected);

            var requestTask = fake.RequestAsync<string, int>("vsb/scene/index", "main");

            // The request must show up as pending with a generated correlation ID.
            var pending = fake.PendingRequestCorrelationIds;
            Assert.That(pending.Count, Is.EqualTo(1));

            // Resolve via correlation-id-keyed responder.
            var responded = fake.RespondToRequest(pending[0], 42);
            Assert.That(responded, Is.True);

            var result = await requestTask.ConfigureAwait(false);
            Assert.That(result.Success, Is.True);
            Assert.That(result.Value, Is.EqualTo(42));
        }
    }
}
