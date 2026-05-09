#nullable enable
using System;
using NUnit.Framework;
using UnityEngine.Rendering.Universal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class VolumeOverrideHandlerTests
    {
        private FakeOutputCommandDispatcher _dispatcher = null!;
        private FakeOutputSceneRoots _roots = null!;
        private RecordingMessageSink _sink = null!;
        private AdapterLogger _logger = null!;
        private StageLightingVolumeOutputAdapterDiagnostics _diag = null!;
        private AdapterErrorReporter _reporter = null!;
        private VolumeOverrideHandler _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _dispatcher = new FakeOutputCommandDispatcher();
            _roots = new FakeOutputSceneRoots();
            _sink = new RecordingMessageSink();
            _logger = new AdapterLogger();
            _diag = new StageLightingVolumeOutputAdapterDiagnostics();
            _reporter = new AdapterErrorReporter(_sink, _logger, _diag, () => 0);
            _sut = new VolumeOverrideHandler(_dispatcher, _roots, _sink, _reporter, _logger, _diag);
        }

        [TearDown]
        public void TearDown() { _sut.Dispose(); _roots.Dispose(); _dispatcher.Dispose(); }

        [Test]
        public void Start_RegistersHandlersAndRecordsTypeCount()
        {
            _sut.Start(new[] { typeof(Bloom), typeof(Tonemapping), typeof(ColorAdjustments) });
            // schema request should be live.
            Assert.That(_dispatcher.HasRequestHandler(StageLightingTopics.VolumeOverrideSchema), Is.True);
            // enabled topics should be registered for each known type.
            Assert.That(_dispatcher.HasStateHandler(StageLightingTopics.VolumeOverrideEnabled(typeof(Bloom).FullName!)), Is.True);
            Assert.That(_diag.VolumeOverrideTypeCount, Is.EqualTo(3));
        }

        [Test]
        public void Enabled_AddsComponent_AndTogglesActive()
        {
            _sut.Start(new[] { typeof(Bloom) });
            _dispatcher.EmitState(StageLightingTopics.VolumeOverrideEnabled(typeof(Bloom).FullName!), true);
            Assert.That(_roots.GlobalVolumeProfile!.Has<Bloom>(), Is.True);
            Assert.That(_roots.GlobalVolumeProfile!.TryGet<Bloom>(out var bloom), Is.True);
            Assert.That(bloom.active, Is.True);

            _dispatcher.EmitState(StageLightingTopics.VolumeOverrideEnabled(typeof(Bloom).FullName!), false);
            Assert.That(bloom.active, Is.False);
        }

        [Test]
        public void Param_AppliesValueOnExistingOrAddedComponent()
        {
            _sut.Start(new[] { typeof(Bloom) });
            var dto = new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1.5f, null, null, null);
            _dispatcher.EmitState(StageLightingTopics.VolumeOverrideParam(typeof(Bloom).FullName!, "intensity"), dto);
            Assert.That(_roots.GlobalVolumeProfile!.TryGet<Bloom>(out var bloom), Is.True);
            Assert.That(bloom.intensity.value, Is.EqualTo(1.5f));
            Assert.That(bloom.intensity.overrideState, Is.True);
        }

        [Test]
        public void SchemaRequest_ReturnsCachedSchema()
        {
            _sut.Start(new[] { typeof(Bloom), typeof(Tonemapping) });
            var resp = _dispatcher.InvokeRequest<EmptyDto, VolumeOverrideSchemaDto>(
                StageLightingTopics.VolumeOverrideSchema, default);
            Assert.That(resp.Types.Count, Is.EqualTo(2));
        }

        [Test]
        public void Dispose_RemovesAddedComponentsFromProfile()
        {
            _sut.Start(new[] { typeof(Bloom) });
            _dispatcher.EmitState(StageLightingTopics.VolumeOverrideEnabled(typeof(Bloom).FullName!), true);
            Assert.That(_roots.GlobalVolumeProfile!.Has<Bloom>(), Is.True);
            _sut.Dispose();
            Assert.That(_roots.GlobalVolumeProfile!.Has<Bloom>(), Is.False);
        }
    }
}
