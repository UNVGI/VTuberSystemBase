#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    [TestFixture]
    public sealed class VolumeUiStateManagerTests
    {
        private FakeUiCommandClient _commands = null!;
        private FailureAggregator _failures = null!;
        private FakeTimeProvider _time = null!;
        private VolumeUiStateManager _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _commands = new FakeUiCommandClient();
            _failures = new FailureAggregator();
            _time = new FakeTimeProvider();
            _sut = new VolumeUiStateManager(_commands, _failures, _time);
        }

        [Test]
        public async Task OnEditTargetChanged_RequestsMetadata_AndCachesOnSuccess()
        {
            var schema = new VolumeMetadataResponse
            {
                Overrides = new[]
                {
                    new VolumeOverrideSchema
                    {
                        Type = "Bloom",
                        DisplayName = "Bloom",
                        Params = new[]
                        {
                            new VolumeParamSchema
                            {
                                Name = "intensity",
                                TypeTag = "float",
                                Default = JsonSerializer.SerializeToElement(0.5f),
                                DisplayName = "Intensity",
                            },
                        },
                    },
                },
            };
            _commands.RequestResponder = req => schema;

            var id = new CameraId("cam-1");
            await _sut.OnEditTargetChangedAsync(id);

            Assert.IsTrue(_sut.HasState(id));
            Assert.IsTrue(_sut.TryGet(id, out var state));
            Assert.IsFalse(state.SchemaFailed);
            Assert.IsNotNull(state.Schema);
            Assert.AreEqual(1, _commands.Requests.Count);
            Assert.AreEqual("camera/cam-1/volume/overrides/metadata", _commands.Requests[0].Topic);
        }

        [Test]
        public async Task OnEditTargetChanged_OnFailure_RecordsAggregateAndKeepsTabAlive()
        {
            // FakeUiCommandClient.RequestResponder is null → returns Timeout.
            var id1 = new CameraId("cam-1");
            await _sut.OnEditTargetChangedAsync(id1);
            Assert.IsTrue(_sut.HasState(id1));
            Assert.IsTrue(_sut.TryGet(id1, out var state));
            Assert.IsTrue(state.SchemaFailed);
            Assert.AreEqual(1, _failures.CountOf(FailureKind.VolumeMetadataFailure));

            // A second camera continues to work.
            _commands.RequestResponder = _ => new VolumeMetadataResponse { Overrides = Array.Empty<VolumeOverrideSchema>() };
            var id2 = new CameraId("cam-2");
            await _sut.OnEditTargetChangedAsync(id2);
            Assert.IsTrue(_sut.TryGet(id2, out var state2));
            Assert.IsFalse(state2.SchemaFailed);
        }

        [Test]
        public void DragSuppressesEcho_AndEndDragApplies()
        {
            var id = new CameraId("cam-1");
            _sut.GetOrCreate(id); // seed
            _sut.BeginDrag("Bloom", "intensity");

            var v = JsonSerializer.SerializeToElement(0.9f);
            _sut.ApplyOverrideParamState(id, "Bloom", "intensity", v);

            Assert.IsTrue(_sut.TryGet(id, out var state));
            Assert.IsFalse(state.ParamValues.ContainsKey(("Bloom", "intensity")), "Echo must be suppressed during drag");

            _sut.EndDrag("Bloom", "intensity");
            var flushed = _sut.TryFlushSuppressedEcho(id, "Bloom", "intensity", out var flushedValue);
            Assert.IsTrue(flushed);
            Assert.AreEqual(0.9f, flushedValue.GetSingle());
            Assert.IsTrue(state.ParamValues.ContainsKey(("Bloom", "intensity")));
        }

        [Test]
        public void NotDragging_AppliesEchoImmediately()
        {
            var id = new CameraId("cam-1");
            _sut.GetOrCreate(id);
            var v = JsonSerializer.SerializeToElement(0.3f);
            _sut.ApplyOverrideParamState(id, "Bloom", "intensity", v);

            Assert.IsTrue(_sut.TryGet(id, out var state));
            Assert.IsTrue(state.ParamValues.TryGetValue(("Bloom", "intensity"), out var stored));
            Assert.AreEqual(0.3f, stored.GetSingle());
        }

        [Test]
        public void ApplyOverridesListState_ResetsEnabledMap()
        {
            var id = new CameraId("cam-1");
            _sut.GetOrCreate(id);
            _sut.ApplyOverrideEnabledState(id, "Vignette", true);
            _sut.ApplyOverridesListState(id, new[]
            {
                new VolumeOverrideEntry { Type = "Bloom", Enabled = true },
                new VolumeOverrideEntry { Type = "Tonemapping", Enabled = false },
            });
            Assert.IsTrue(_sut.TryGet(id, out var state));
            Assert.AreEqual(2, state.OverrideEnabled.Count);
            Assert.IsTrue(state.OverrideEnabled["Bloom"]);
            Assert.IsFalse(state.OverrideEnabled["Tonemapping"]);
            Assert.IsFalse(state.OverrideEnabled.ContainsKey("Vignette"));
        }

        [Test]
        public void Forget_RemovesCachedState()
        {
            var id = new CameraId("cam-1");
            _sut.GetOrCreate(id);
            Assert.IsTrue(_sut.HasState(id));
            _sut.Forget(id);
            Assert.IsFalse(_sut.HasState(id));
        }
    }
}
