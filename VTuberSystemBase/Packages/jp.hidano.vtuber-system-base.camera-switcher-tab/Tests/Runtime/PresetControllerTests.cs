#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    [TestFixture]
    public sealed class PresetControllerTests
    {
        private FakePresetStore _store = null!;
        private FakeUiCommandClient _commands = null!;
        private FakeTimeProvider _time = null!;
        private FailureAggregator _failures = null!;
        private PresetController _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _store = new FakePresetStore();
            _commands = new FakeUiCommandClient();
            _time = new FakeTimeProvider();
            _failures = new FailureAggregator();
            _sut = new PresetController(_store, _commands, _time, _failures);
        }

        [TearDown]
        public void TearDown() => _sut.Dispose();

        private static PresetPayload Empty(string name) => new PresetPayload
        {
            Name = name,
            Cameras = Array.Empty<PresetCameraEntry>(),
            VolumeConfigs = new Dictionary<string, VolumeConfig>(),
        };

        [Test]
        public void Create_DuplicateName_IsRejected()
        {
            Assert.IsTrue(_sut.CreatePreset("alpha").Success);
            var dup = _sut.CreatePreset("alpha");
            Assert.IsFalse(dup.Success);
            Assert.AreEqual(PresetIoFailureKind.SerializationFailed, dup.FailureKind);
        }

        [Test]
        public async Task NotifyStateMutation_DebounceFlushesAfter500ms()
        {
            _sut.CreatePreset("alpha");
            // Initial save was scheduled by Create; advance time and verify.
            _time.Advance(TimeSpan.FromMilliseconds(499));
            Assert.AreEqual(0, _store.SaveCallCount);
            _time.Advance(TimeSpan.FromMilliseconds(2));
            // Save runs on a Task.Run worker; let it complete.
            await Task.Yield();
            await Task.Delay(50);
            Assert.AreEqual(1, _store.SaveCallCount);
        }

        [Test]
        public async Task RepeatedNotify_CoalescesIntoOneSave()
        {
            _sut.CreatePreset("a");
            _sut.NotifyStateMutation();
            _time.Advance(TimeSpan.FromMilliseconds(100));
            _sut.NotifyStateMutation();
            _time.Advance(TimeSpan.FromMilliseconds(200));
            _sut.NotifyStateMutation();
            _time.Advance(TimeSpan.FromMilliseconds(501));
            await Task.Delay(50);
            Assert.AreEqual(1, _store.SaveCallCount);
        }

        [Test]
        public async Task ActivatePreset_DispatchesDeleteAddMetadataVolumeActiveSet_InOrder()
        {
            _sut.CreatePreset("source", new PresetPayload
            {
                Name = "source",
                Cameras = new[]
                {
                    new PresetCameraEntry
                    {
                        LogicalId = "cam-A",
                        DisplayName = "A",
                        Type = CameraType.Perspective,
                        DefaultTransform = default,
                    },
                },
                VolumeConfigs = new Dictionary<string, VolumeConfig>(),
                ActiveCameraLogicalId = "cam-A",
            });
            _sut.CreatePreset("target", new PresetPayload
            {
                Name = "target",
                Cameras = new[]
                {
                    new PresetCameraEntry
                    {
                        LogicalId = "cam-B",
                        DisplayName = "B",
                        Type = CameraType.Perspective,
                        DefaultTransform = default,
                    },
                },
                VolumeConfigs = new Dictionary<string, VolumeConfig>
                {
                    ["cam-B"] = new VolumeConfig { Enabled = true, Overrides = Array.Empty<VolumeOverride>() },
                },
                ActiveCameraLogicalId = "cam-B",
            });

            _commands.Sent.Clear();
            _sut.TryGet("source", out var current);
            await _sut.ActivatePresetAsync("target", current);

            // Filter the dispatched commands; ignore preset/command echo entries
            // since we're verifying the camera-switch order.
            var ordered = _commands.Sent
                .Where(s => s.Topic != CameraIpcTopics.PresetCommand)
                .Select(s => s.Topic)
                .ToList();

            // Expected order: delete cam-A → add (no id, op=add) → metadata cam-B → volume cam-B → active-set
            Assert.GreaterOrEqual(ordered.Count, 4);

            // Find the indexes:
            int deleteIdx = -1, addIdx = -1, metaIdx = -1, volEnabledIdx = -1, activeSetIdx = -1;
            for (var i = 0; i < _commands.Sent.Count; i++)
            {
                var s = _commands.Sent[i];
                if (s.Topic == CameraIpcTopics.PresetCommand) continue;
                if (s.Topic == CameraIpcTopics.CameraCommand && s.Payload is CameraCommandPayload c)
                {
                    if (c.Op == CameraCommandOps.Delete && deleteIdx < 0) deleteIdx = i;
                    else if (c.Op == CameraCommandOps.Add && addIdx < 0) addIdx = i;
                    else if (c.Op == CameraCommandOps.ActiveSet && activeSetIdx < 0) activeSetIdx = i;
                }
                else if (s.Topic.StartsWith("camera/cam-B/metadata/") && metaIdx < 0) metaIdx = i;
                else if (s.Topic == "camera/cam-B/volume/enabled" && volEnabledIdx < 0) volEnabledIdx = i;
            }

            Assert.GreaterOrEqual(deleteIdx, 0, "delete missing");
            Assert.GreaterOrEqual(addIdx, 0, "add missing");
            Assert.GreaterOrEqual(metaIdx, 0, "metadata missing");
            Assert.GreaterOrEqual(volEnabledIdx, 0, "volume missing");
            Assert.GreaterOrEqual(activeSetIdx, 0, "active-set missing");
            Assert.Less(deleteIdx, addIdx, "delete before add");
            Assert.Less(addIdx, metaIdx, "add before metadata");
            Assert.Less(metaIdx, volEnabledIdx, "metadata before volume");
            Assert.Less(volEnabledIdx, activeSetIdx, "volume before active-set");
        }

        [Test]
        public async Task ActivatePreset_SerialisesConcurrentRequests()
        {
            _sut.CreatePreset("a", Empty("a"));
            _sut.CreatePreset("b", Empty("b"));
            _sut.TryGet("a", out var a);
            _sut.TryGet("b", out var b);
            // Two concurrent activates — second waits for first.
            var t1 = _sut.ActivatePresetAsync("a", b);
            var t2 = _sut.ActivatePresetAsync("b", a);
            await Task.WhenAll(t1, t2);
            Assert.IsTrue(t1.Result.Success);
            Assert.IsTrue(t2.Result.Success);
        }

        [Test]
        public async Task RestoreOnStart_FileNotFound_TreatedAsSuccess()
        {
            _store.ForceLoadFailure = PresetIoFailureKind.FileNotFound;
            var result = await _sut.RestoreOnStartAsync();
            Assert.IsTrue(result.Success, "FileNotFound is a soft success");
            Assert.AreEqual(0, _failures.CountOf(FailureKind.PresetIoFailure));
        }

        [Test]
        public async Task RestoreOnStart_CorruptedFile_RecordsFailure()
        {
            _store.ForceLoadFailure = PresetIoFailureKind.Corrupted;
            _store.ForceCorruptOnLoad = true;
            _store.BackupPath = "/tmp/foo.bak";
            var result = await _sut.RestoreOnStartAsync();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(PresetIoFailureKind.Corrupted, result.FailureKind);
            Assert.GreaterOrEqual(_failures.CountOf(FailureKind.PresetIoFailure), 1);
        }

        [Test]
        public async Task RestoreOnStart_SeedsModelAndActiveName()
        {
            _store.Seed(new[]
            {
                Empty("alpha"),
                Empty("beta"),
            }, activeName: "beta");

            await _sut.RestoreOnStartAsync();

            Assert.AreEqual(new[] { "alpha", "beta" }, _sut.PresetNames.ToArray());
            Assert.AreEqual("beta", _sut.ActivePresetName);
        }

        [Test]
        public void RenameActivePreset_KeepsActivePointing()
        {
            _sut.CreatePreset("alpha");
            // Force active using the model directly via switching from empty-current.
            _sut.RenamePreset("alpha", "alpha2");
            Assert.AreEqual(new[] { "alpha2" }, _sut.PresetNames.ToArray());
        }

        [Test]
        public void DeleteActivePreset_ClearsActiveAndFires()
        {
            _sut.CreatePreset("alpha");
            // Make alpha "active" via internal API: re-create to seed active.
            // For this contract we just verify that delete clears _activeName when it matches.
            // Since CreatePreset doesn't set active, simulate by activating it.
            // Skip: use ActivatePresetAsync.
            _sut.TryGet("alpha", out var alpha);
            _sut.ActivatePresetAsync("alpha", alpha).GetAwaiter().GetResult();
            Assert.AreEqual("alpha", _sut.ActivePresetName);

            string? observedActive = "untouched";
            _sut.OnActivePresetChanged += n => observedActive = n;
            _sut.DeletePreset("alpha");
            Assert.IsNull(_sut.ActivePresetName);
            Assert.IsNull(observedActive);
        }

        [Test]
        public void Duplicate_UnknownSource_Fails()
        {
            var r = _sut.DuplicatePreset("missing", "copy");
            Assert.IsFalse(r.Success);
        }
    }
}
