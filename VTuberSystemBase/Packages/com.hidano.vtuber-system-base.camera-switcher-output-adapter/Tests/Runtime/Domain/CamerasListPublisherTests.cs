#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class CamerasListPublisherTests
    {
        [Test]
        public void PublishCamerasList_EmitsAllocOrderAscendingEntries()
        {
            var bus = new FakeCoreIpcBus();
            var clock = new FakeClock(initialUnixMs: 1234);
            var publisher = new CamerasListPublisher(bus, clock);
            var registry = new CameraEntryRegistry();
            registry.Upsert(MakeEntry(1));
            registry.Upsert(MakeEntry(2));
            registry.Upsert(MakeEntry(3));

            publisher.PublishCamerasList(registry.Enumerate());

            var payload = AssertEnvelope.SingleStatePayload<CamerasListPayload>(bus, CameraIpcTopics.CamerasList);
            Assert.That(payload.Cameras.Count, Is.EqualTo(3));
            Assert.That(payload.Cameras[0].CameraId, Is.EqualTo("cam-0001"));
            Assert.That(payload.Cameras[2].CameraId, Is.EqualTo("cam-0003"));
            Assert.That(payload.UpdatedAtUnixMs, Is.EqualTo(1234));
        }

        [Test]
        public void PublishCamerasActive_EmitsExpectedTopicAndPayload()
        {
            var bus = new FakeCoreIpcBus();
            var publisher = new CamerasListPublisher(bus, new FakeClock(7));
            publisher.PublishCamerasActive(new CameraId("cam-0002"));
            var payload = AssertEnvelope.SingleStatePayload<CamerasActiveStatePayload>(bus, CameraIpcTopics.CamerasActive);
            Assert.That(payload.ActiveCameraId, Is.EqualTo("cam-0002"));
            Assert.That(payload.UpdatedAtUnixMs, Is.EqualTo(7));
        }

        [Test]
        public void PublishCameraCreated_EchoesClientRequestId()
        {
            var bus = new FakeCoreIpcBus();
            var publisher = new CamerasListPublisher(bus, new FakeClock());
            var entry = MakeEntry(1);
            publisher.PublishCameraCreated("g-42", entry);

            var ev = AssertEnvelope.SingleEventPayload<CameraCreatedEventPayload>(bus, CameraIpcTopics.CameraCreated);
            Assert.That(ev.ClientRequestId, Is.EqualTo("g-42"));
            Assert.That(ev.CameraId, Is.EqualTo("cam-0001"));
            Assert.That(ev.Metadata.CameraId, Is.EqualTo("cam-0001"));
        }

        [Test]
        public void PublishVolumeEnabledForAll_FlagsActiveCameraAsTrue()
        {
            var bus = new FakeCoreIpcBus();
            var publisher = new CamerasListPublisher(bus, new FakeClock());
            var registry = new CameraEntryRegistry();
            registry.Upsert(MakeEntry(1));
            registry.Upsert(MakeEntry(2));

            publisher.PublishVolumeEnabledForAll(registry.Enumerate(), new CameraId("cam-0002"));

            Assert.That(bus.PublishedStates.Count, Is.EqualTo(2));
            var first = bus.PublishedStates[0];
            var second = bus.PublishedStates[1];
            Assert.That(first.Topic, Is.EqualTo(CameraIpcTopics.VolumeEnabled("cam-0001")));
            Assert.That(((VolumeEnabledStatePayload)first.Payload!).Enabled, Is.False);
            Assert.That(second.Topic, Is.EqualTo(CameraIpcTopics.VolumeEnabled("cam-0002")));
            Assert.That(((VolumeEnabledStatePayload)second.Payload!).Enabled, Is.True);
        }

        private static CameraEntry MakeEntry(int allocOrder) => new CameraEntry(
            cameraId: new CameraId($"cam-{allocOrder:D4}"),
            displayName: $"Cam{allocOrder}",
            type: CameraType.Perspective,
            defaultTransform: PayloadFactory.DefaultTransform(),
            allocOrder: allocOrder,
            gameObject: null,
            cameraComponent: null,
            localVolume: null);
    }
}
