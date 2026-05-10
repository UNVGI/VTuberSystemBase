#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Smoke tests for the test-doubles introduced by Task 1.3. PlayMode-friendly
    /// (no scene mutation) — they only exercise pure-state behaviour of each fake.
    /// </summary>
    [TestFixture]
    public sealed class FakesSelfTests
    {
        [Test]
        public void FakeOutputCommandDispatcher_RegistersAndInvokesStateHandler()
        {
            using var dispatcher = new FakeOutputCommandDispatcher();
            int invocations = 0;
            string? lastTopic = null;
            using (dispatcher.RegisterStateHandler<int>("test/topic", cmd =>
            {
                invocations++;
                lastTopic = cmd.Topic;
            }))
            {
                Assert.That(dispatcher.RegisteredHandlerCount, Is.EqualTo(1));
                dispatcher.InvokeStateAt("test/topic", 42);
            }
            Assert.That(invocations, Is.EqualTo(1));
            Assert.That(lastTopic, Is.EqualTo("test/topic"));
            Assert.That(dispatcher.RegisteredHandlerCount, Is.EqualTo(0));
        }

        [Test]
        public void FakeOutputCommandDispatcher_RequestHandler_RoundTripsResponse()
        {
            using var dispatcher = new FakeOutputCommandDispatcher();
            using (dispatcher.RegisterRequestHandler<int, string>("test/req", req => $"echo:{req.Payload}"))
            {
                var response = dispatcher.InvokeRequestAt<int, string>("test/req", 7);
                Assert.That(response, Is.EqualTo("echo:7"));
                Assert.That(dispatcher.CapturedResponses.Count, Is.EqualTo(1));
                Assert.That(dispatcher.CapturedResponses[0].Topic, Is.EqualTo("test/req"));
            }
        }

        [Test]
        public void FakeOutputCommandDispatcher_DuplicateStateRegistration_Throws()
        {
            using var dispatcher = new FakeOutputCommandDispatcher();
            using (dispatcher.RegisterStateHandler<int>("topic", _ => { }))
            {
                Assert.Throws<System.InvalidOperationException>(() =>
                    dispatcher.RegisterStateHandler<int>("topic", _ => { }));
            }
        }

        [Test]
        public void FakeCameraIdAllocator_UsesSequenceThenFallback()
        {
            var allocator = new FakeCameraIdAllocator()
                .WithSequence("cam-aaaa", "cam-bbbb")
                .WithFallback("cam-zzzz");
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-aaaa"));
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-bbbb"));
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-zzzz"));
            Assert.That(allocator.AllocateCallCount, Is.EqualTo(3));
        }

        [Test]
        public void FakeOscReceiverHost_StartFailureSetsFailedStatus()
        {
            var host = new FakeOscReceiverHost
            {
                NextStartResult = OscReceiverStartResult.Failure("port in use"),
            };
            var result = host.StartAsync("127.0.0.1", 9000).Result;
            Assert.That(result.Success, Is.False);
            Assert.That(host.Status, Is.EqualTo(OscReceiverHostStatus.Failed));
        }

        [Test]
        public void FakeOscReceiverHost_EmitDeliversMessage()
        {
            var host = new FakeOscReceiverHost();
            string? receivedCameraId = null;
            host.MessageReceived += msg => receivedCameraId = msg.CameraId;
            host.Emit("cam-0001", new byte[] { 1, 2, 3 });
            Assert.That(receivedCameraId, Is.EqualTo("cam-0001"));
        }

        [Test]
        public void FakeVolumeOverrideSchemaResolver_Throwing_PropagatesException()
        {
            var resolver = FakeVolumeOverrideSchemaResolver.Throwing(new System.InvalidOperationException("boom"));
            Assert.Throws<System.InvalidOperationException>(() => resolver.GetSchema());
        }

        [Test]
        public void FakeCoreIpcBus_PublishStateRecordsTopicAndType()
        {
            var bus = new FakeCoreIpcBus();
            bus.PublishState("cameras/list", new CamerasListPayload { Cameras = System.Array.Empty<CameraListEntry>(), UpdatedAtUnixMs = 0 });
            Assert.That(bus.PublishedStates.Count, Is.EqualTo(1));
            Assert.That(bus.PublishedStates[0].Topic, Is.EqualTo("cameras/list"));
            Assert.That(bus.PublishedStates[0].PayloadType, Is.EqualTo(typeof(CamerasListPayload)));
        }

        [Test]
        public void FakeClock_AdvancesMonotonically()
        {
            var clock = new FakeClock(1000);
            Assert.That(clock.UnixMillisecondsNow(), Is.EqualTo(1000));
            clock.Advance(50);
            Assert.That(clock.UnixMillisecondsNow(), Is.EqualTo(1050));
        }
    }
}
