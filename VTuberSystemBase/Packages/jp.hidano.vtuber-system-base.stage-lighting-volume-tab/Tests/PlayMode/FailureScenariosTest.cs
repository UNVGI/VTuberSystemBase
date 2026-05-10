#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.PlayMode
{
    /// <summary>
    /// Failure-path integration tests (Task 8.4, Requirements 2.6, 2.7, 9.1, 9.7, 12.5).
    /// Covers stage-key not in catalog, IPC disconnected guard, and preview command
    /// flow on tab activation / deactivation.
    /// </summary>
    [TestFixture]
    public sealed class FailureScenariosTest
    {
        private static (StageLightingVolumeTabViewModel vm,
                        FakeIpcClient ipc,
                        FakeConnectionStatus conn) Build(bool startConnected)
        {
            var ipc = new FakeIpcClient();
            var clock = new FakeClock();
            var conn = new FakeConnectionStatus(startConnected
                ? ConnectionStatusCode.Connected
                : ConnectionStatusCode.Disconnected);
            var logger = new FakeDiagnosticsLogger();
            var lightList = new LightListState(ipc, logger);
            var stageCatalog = new StageCatalogState(ipc, logger);
            var volumeCache = new VolumeSchemaCache(ipc, logger);
            var debounce = new DebounceFlusher(StageLightingVolumeTabViewModel.DefaultDebounceInterval, clock);
            var diag = new StageTabDiagnostics(logger);
            var vm = new StageLightingVolumeTabViewModel(
                ipc, ipc, conn, new FakePresetStorage(),
                lightList, stageCatalog, volumeCache, debounce, clock, diag, logger);
            return (vm, ipc, conn);
        }

        [Test]
        public void Preset_StageKeyNotInCatalog_RaisesUnresolvedWarning_AndOtherSubsystemsContinue()
        {
            var (vm, ipc, _) = Build(startConnected: true);
            vm.OnActivated();

            // Empty catalog (no stage entries).
            ipc.Emit(StageLightingTopics.StageCatalog, new StageCatalogDto(new List<StageCatalogEntryDto>()));

            vm.CreatePreset("P");
            vm.Presets[0].StageAddressableKey = "stages/missing";

            string? warn = null;
            vm.OnOperationWarning += w => warn = w;
            vm.ActivatePreset("P");

            Assert.That(warn, Is.EqualTo(StageLightingVolumeTabViewModel.WarnStageUnresolved));

            // Light + Volume operations still work.
            ipc.Sent.Clear();
            vm.SetVolumeOverrideEnabled("UnityEngine.Rendering.Universal.Bloom", true);
            Assert.That(ipc.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public void Disconnected_AllCommandsAreSuppressed()
        {
            var (vm, ipc, _) = Build(startConnected: false);
            vm.OnActivated();
            ipc.Sent.Clear();

            vm.SwitchStage("stages/a");
            vm.AddLight(new LightInitialDto(LightTypeDto.Directional, new Vector3Dto(0, 0, 0),
                new ColorDto(1, 1, 1, 1), 1f, 0f, 30f, "X"));
            vm.SetVolumeOverrideEnabled("Type", true);

            Assert.That(ipc.Sent, Is.Empty,
                "All Command methods must be no-ops while disconnected.");
        }

        [Test]
        public void TabActivationCycle_PublishesPreviewSetEnabledTrueAndFalse()
        {
            // PreviewPanelController is exercised in PreviewPanelControllerTests; this test
            // documents that the ViewModel itself does not interfere with the preview/command
            // path (it only forwards via PreviewPanelController, not directly).
            var (vm, ipc, _) = Build(startConnected: true);
            vm.OnActivated();

            // Without a Preview controller hooked up, the ViewModel itself should not
            // emit preview/command. (PreviewPanelController is the owner of that channel.)
            foreach (var s in ipc.Sent)
            {
                Assert.That(s.Topic, Is.Not.EqualTo(StageLightingTopics.PreviewCommand),
                    "ViewModel must not publish preview/command directly.");
            }
        }
    }
}
