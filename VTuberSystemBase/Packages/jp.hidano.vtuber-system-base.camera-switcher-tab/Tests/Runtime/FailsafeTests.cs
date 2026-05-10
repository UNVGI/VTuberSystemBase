#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Cross-cutting failsafe coverage: OSC failure does not stop IPC, IPC
    /// disconnect aggregates without exceptions, Volume metadata failure is
    /// scoped to one camera, camera/error does not block other operations.
    /// </summary>
    [TestFixture]
    public sealed class FailsafeTests
    {
        [Test]
        public void OscPortInUse_DoesNotBlockIpcCommands()
        {
            var commands = new FakeUiCommandClient();
            var subs = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus(ConnectionStatusCode.Connected);
            var time = new FakeTimeProvider();
            var failures = new FailureAggregator();
            var emitter = new FakeOscEmitter { ForceStartFailure = true };
            var ser = new FakeFlatRecordSerializer();
            var osc = new OscStreamController(ser, emitter, failures, time);
            var volume = new VolumeUiStateManager(commands, failures, time);
            var presets = new PresetController(new FakePresetStore(), commands, time, failures);
            var preview = new PreviewSubscriptionController(commands, new FakePreviewHandleResolver());
            using var coord = new CameraSwitcherCoordinator(commands, subs, conn, time,
                new CameraRegistry(), new ActiveCameraTracker(), new TimeoutTracker(time),
                failures, osc, volume, presets, preview, null);

            // Even though OSC start would fail, IPC commands still flow.
            coord.RequestAddCamera(CameraType.Perspective, "X");
            Assert.AreEqual(1, commands.Sent.Count);
            Assert.AreEqual(CameraIpcTopics.CameraCommand, commands.Sent[0].Topic);
        }

        [Test]
        public void IpcSendFails_RecordsFailureWithoutThrowing()
        {
            var commands = new FakeUiCommandClient
            {
                ForceFail = true,
                FailWith = new SendError(SendErrorCode.NotConnected),
            };
            var subs = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus(ConnectionStatusCode.Disconnected);
            var time = new FakeTimeProvider();
            var failures = new FailureAggregator();
            var emitter = new FakeOscEmitter();
            var ser = new FakeFlatRecordSerializer();
            using var coord = new CameraSwitcherCoordinator(commands, subs, conn, time,
                new CameraRegistry(), new ActiveCameraTracker(), new TimeoutTracker(time),
                failures, new OscStreamController(ser, emitter, failures, time),
                new VolumeUiStateManager(commands, failures, time),
                new PresetController(new FakePresetStore(), commands, time, failures),
                new PreviewSubscriptionController(commands, new FakePreviewHandleResolver()), null);

            // ActivateCamera should not throw even though IPC fails.
            Assert.DoesNotThrow(() => coord.ActivateCamera(new CameraId("cam-1")));
            Assert.GreaterOrEqual(failures.CountOf(FailureKind.IpcSendFailure), 1);
        }

        [Test]
        public async Task PresetSaveFailure_RecordsButContinues()
        {
            var store = new FakePresetStore { ForceSaveFailure = PresetIoFailureKind.WriteFailed };
            var time = new FakeTimeProvider();
            var failures = new FailureAggregator();
            var commands = new FakeUiCommandClient();
            using var presets = new PresetController(store, commands, time, failures);

            presets.CreatePreset("alpha");
            // Drive debounce.
            time.Advance(System.TimeSpan.FromMilliseconds(600));
            await Task.Delay(100);

            Assert.GreaterOrEqual(failures.CountOf(FailureKind.PresetIoFailure), 1);
            // Subsequent CreatePreset still works.
            var another = presets.CreatePreset("beta");
            Assert.IsTrue(another.Success);
        }

        [Test]
        public async Task VolumeMetadataFailure_IsScopedPerCamera()
        {
            var commands = new FakeUiCommandClient();
            // First request fails (RequestResponder is null → Timeout).
            var failures = new FailureAggregator();
            var time = new FakeTimeProvider();
            var sut = new VolumeUiStateManager(commands, failures, time);

            await sut.OnEditTargetChangedAsync(new CameraId("cam-1"));
            Assert.AreEqual(1, failures.CountOf(FailureKind.VolumeMetadataFailure));

            // Second camera with success.
            commands.RequestResponder = _ => new VolumeMetadataResponse { Overrides = System.Array.Empty<VolumeOverrideSchema>() };
            await sut.OnEditTargetChangedAsync(new CameraId("cam-2"));
            Assert.IsTrue(sut.TryGet(new CameraId("cam-2"), out var s2));
            Assert.IsFalse(s2.SchemaFailed, "Second camera must succeed even though first failed");
        }
    }
}
