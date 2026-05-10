#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    /// <summary>
    /// Cross-handler smoke tests verifying Task 8.1's contract: every IPC handler swallows
    /// thrown exceptions, logs at <c>Error</c>, and (where appropriate) publishes a domain
    /// error event so the renderer keeps running.
    /// </summary>
    public sealed class HandlerExceptionPathsTests
    {
        [Test]
        public void LightHandler_AddPath_IdFactoryThrows_ReportsInternalError()
        {
            using var dispatcher = new FakeOutputCommandDispatcher();
            using var roots = new FakeOutputSceneRoots();
            var sink = new RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag, () => 0);
            using var handler = new LightHandler(dispatcher, roots, sink, reporter, logger, diag,
                idFactory: () => throw new System.InvalidOperationException("boom"));
            handler.Start();
            sink.Clear();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("light_error"));
            dispatcher.EmitEvent(StageLightingTopics.LightCommand,
                new LightCommandDto("add", null,
                    new LightInitialDto(LightTypeDto.Point, default, new ColorDto(1, 1, 1, 1), 1, 10, 30, "L")));

            var errs = sink.PublishedEvents.Where(p => p.Topic == StageLightingTopics.LightError).ToList();
            Assert.That(errs.Count, Is.EqualTo(1));
            var dto = (LightErrorDto)errs[0].Payload!;
            Assert.That(dto.ErrorCode, Is.EqualTo("internal_error"));
        }

        [Test]
        public void StageHandler_LoadFailure_KeepsPreviousAndPublishesLoadFailed()
        {
            using var dispatcher = new FakeOutputCommandDispatcher();
            using var roots = new FakeOutputSceneRoots();
            var sink = new RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag, () => 0);
            var provider = new FakeInstantiationProvider();
            provider.Configure("Stages/A");
            provider.Configure("Stages/Bad", new FakeInstantiationProvider.KeyConfig
            {
                Success = false,
                ErrorCode = "load_failed",
                ErrorMessage = "boom",
            });
            using var stage = new StageHandler(dispatcher, roots, provider,
                new StageCatalogBuilder(logger), reporter, logger, diag, sink);
            stage.Start();
            stage.HandleCommandAsync(new StageCommandDto("load", "Stages/A")).GetAwaiter().GetResult();
            var prevStage = stage.State.CurrentStage;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("stage_load_failed"));
            stage.HandleCommandAsync(new StageCommandDto("load", "Stages/Bad")).GetAwaiter().GetResult();
            Assert.That(stage.State.CurrentStage, Is.SameAs(prevStage));
            var failed = sink.PublishedEvents.Where(p => p.Topic == StageLightingTopics.StageLoadFailed).ToList();
            Assert.That(failed.Count, Is.EqualTo(1));
        }

        [Test]
        public void VolumeOverrideHandler_UnknownType_DoesNotThrow()
        {
            using var dispatcher = new FakeOutputCommandDispatcher();
            using var roots = new FakeOutputSceneRoots();
            var sink = new RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag, () => 0);
            using var vh = new VolumeOverrideHandler(dispatcher, roots, sink, reporter, logger, diag);
            vh.Start(new[] { typeof(Bloom) });

            // Emitting on a topic that was never registered (unknown type) is a no-op for the
            // dispatcher table. The contract under test is "no exception, no crash".
            Assert.DoesNotThrow(() =>
            {
                dispatcher.EmitState(StageLightingTopics.VolumeOverrideEnabled("Foo.UnknownComponent"), true);
            });
        }
    }
}
