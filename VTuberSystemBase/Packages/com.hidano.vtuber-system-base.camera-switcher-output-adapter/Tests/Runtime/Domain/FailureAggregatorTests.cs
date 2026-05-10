#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class FailureAggregatorTests
    {
        [Test]
        public void OscDecodeFailure_DoesNotPublishCameraError()
        {
            var bus = new FakeCoreIpcBus();
            var agg = new FailureAggregator(bus);
            agg.RecordOscDecodeFailure("cam-0001", new System.InvalidOperationException("CRC"));
            Assert.That(agg.CountOf(FailureKind.OscDecodeFailed), Is.EqualTo(1));
            Assert.That(bus.PublishedEvents.Count, Is.EqualTo(0));
            Assert.That(agg.CameraErrorPublishCount, Is.EqualTo(0));
        }

        [Test]
        public void OscStartupFailure_PublishesCameraErrorWithReasonOscStartupFailed()
        {
            var bus = new FakeCoreIpcBus();
            var agg = new FailureAggregator(bus);
            agg.RecordOscStartupFailure("port in use");

            Assert.That(agg.CountOf(FailureKind.OscStartupFailed), Is.EqualTo(1));
            Assert.That(bus.PublishedEvents.Count, Is.EqualTo(1));
            var ev = bus.PublishedEvents[0];
            Assert.That(ev.Topic, Is.EqualTo(CameraIpcTopics.CameraError));
            var payload = (CameraErrorEventPayload)ev.Payload!;
            Assert.That(payload.Reason, Is.EqualTo("OscStartupFailed"));
            Assert.That(payload.Detail, Is.EqualTo("port in use"));
        }

        [Test]
        public void UnknownCameraIdOnOsc_LogsOnly()
        {
            var bus = new FakeCoreIpcBus();
            var agg = new FailureAggregator(bus);
            agg.RecordUnknownCameraIdOnOsc("cam-9999");
            Assert.That(agg.CountOf(FailureKind.UnknownCameraIdOnOsc), Is.EqualTo(1));
            Assert.That(bus.PublishedEvents.Count, Is.EqualTo(0));
        }

        [Test]
        public void UnknownCameraIdOnIpc_PublishesUnknownCameraIdReason()
        {
            var bus = new FakeCoreIpcBus();
            var agg = new FailureAggregator(bus);
            agg.RecordUnknownCameraIdOnIpc("delete", "cam-9999", "g-1");
            Assert.That(bus.PublishedEvents.Count, Is.EqualTo(1));
            var payload = (CameraErrorEventPayload)bus.PublishedEvents[0].Payload!;
            Assert.That(payload.Reason, Is.EqualTo(CameraErrorReasons.UnknownCameraId));
            Assert.That(payload.Op, Is.EqualTo("delete"));
            Assert.That(payload.CameraId, Is.EqualTo("cam-9999"));
            Assert.That(payload.ClientRequestId, Is.EqualTo("g-1"));
        }

        [Test]
        public void VolumeBindFailed_PublishesEvent()
        {
            var bus = new FakeCoreIpcBus();
            var agg = new FailureAggregator(bus);
            agg.RecordVolumeBindFailed("override-add", "cam-0001", "Bloom", "UnknownOverrideType", "no detail");
            Assert.That(agg.CountOf(FailureKind.VolumeBindFailed), Is.EqualTo(1));
            Assert.That(bus.PublishedEvents.Count, Is.EqualTo(1));
        }

        [Test]
        public void Snapshot_RetainsRecentHistoryUpToLimit()
        {
            var agg = new FailureAggregator();
            for (var i = 0; i < FailureAggregator.RecentHistoryLimit + 10; i++)
            {
                agg.RecordUnknownCameraIdOnOsc($"cam-{i:D4}");
            }
            var snapshot = agg.GetSnapshot();
            Assert.That(snapshot.RecentHistory.Count, Is.EqualTo(FailureAggregator.RecentHistoryLimit));
            Assert.That(snapshot.UnknownCameraIdOnOscCount, Is.EqualTo(FailureAggregator.RecentHistoryLimit + 10));
        }

        [Test]
        public void NullBus_StillCountsButNoPublish()
        {
            var agg = new FailureAggregator(null);
            agg.RecordOscStartupFailure("port in use");
            Assert.That(agg.CountOf(FailureKind.OscStartupFailed), Is.EqualTo(1));
            Assert.That(agg.CameraErrorPublishCount, Is.EqualTo(1));
        }
    }
}
