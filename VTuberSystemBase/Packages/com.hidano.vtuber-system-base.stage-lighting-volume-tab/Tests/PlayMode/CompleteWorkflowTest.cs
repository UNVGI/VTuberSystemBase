#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
    /// End-to-end preset workflow + apply integration test (Task 8.2, Requirements 7.4,
    /// 7.6, 7.7, 8.5, 8.8, 8.11, 12.5). Walks the full create → save → restart → restore
    /// cycle through <see cref="JsonPresetStorage"/> on a temp directory.
    /// </summary>
    [TestFixture]
    public sealed class CompleteWorkflowTest
    {
        private string _tempDir = "";
        private string _filePath = "";

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "vtuber-workflow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _filePath = Path.Combine(_tempDir, "stage-lighting-volume-tab.json");
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public async Task CompleteCycle_CreatePreset_AddLight_VolumeEdit_Save_RestartRestore()
        {
            var logger = new FakeDiagnosticsLogger();
            var ipc = new FakeIpcClient();
            ipc.RequestResponder = _ => new VolumeOverrideSchemaDto(1, new List<VolumeOverrideTypeDto>());
            var clock = new FakeClock();
            var conn = new FakeConnectionStatus(ConnectionStatusCode.Connected);

            // First session.
            var storage1 = new JsonPresetStorage(_filePath, logger);
            var lightList1 = new LightListState(ipc, logger);
            var stageCatalog1 = new StageCatalogState(ipc, logger);
            var volumeCache1 = new VolumeSchemaCache(ipc, logger);
            var debounce1 = new DebounceFlusher(StageLightingVolumeTabViewModel.DefaultDebounceInterval, clock);
            var diag1 = new StageTabDiagnostics(logger);
            var vm1 = new StageLightingVolumeTabViewModel(
                ipc, ipc, conn, storage1, lightList1, stageCatalog1, volumeCache1, debounce1, clock,
                diag1, logger);

            vm1.OnActivated();
            await Task.Delay(30);

            vm1.CreatePreset("Daylight");
            vm1.Presets[0].Lights.Add(new LightConfigDto
            {
                DisplayName = "Sun",
                Type = LightTypeDto.Directional,
                Color = new ColorDto(1, 1, 1, 1),
                Intensity = 1.5f,
            });
            vm1.Presets[0].VolumeOverrides.Add(new VolumeOverrideConfigDto
            {
                TypeFullName = "UnityEngine.Rendering.Universal.Bloom",
                Enabled = true,
                Params = new Dictionary<string, VolumeOverrideParamValueDto>
                {
                    ["intensity"] = new VolumeOverrideParamValueDto(
                        ParamKind.Float, null, null, 0.5f, null, null, null),
                },
            });
            vm1.ActivatePreset("Daylight");

            // Push debounced flush past the 500ms window and let the storage save.
            clock.Advance(TimeSpan.FromMilliseconds(600));
            await Task.Delay(50);

            vm1.Dispose();

            Assert.That(File.Exists(_filePath), Is.True, "Preset file should have been written.");

            // Second session: brand-new everything except the on-disk file.
            var storage2 = new JsonPresetStorage(_filePath, logger);
            var ipc2 = new FakeIpcClient();
            ipc2.RequestResponder = _ => new VolumeOverrideSchemaDto(1, new List<VolumeOverrideTypeDto>());
            var clock2 = new FakeClock();
            var conn2 = new FakeConnectionStatus(ConnectionStatusCode.Connected);
            var lightList2 = new LightListState(ipc2, logger);
            var stageCatalog2 = new StageCatalogState(ipc2, logger);
            var volumeCache2 = new VolumeSchemaCache(ipc2, logger);
            var debounce2 = new DebounceFlusher(StageLightingVolumeTabViewModel.DefaultDebounceInterval, clock2);
            var diag2 = new StageTabDiagnostics(logger);
            var vm2 = new StageLightingVolumeTabViewModel(
                ipc2, ipc2, conn2, storage2, lightList2, stageCatalog2, volumeCache2, debounce2, clock2,
                diag2, logger);

            vm2.OnActivated();
            await Task.Delay(50);

            Assert.That(vm2.Presets, Has.Count.EqualTo(1));
            Assert.That(vm2.Presets[0].Name, Is.EqualTo("Daylight"));
            Assert.That(vm2.ActivePresetName, Is.EqualTo("Daylight"));

            vm2.Dispose();
        }
    }
}
