#nullable enable
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Preview;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.PlayMode
{
    /// <summary>
    /// Smoke-grade leak check that exercises the major lifecycle paths five times
    /// without going through OutputSceneBootstrapper. We construct the handler set
    /// directly with the editor doubles and verify zero residual GameObjects, zero
    /// stage instances, and a clear preview-host locator after teardown.
    /// </summary>
    public sealed class BootstrapPlayModeTests
    {
        [UnityTest]
        public IEnumerator PlayModeRepeats5Times_NoLeak()
        {
            for (int i = 0; i < 5; i++)
            {
                using var dispatcher = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeOutputCommandDispatcher();
                using var roots = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeOutputSceneRoots();
                var sink = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.RecordingMessageSink();
                var logger = new AdapterLogger();
                var diag = new StageLightingVolumeOutputAdapterDiagnostics();
                var reporter = new AdapterErrorReporter(sink, logger, diag, () => 0);
                var provider = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeInstantiationProvider();
                provider.Configure("S");

                using var stage = new StageHandler(dispatcher, roots, provider,
                    new StageCatalogBuilder(logger), reporter, logger, diag, sink);
                stage.Start();
                yield return null;
                yield return stage.HandleCommandAsync(new StageCommandDto("load", "S"));

                using var light = new LightHandler(dispatcher, roots, sink, reporter, logger, diag);
                light.Start();
                dispatcher.EmitEvent(StageLightingTopics.LightCommand,
                    new LightCommandDto("add", null,
                        new LightInitialDto(LightTypeDto.Point, default, new ColorDto(1, 1, 1, 1), 1, 10, 30, "L")));
                yield return null;
                light.Dispose();

                yield return stage.HandleCommandAsync(new StageCommandDto("unload", null));

                stage.Dispose();
                yield return null;
            }

            // After all iterations, no Light_<id> GameObjects should remain in memory.
            var leakedLights = Resources.FindObjectsOfTypeAll<Light>()
                .Where(l => l != null && l.gameObject != null && l.gameObject.name.StartsWith("Light_"))
                .ToList();
            Assert.That(leakedLights, Is.Empty, "Light_ GameObjects leaked across iterations");

            var hosts = Resources.FindObjectsOfTypeAll<StagePreviewHost>();
            Assert.That(hosts, Is.Empty, "StagePreviewHost leaked");

            Assert.That(StagePreviewHostLocator.Current, Is.Null);
        }
    }
}
