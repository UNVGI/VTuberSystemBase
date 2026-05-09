#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    [TestFixture]
    public sealed class OscStreamControllerTests
    {
        private FakeOscEmitter _emitter = null!;
        private FakeFlatRecordSerializer _serializer = null!;
        private FakeTimeProvider _time = null!;
        private FailureAggregator _failures = null!;
        private OscStreamController _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _emitter = new FakeOscEmitter();
            _emitter.StartAsync("127.0.0.1", 9000).GetAwaiter().GetResult();
            _serializer = new FakeFlatRecordSerializer();
            _time = new FakeTimeProvider();
            _failures = new FailureAggregator();
            _sut = new OscStreamController(_serializer, _emitter, _failures, _time);
        }

        [TearDown]
        public void TearDown()
        {
            _sut.Dispose();
            _emitter.Dispose();
        }

        private static CameraSnapshot Snap(string id, uint frame)
        {
            return new CameraSnapshot
            {
                CameraId = new CameraId(id),
                CameraType = CameraType.Perspective,
                FocalLengthMm = 50f,
                SensorWidthMm = 36f,
                SensorHeightMm = 24f,
                NearClipM = 0.1f,
                FarClipM = 100f,
                RotationW = 1f,
                FrameCounter = frame,
            };
        }

        [Test]
        public void NullTarget_NoSend()
        {
            _sut.FrameTick(Snap("cam-1", 1));
            Assert.AreEqual(0, _emitter.Sent.Count);
            Assert.AreEqual(0, _sut.SentCount);
        }

        [Test]
        public void SetTarget_FrameTickSendsOncePerFrame()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            _sut.FrameTick(Snap("cam-1", 1));
            _sut.FrameTick(Snap("cam-1", 2));
            _sut.FrameTick(Snap("cam-1", 3));
            Assert.AreEqual(3, _emitter.Sent.Count);
            Assert.AreEqual(3, _sut.SentCount);
            // Address always uses target id.
            foreach (var sent in _emitter.Sent)
            {
                Assert.AreEqual("/ucapi/camera/cam-1/flat", sent.Address);
            }
        }

        [Test]
        public void SetTargetNull_StopsSending()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            _sut.FrameTick(Snap("cam-1", 1));
            _sut.SetTarget(null);
            _sut.FrameTick(Snap("cam-1", 2));
            Assert.AreEqual(1, _emitter.Sent.Count);
        }

        [Test]
        public void OnCameraDeleted_CurrentTarget_ResetsTargetToNull()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            _sut.OnCameraDeleted(new CameraId("cam-1"));
            _sut.FrameTick(Snap("cam-1", 1));
            Assert.AreEqual(0, _emitter.Sent.Count);
            Assert.IsFalse(_sut.Target.HasValue);
        }

        [Test]
        public void OnCameraDeleted_OtherCamera_KeepsTarget()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            _sut.OnCameraDeleted(new CameraId("cam-2"));
            _sut.FrameTick(Snap("cam-1", 1));
            Assert.AreEqual(1, _emitter.Sent.Count);
        }

        [Test]
        public void Serialize_Invalid_SkipsAndRecordsFailure()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            _serializer.ForceFailure = SerializeFailureReason.InvalidPosition;
            _sut.FrameTick(Snap("cam-1", 1));
            Assert.AreEqual(0, _emitter.Sent.Count);
            Assert.AreEqual(1, _failures.CountOf(FailureKind.OscFailure));
        }

        [Test]
        public void NullSnapshot_SkipsWithoutSending()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            _sut.FrameTick(snapshot: null);
            Assert.AreEqual(0, _emitter.Sent.Count);
            Assert.AreEqual(1, _sut.SkippedCount);
        }

        [Test]
        public void EmitterAsyncFailure_RecordedToFailureAggregator()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            _emitter.RaiseSendFailure(new OscEmitFailure(OscFailureKind.SocketError, "icmp"));
            Assert.AreEqual(1, _failures.CountOf(FailureKind.OscFailure));
        }

        [Test]
        public void SnapshotMismatch_DoesNotSend()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            // Snapshot for a different camera should be skipped (cooperative guard).
            _sut.FrameTick(Snap("cam-2", 1));
            Assert.AreEqual(0, _emitter.Sent.Count);
            Assert.AreEqual(1, _sut.SkippedCount);
        }

        [Test]
        public void OneThousandTicks_NoLeak_Smoke()
        {
            _sut.SetTarget(new CameraId("cam-1"));
            for (uint i = 0; i < 1000; i++)
            {
                _sut.FrameTick(Snap("cam-1", i));
            }
            Assert.AreEqual(1000, _emitter.Sent.Count);
            Assert.AreEqual(1000, _sut.SentCount);
            // FailureAggregator never accumulated entries on the happy path.
            Assert.AreEqual(0, _failures.CountOf(FailureKind.OscFailure));
        }
    }
}
