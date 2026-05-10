#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    /// <summary>
    /// EditMode integration test exercising Stage + Light + Volume handlers in a single
    /// session. Preview is excluded because <c>StagePreviewHost</c> requires a MonoBehaviour
    /// Awake (covered by PlayMode tests).
    /// </summary>
    public sealed class IntegrationRoundTripTests
    {
        [Test]
        public void StageLightVolume_RoundTrip()
        {
            using var dispatcher = new FakeOutputCommandDispatcher();
            using var roots = new FakeOutputSceneRoots();
            var sink = new RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag, () => 0);
            var provider = new FakeInstantiationProvider();
            provider.Configure("TestStage");

            using var stage = new StageHandler(dispatcher, roots, provider,
                new StageCatalogBuilder(logger), reporter, logger, diag, sink);
            using var light = new LightHandler(dispatcher, roots, sink, reporter, logger, diag);
            using var volume = new VolumeOverrideHandler(dispatcher, roots, sink, reporter, logger, diag);

            stage.Start();
            light.Start();
            volume.Start(new[] { typeof(Bloom) });

            // 1) StageHandler is registered for stage/command and an initial stage/current was published.
            Assert.That(dispatcher.HasEventHandler(StageLightingTopics.StageCommand), Is.True);
            Assert.That(sink.PublishedStates.Any(p => p.Topic == StageLightingTopics.StageCurrent), Is.True);

            // 2) Stage load -> stage/current(addressableKey)
            stage.HandleCommandAsync(new StageCommandDto("load", "TestStage")).GetAwaiter().GetResult();
            var lastStage = sink.PublishedStates.Last(p => p.Topic == StageLightingTopics.StageCurrent);
            Assert.That(((StageCurrentDto)lastStage.Payload!).AddressableKey, Is.EqualTo("TestStage"));

            // 3) Light add -> light/added + lights/list
            sink.Clear();
            dispatcher.EmitEvent(StageLightingTopics.LightCommand,
                new LightCommandDto("add", null,
                    new LightInitialDto(LightTypeDto.Point, default, new ColorDto(1, 1, 1, 1), 1f, 10f, 30f, "L1")));
            var addedEvents = sink.PublishedEvents.Where(p => p.Topic == StageLightingTopics.LightAdded).ToList();
            Assert.That(addedEvents.Count, Is.EqualTo(1));
            var added = (LightAddedDto)addedEvents[0].Payload!;
            Assert.That(sink.PublishedStates.Any(p => p.Topic == StageLightingTopics.LightsList), Is.True);

            // 4) Light intensity update -> registry value updated
            dispatcher.EmitState(StageLightingTopics.LightProperty(added.LightId, StageLightingTopics.PropertyIntensity), 2.5f);
            Assert.That(light.Registry.TryGet(added.LightId, out var entry), Is.True);
            Assert.That(entry.Light.intensity, Is.EqualTo(2.5f));

            // 5) Volume override Bloom -> profile contains Bloom + intensity reflected
            dispatcher.EmitState(StageLightingTopics.VolumeOverrideEnabled(typeof(Bloom).FullName!), true);
            dispatcher.EmitState(StageLightingTopics.VolumeOverrideParam(typeof(Bloom).FullName!, "intensity"),
                new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1.5f, null, null, null));
            Assert.That(roots.GlobalVolumeProfile!.TryGet<Bloom>(out var bloom), Is.True);
            Assert.That(bloom.intensity.value, Is.EqualTo(1.5f));

            // 6) Schema request roundtrip
            var schema = dispatcher.InvokeRequest<EmptyDto, VolumeOverrideSchemaDto>(
                StageLightingTopics.VolumeOverrideSchema, default);
            Assert.That(schema.Types.Count, Is.GreaterThanOrEqualTo(1));
        }
    }
}
