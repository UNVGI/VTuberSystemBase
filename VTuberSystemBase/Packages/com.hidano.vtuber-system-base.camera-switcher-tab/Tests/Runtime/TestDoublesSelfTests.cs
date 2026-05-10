#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Task 1.5 acceptance tests: smoke checks against the Fake adapter set so
    /// that subsequent Domain tests can rely on the doubles' contract.
    /// </summary>
    [TestFixture]
    public sealed class TestDoublesSelfTests
    {
        [Test]
        public void FakeUiCommandClient_PublishStateRecordsCall()
        {
            var client = new FakeUiCommandClient();
            var result = client.PublishState("topic/x", new { v = 1 });
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, client.Sent.Count);
            Assert.AreEqual("topic/x", client.Sent[0].Topic);
            Assert.AreEqual(MessageKind.State, client.Sent[0].Kind);
        }

        [Test]
        public void FakeUiSubscriptionClient_EmitFiresMatchingSubscribers()
        {
            var sub = new FakeUiSubscriptionClient();
            var received = 0;
            using var token = sub.Subscribe<int>("t", MessageKind.State, env => received += env.Payload);
            sub.Emit("t", 7);
            sub.Emit("t", 2);
            sub.Emit("other", 1000);
            Assert.AreEqual(9, received);
        }

        [Test]
        public async Task FakeOscEmitter_StartSendStopRoundtrip()
        {
            var osc = new FakeOscEmitter();
            Assert.AreEqual(OscEmitterState.Stopped, osc.State);
            await osc.StartAsync("127.0.0.1", 57300);
            Assert.AreEqual(OscEmitterState.Running, osc.State);
            var bytes = new byte[UcapiFlatRecord.ExpectedSize];
            var rec = UcapiFlatRecord.FromBytes(bytes);
            var send = osc.Send("/ucapi/camera/cam-a/flat", rec);
            Assert.IsTrue(send.Success);
            Assert.AreEqual(1, osc.Sent.Count);
            await osc.StopAsync();
            Assert.AreEqual(OscEmitterState.Stopped, osc.State);
        }

        [Test]
        public void FakeOscEmitter_RejectsSendBeforeStart()
        {
            var osc = new FakeOscEmitter();
            var bytes = new byte[1];
            var send = osc.Send("/x", UcapiFlatRecord.FromBytes(bytes));
            Assert.IsFalse(send.Success);
            Assert.AreEqual(OscFailureKind.NotStarted, send.Failure!.Value.Kind);
        }

        [Test]
        public void FakeTimeProvider_AdvanceFiresDebounceAtBoundary()
        {
            var time = new FakeTimeProvider();
            var fired = 0;
            using var timer = time.CreateDebounce(TimeSpan.FromMilliseconds(500), () => fired++);
            timer.Bump();
            time.Advance(TimeSpan.FromMilliseconds(499));
            Assert.AreEqual(0, fired, "Debounce must not fire before window elapses.");
            time.Advance(TimeSpan.FromMilliseconds(2));
            Assert.AreEqual(1, fired);
            // No further fires without another Bump.
            time.Advance(TimeSpan.FromSeconds(10));
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void FakeTimeProvider_BumpResetsWindow()
        {
            var time = new FakeTimeProvider();
            var fired = 0;
            using var timer = time.CreateDebounce(TimeSpan.FromMilliseconds(500), () => fired++);
            timer.Bump();
            time.Advance(TimeSpan.FromMilliseconds(400));
            timer.Bump(); // resets the deadline
            time.Advance(TimeSpan.FromMilliseconds(400));
            Assert.AreEqual(0, fired);
            time.Advance(TimeSpan.FromMilliseconds(101));
            Assert.AreEqual(1, fired);
        }

        [Test]
        public async Task FakePresetStore_SaveAllRoundtripsWithLoadAll()
        {
            var store = new FakePresetStore();
            var presets = new[]
            {
                new PresetPayload { Name = "P1" },
                new PresetPayload { Name = "P2" },
            };
            var save = await store.SaveAllAsync(presets, "P1");
            Assert.IsTrue(save.Success);
            Assert.AreEqual(1, store.SaveCallCount);
            var load = await store.LoadAllAsync();
            Assert.IsTrue(load.Result.Success);
            Assert.AreEqual(2, load.Presets.Count);
            Assert.AreEqual("P1", load.ActivePresetName);
        }

        [Test]
        public async Task FakePresetStore_ForcedFailureSurfacesKind()
        {
            var store = new FakePresetStore { ForceSaveFailure = PresetIoFailureKind.WriteFailed };
            var save = await store.SaveAllAsync(Array.Empty<PresetPayload>(), null);
            Assert.IsFalse(save.Success);
            Assert.AreEqual(PresetIoFailureKind.WriteFailed, save.FailureKind);
        }

        [Test]
        public async Task FakePreviewHandleResolver_HitAndMiss()
        {
            var resolver = new FakePreviewHandleResolver();
            var handle = new object();
            resolver.Handles["preview/cam-a"] = handle;
            var hit = await resolver.ResolveAsync("preview/cam-a");
            Assert.IsTrue(hit.Found);
            Assert.AreSame(handle, hit.Handle);
            var miss = await resolver.ResolveAsync("preview/cam-b");
            Assert.IsFalse(miss.Found);
            resolver.Release("preview/cam-a");
            CollectionAssert.Contains(resolver.Released, "preview/cam-a");
        }

        [Test]
        public void FakeConnectionStatus_TransitionsRaiseEvents()
        {
            var status = new FakeConnectionStatus(ConnectionStatusCode.Disconnected);
            ConnectionStatusEvent? captured = null;
            status.OnStatusChanged += e => captured = e;
            status.SetStatus(ConnectionStatusCode.Connected);
            Assert.IsNotNull(captured);
            Assert.AreEqual(ConnectionStatusCode.Disconnected, captured!.Value.From);
            Assert.AreEqual(ConnectionStatusCode.Connected, captured!.Value.To);
            Assert.IsTrue(status.IsConnected);
        }

        [Test]
        public void FakeFlatRecordSerializer_ReturnsExpectedSizeBuffer()
        {
            var serializer = new FakeFlatRecordSerializer();
            var snap = new CameraSnapshot { FrameCounter = 0xCAFE };
            var result = serializer.Serialize(in snap);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(UcapiFlatRecord.ExpectedSize, result.Record.Length);
            var bytes = result.Record.AsBytes();
            Assert.AreEqual(0xFE, bytes[0]);
            Assert.AreEqual(0xCA, bytes[1]);
        }
    }
}
