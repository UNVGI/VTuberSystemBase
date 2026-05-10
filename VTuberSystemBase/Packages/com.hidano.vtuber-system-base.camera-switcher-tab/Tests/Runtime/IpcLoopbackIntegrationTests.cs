#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// End-to-end style tests against the Fake IPC layer covering the four
    /// scenarios called out in tasks.md §4.1: add/created round-trip,
    /// cameras/list + cameras/active state sync, camera/error per-operation
    /// localisation, and connection drop / recover transitions.
    /// </summary>
    [TestFixture]
    public sealed class IpcLoopbackIntegrationTests
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
        private CameraSwitcherCoordinator _coord = null!;

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
            _coord = new CameraSwitcherCoordinator(_commands, _subs, _conn, _time,
                _registry, _tracker, _timeouts, _failures, _osc, _volume, _presets, _preview, null);
            _coord.SubscribeAll();
        }

        [TearDown]
        public void TearDown()
        {
            _coord.Dispose();
            _emitter.Dispose();
        }

        [Test]
        public void AddCommand_ThenCameraCreatedEvent_RegistersCameraAndCancelsTimeout()
        {
            // UI sends add.
            _coord.RequestAddCamera(CameraType.Perspective, "Cam One");
            var sent = _commands.Sent[0];
            var requestId = ((CameraCommandPayload)sent.Payload!).ClientRequestId;

            // Server responds via subscription.
            _subs.Emit(CameraIpcTopics.CameraCreated, new CameraCreatedEventPayload
            {
                ClientRequestId = requestId,
                CameraId = "cam-1",
                Metadata = new CameraListEntry { CameraId = "cam-1", DisplayName = "Cam One", Type = CameraTypeNames.Perspective, DefaultTransform = default },
            }, MessageKind.Event);

            Assert.AreEqual(1, _coord.Cameras.Count);
            Assert.AreEqual("cam-1", _coord.Cameras[0].Id.Value);
            Assert.IsFalse(_timeouts.IsArmed(requestId));
        }

        [Test]
        public void CamerasListAndActive_StatesSyncToUiState()
        {
            _subs.Emit(CameraIpcTopics.CamerasList, new CamerasListPayload
            {
                Cameras = new[]
                {
                    new CameraListEntry { CameraId = "cam-1", DisplayName = "A", Type = CameraTypeNames.Perspective, DefaultTransform = default },
                    new CameraListEntry { CameraId = "cam-2", DisplayName = "B", Type = CameraTypeNames.Perspective, DefaultTransform = default },
                },
                UpdatedAtUnixMs = 0,
            });
            Assert.AreEqual(2, _coord.Cameras.Count);

            _subs.Emit(CameraIpcTopics.CamerasActive, new CamerasActiveStatePayload
            {
                ActiveCameraId = "cam-2",
                UpdatedAtUnixMs = 0,
            });
            Assert.AreEqual("cam-2", _coord.ActiveCameraId.Value);
        }

        [Test]
        public void CameraError_LocalisedToFailureAggregator()
        {
            _subs.Emit(CameraIpcTopics.CameraError, new CameraErrorEventPayload
            {
                ClientRequestId = "req-1",
                Op = CameraCommandOps.Add,
                Reason = CameraErrorReasons.ResourceExhausted,
                Detail = "max cameras",
            }, MessageKind.Event);
            Assert.AreEqual(1, _failures.CountOf(FailureKind.CameraError));
        }

        [Test]
        public void Connection_DisconnectedReconnect_PrunesEditingTargetIfDeleted()
        {
            // Seed initial state.
            _subs.Emit(CameraIpcTopics.CamerasList, new CamerasListPayload
            {
                Cameras = new[]
                {
                    new CameraListEntry { CameraId = "cam-1", DisplayName = "A", Type = CameraTypeNames.Perspective, DefaultTransform = default },
                },
                UpdatedAtUnixMs = 0,
            });
            _coord.SelectEditTarget(new CameraId("cam-1"));

            // Disconnect.
            _conn.SetStatus(ConnectionStatusCode.Connecting);
            _conn.SetStatus(ConnectionStatusCode.Disconnected);
            Assert.AreEqual(TabStatus.Suspended, _coord.Status);

            // Reconnect with a fresh list that no longer contains cam-1 (server purged it).
            _conn.SetStatus(ConnectionStatusCode.Connecting);
            _conn.SetStatus(ConnectionStatusCode.Connected);
            _subs.Emit(CameraIpcTopics.CamerasList, new CamerasListPayload
            {
                Cameras = System.Array.Empty<CameraListEntry>(),
                UpdatedAtUnixMs = 0,
            });

            Assert.AreEqual(0, _coord.Cameras.Count);
            Assert.IsFalse(_coord.EditingCameraId.HasValue, "Editing target must be cleared when its camera disappears");
            Assert.AreEqual(TabStatus.Ready, _coord.Status);
        }
    }
}
