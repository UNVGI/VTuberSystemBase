#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Osc;
using VTuberSystemBase.CameraSwitcherTab.Diagnostics;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Verifies that <see cref="CameraSwitcherTabDiagnostics.GetSnapshot"/>
    /// returns every observable required by Requirement 14.9: tab status,
    /// camera count, active / editing cameraId, OSC state, IPC connectivity,
    /// last preset save time, active preset name, and per-Kind failure counts.
    /// </summary>
    [TestFixture]
    public sealed class CameraSwitcherTabDiagnosticsTests
    {
        [Test]
        public void GetSnapshot_PopulatesAllExpectedFields()
        {
            var commands = new FakeUiCommandClient();
            var subs = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus(ConnectionStatusCode.Disconnected);
            var time = new FakeTimeProvider();
            var failures = new FailureAggregator();
            var timeouts = new TimeoutTracker(time);
            var emitter = new FakeOscEmitter();
            var ser = new FakeFlatRecordSerializer();
            var osc = new OscStreamController(ser, emitter, failures, time);
            var volume = new VolumeUiStateManager(commands, failures, time);
            var presetStore = new FakePresetStore();
            var presets = new PresetController(presetStore, commands, time, failures);
            var resolver = new FakePreviewHandleResolver();
            var preview = new PreviewSubscriptionController(commands, resolver);
            var registry = new CameraRegistry();
            var tracker = new ActiveCameraTracker();
            using var coord = new CameraSwitcherCoordinator(commands, subs, conn, time,
                registry, tracker, timeouts, failures, osc, volume, presets, preview, null);
            using var lifecycle = new OscClientLifecycle(emitter);

            var diag = new CameraSwitcherTabDiagnostics(coord, presets, lifecycle, conn);
            var snap = diag.GetSnapshot();

            Assert.AreEqual(TabStatus.Initializing, snap.Status);
            Assert.AreEqual(0, snap.CameraCount);
            Assert.IsNull(snap.ActiveCameraId);
            Assert.IsNull(snap.EditingCameraId);
            Assert.AreEqual("Stopped", snap.OscState);
            Assert.IsFalse(snap.IpcConnected);
            Assert.IsNull(snap.LastPresetSaveAt);
            Assert.IsNull(snap.ActivePresetName);
            Assert.IsNotNull(snap.FailureCounts);
        }
    }
}
