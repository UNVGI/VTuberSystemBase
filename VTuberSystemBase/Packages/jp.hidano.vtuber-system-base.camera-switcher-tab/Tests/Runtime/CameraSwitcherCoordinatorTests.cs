#nullable enable
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    [TestFixture]
    public sealed class CameraSwitcherCoordinatorTests
    {
        private FakeUiCommandClient _commands = null!;
        private FakeUiSubscriptionClient _subs = null!;
        private FakeConnectionStatus _conn = null!;
        private FakeTimeProvider _time = null!;
        private FailureAggregator _failures = null!;
        private TimeoutTracker _timeouts = null!;
        private FakeOscEmitter _emitter = null!;
        private FakeFlatRecordSerializer _ser = null!;
        private OscStreamController _osc = null!;
        private VolumeUiStateManager _volume = null!;
        private FakePresetStore _presetStore = null!;
        private PresetController _presets = null!;
        private FakePreviewHandleResolver _resolver = null!;
        private PreviewSubscriptionController _preview = null!;
        private CameraRegistry _registry = null!;
        private ActiveCameraTracker _tracker = null!;
        private FakeDiagnosticsLogger _log = null!;
        private CameraSwitcherCoordinator _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _commands = new FakeUiCommandClient();
            _subs = new FakeUiSubscriptionClient();
            _conn = new FakeConnectionStatus(ConnectionStatusCode.Disconnected);
            _time = new FakeTimeProvider();
            _failures = new FailureAggregator();
            _timeouts = new TimeoutTracker(_time);
            _emitter = new FakeOscEmitter();
            _emitter.StartAsync("127.0.0.1", 9000).GetAwaiter().GetResult();
            _ser = new FakeFlatRecordSerializer();
            _osc = new OscStreamController(_ser, _emitter, _failures, _time);
            _volume = new VolumeUiStateManager(_commands, _failures, _time);
            _presetStore = new FakePresetStore();
            _presets = new PresetController(_presetStore, _commands, _time, _failures);
            _resolver = new FakePreviewHandleResolver();
            _preview = new PreviewSubscriptionController(_commands, _resolver);
            _registry = new CameraRegistry();
            _tracker = new ActiveCameraTracker();
            _log = new FakeDiagnosticsLogger();

            _sut = new CameraSwitcherCoordinator(_commands, _subs, _conn, _time,
                _registry, _tracker, _timeouts, _failures, _osc, _volume, _presets, _preview, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _sut.Dispose();
            _emitter.Dispose();
        }

        [Test]
        public void Initial_StatusIsInitializing()
        {
            Assert.AreEqual(TabStatus.Initializing, _sut.Status);
        }

        [Test]
        public void RequestAddCamera_PublishesAddCommand_AndArmsTimeout()
        {
            _sut.RequestAddCamera(CameraType.Perspective, "Cam One");
            Assert.AreEqual(1, _commands.Sent.Count);
            var p = (CameraCommandPayload)_commands.Sent[0].Payload!;
            Assert.AreEqual(CameraCommandOps.Add, p.Op);
            Assert.AreEqual(CameraTypeNames.Perspective, p.Type);
            Assert.AreEqual("Cam One", p.DisplayName);
            Assert.IsTrue(_timeouts.IsArmed(p.ClientRequestId));
        }

        [Test]
        public void CameraCreatedEvent_CancelsTimeout_AndUpsertsRegistry()
        {
            _sut.RequestAddCamera(CameraType.Perspective, "X");
            var requestId = ((CameraCommandPayload)_commands.Sent[0].Payload!).ClientRequestId;
            Assert.IsTrue(_timeouts.IsArmed(requestId));

            _sut.HandleCameraCreated(new CameraCreatedEventPayload
            {
                ClientRequestId = requestId,
                CameraId = "cam-1",
                Metadata = new CameraListEntry
                {
                    CameraId = "cam-1",
                    DisplayName = "X",
                    Type = CameraTypeNames.Perspective,
                    DefaultTransform = default,
                },
            });

            Assert.IsFalse(_timeouts.IsArmed(requestId));
            Assert.AreEqual(1, _sut.Cameras.Count);
            Assert.AreEqual("cam-1", _sut.Cameras[0].Id.Value);
        }

        [Test]
        public void Timeout_RecordsIpcSendFailure()
        {
            _sut.RequestAddCamera(CameraType.Perspective, "X");
            _time.Advance(System.TimeSpan.FromSeconds(6));
            Assert.GreaterOrEqual(_failures.CountOf(FailureKind.IpcSendFailure), 1);
        }

        [Test]
        public void RequestDeleteCamera_LocalCleanupsOscAndPreview()
        {
            // Seed: register cam-1, set as editing target.
            _registry.Upsert(new CameraMetadata { Id = new CameraId("cam-1"), DisplayName = "x", Type = CameraType.Perspective });
            _tracker.SetEditing(new CameraId("cam-1"));
            _sut.PreviewForTesting.Attach(new[] { new CameraId("cam-1") }, 192, 108, 15);
            _commands.Sent.Clear();

            _sut.RequestDeleteCamera(new CameraId("cam-1"));

            // Editing target cleared (local optimistic).
            Assert.IsFalse(_tracker.EditingCameraId.HasValue);
            // Preview slot detached.
            Assert.AreEqual(0, _sut.PreviewForTesting.Slots.Count);
        }

        [Test]
        public void HandleCamerasActive_UpdatesActiveTracker()
        {
            _sut.HandleCamerasActive(new CamerasActiveStatePayload
            {
                ActiveCameraId = "cam-7",
                UpdatedAtUnixMs = 0,
            });
            Assert.AreEqual("cam-7", _sut.ActiveCameraId.Value);
        }

        [Test]
        public void HandleCameraError_RecordsCameraErrorFailure()
        {
            _sut.HandleCameraError(new CameraErrorEventPayload
            {
                Op = "add",
                Reason = CameraErrorReasons.ResourceExhausted,
                Detail = "max cameras reached",
            });
            Assert.AreEqual(1, _failures.CountOf(FailureKind.CameraError));
        }

        [Test]
        public void Connection_DisconnectedToConnected_SetsReady()
        {
            _conn.SetStatus(ConnectionStatusCode.Connecting);
            _conn.SetStatus(ConnectionStatusCode.Connected);
            Assert.AreEqual(TabStatus.Ready, _sut.Status);
        }

        [Test]
        public void Connection_ConnectedToDisconnected_SetsSuspended()
        {
            _conn.SetStatus(ConnectionStatusCode.Connected);
            _conn.SetStatus(ConnectionStatusCode.Disconnected);
            Assert.AreEqual(TabStatus.Suspended, _sut.Status);
        }

        [Test]
        public void Activate_FrameTickPushesOsc_DeactivateStops()
        {
            _registry.Upsert(new CameraMetadata { Id = new CameraId("cam-1"), DisplayName = "x", Type = CameraType.Perspective });
            _sut.SelectEditTarget(new CameraId("cam-1"));
            _sut.OnTabActivated();

            _sut.FrameTick(new CameraSnapshot
            {
                CameraId = new CameraId("cam-1"),
                CameraType = CameraType.Perspective,
                FocalLengthMm = 50f,
                SensorWidthMm = 36f,
                SensorHeightMm = 24f,
                NearClipM = 0.1f,
                FarClipM = 100f,
                RotationW = 1f,
            });
            Assert.AreEqual(1, _emitter.Sent.Count);

            _sut.OnTabDeactivated();
            _sut.FrameTick(new CameraSnapshot
            {
                CameraId = new CameraId("cam-1"),
                CameraType = CameraType.Perspective,
                FocalLengthMm = 50f, SensorWidthMm = 36f, SensorHeightMm = 24f,
                NearClipM = 0.1f, FarClipM = 100f, RotationW = 1f,
            });
            Assert.AreEqual(1, _emitter.Sent.Count, "FrameTick after deactivate must not send");
        }

        [Test]
        public void SubscribeAll_RegistersExpectedTopics()
        {
            _sut.SubscribeAll();
            var topics = _subs.Subscriptions.Select(s => s.Topic).ToArray();
            CollectionAssert.Contains(topics, CameraIpcTopics.CamerasList);
            CollectionAssert.Contains(topics, CameraIpcTopics.CamerasActive);
            CollectionAssert.Contains(topics, CameraIpcTopics.CameraCreated);
            CollectionAssert.Contains(topics, CameraIpcTopics.CameraError);
        }

        [Test]
        public void UpdateCameraMetadata_SendsState()
        {
            _sut.UpdateCameraMetadata(new CameraId("cam-1"), CameraMetadataKeys.DisplayName, "renamed");
            Assert.AreEqual(1, _commands.Sent.Count);
            Assert.AreEqual("camera/cam-1/metadata/displayName", _commands.Sent[0].Topic);
            Assert.AreEqual(MessageKind.State, _commands.Sent[0].Kind);
        }

        [Test]
        public void SetVolumeOverrideParam_SendsOpaqueValue()
        {
            _sut.SetVolumeOverrideParam(new CameraId("cam-1"), "Bloom", "intensity",
                JsonSerializer.SerializeToElement(0.7f));
            Assert.AreEqual(1, _commands.Sent.Count);
            Assert.AreEqual("camera/cam-1/volume/override/Bloom/intensity", _commands.Sent[0].Topic);
        }

        [Test]
        public void Dispose_TransitionsToDisposing()
        {
            _sut.Dispose();
            Assert.AreEqual(TabStatus.Disposing, _sut.Status);
        }
    }
}
