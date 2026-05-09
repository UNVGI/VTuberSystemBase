#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.PlayMode
{
    public sealed class VolumeOverrideHandlerPlayModeTests
    {
        [UnityTest]
        public IEnumerator BloomIntensity_AppliedToLiveVolumeProfile()
        {
            using var dispatcher = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeOutputCommandDispatcher();
            using var roots = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeOutputSceneRoots();
            var sink = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag, () => 0);
            using var sut = new VolumeOverrideHandler(dispatcher, roots, sink, reporter, logger, diag);
            sut.Start(new[] { typeof(Bloom) });
            yield return null;

            dispatcher.EmitState(StageLightingTopics.VolumeOverrideEnabled(typeof(Bloom).FullName!), true);
            dispatcher.EmitState(StageLightingTopics.VolumeOverrideParam(typeof(Bloom).FullName!, "intensity"),
                new VolumeOverrideParamValueDto(ParamKind.Float, null, null, 1.5f, null, null, null));
            yield return null;

            Assert.That(roots.GlobalVolumeProfile!.TryGet<Bloom>(out var bloom), Is.True);
            Assert.That(bloom.intensity.value, Is.EqualTo(1.5f));
            Assert.That(bloom.intensity.overrideState, Is.True);
        }
    }
}
