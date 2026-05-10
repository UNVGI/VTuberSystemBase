#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks preset switch / orchestration semantics (Task 5.6, Requirements 7.4, 7.6,
    /// 7.7, 7.8). The fixed dispatch order is:
    /// 1) disable currently-enabled overrides, 2) load/unload stage,
    /// 3) remove existing lights, 4) add preset lights, 5/6) re-enable + apply param state.
    /// </summary>
    [TestFixture]
    public sealed class ViewModelPresetSwitchTests
    {
        [Test]
        public void ActivatePreset_DispatchesCommandsInFixedOrder()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();

            // Seed: stage catalog contains preset stage; one existing light on the
            // server side; one previously enabled override.
            ctx.ipc.Emit(StageLightingTopics.StageCatalog,
                new StageCatalogDto(new List<StageCatalogEntryDto>
                {
                    new StageCatalogEntryDto("stages/concert", "Concert", null),
                }));
            ctx.ipc.Emit(StageLightingTopics.LightsList,
                new LightListDto(new List<LightListItemDto>
                {
                    new LightListItemDto("existing", "Existing", LightTypeDto.Point),
                }));
            ctx.vm.SetVolumeOverrideEnabled("UnityEngine.Rendering.Universal.Bloom", true);

            // Build preset.
            ctx.vm.CreatePreset("P1");
            ctx.vm.Presets[0].StageAddressableKey = "stages/concert";
            ctx.vm.Presets[0].Lights.Add(new LightConfigDto
            {
                DisplayName = "Sun",
                Type = LightTypeDto.Directional,
                Color = new ColorDto(1, 1, 1, 1),
                Intensity = 1.5f,
                Range = 0f,
                SpotAngle = 30f,
            });
            ctx.vm.Presets[0].VolumeOverrides.Add(new VolumeOverrideConfigDto
            {
                TypeFullName = "UnityEngine.Rendering.Universal.Tonemapping",
                Enabled = true,
                Params = new Dictionary<string, VolumeOverrideParamValueDto>
                {
                    ["mode"] = new VolumeOverrideParamValueDto(ParamKind.Enum, null, null, null, null, null, "Neutral"),
                },
            });

            ctx.ipc.Sent.Clear();
            ctx.vm.ActivatePreset("P1");

            // Step 1: existing override disabled (Bloom enabled=false).
            int idxDisable = FindFirstSent(ctx, "volume/override/UnityEngine.Rendering.Universal.Bloom/enabled", false);
            Assert.That(idxDisable, Is.GreaterThanOrEqualTo(0));

            // Step 2: stage load.
            int idxStage = FindFirstSent(ctx, StageLightingTopics.StageCommand,
                payloadPredicate: p => p is StageCommandDto sc && sc.Op == "load");
            Assert.That(idxStage, Is.GreaterThan(idxDisable));

            // Step 3: existing light remove.
            int idxLightRemove = FindFirstSent(ctx, StageLightingTopics.LightCommand,
                payloadPredicate: p => p is LightCommandDto lc && lc.Op == "remove");
            Assert.That(idxLightRemove, Is.GreaterThan(idxStage));

            // Step 4: preset light add.
            int idxLightAdd = FindFirstSent(ctx, StageLightingTopics.LightCommand,
                payloadPredicate: p => p is LightCommandDto lc && lc.Op == "add");
            Assert.That(idxLightAdd, Is.GreaterThan(idxLightRemove));

            // Step 5/6: tonemapping enabled true + param state.
            int idxEnable = FindFirstSent(ctx, "volume/override/UnityEngine.Rendering.Universal.Tonemapping/enabled", true);
            Assert.That(idxEnable, Is.GreaterThan(idxLightAdd));
            int idxParam = FindFirstSent(ctx, "volume/override/UnityEngine.Rendering.Universal.Tonemapping/mode");
            Assert.That(idxParam, Is.GreaterThan(idxEnable));
        }

        [Test]
        public void ActivatePreset_StageNotInCatalog_RaisesUnresolvedWarning()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            // Empty catalog.
            ctx.vm.CreatePreset("P");
            ctx.vm.Presets[0].StageAddressableKey = "stages/missing";

            string? warn = null;
            ctx.vm.OnOperationWarning += w => warn = w;
            ctx.vm.ActivatePreset("P");

            Assert.That(warn, Is.EqualTo(VTuberSystemBase.StageLightingVolumeTab.ViewModel.StageLightingVolumeTabViewModel.WarnStageUnresolved));
        }

        // -------- helpers --------
        private static int FindFirstSent(
            (StageLightingVolumeTab.ViewModel.StageLightingVolumeTabViewModel vm,
             TestDoubles.FakeIpcClient ipc,
             TestDoubles.FakePresetStorage storage,
             TestDoubles.FakeClock clock,
             TestDoubles.FakeConnectionStatus conn,
             TestDoubles.FakeDiagnosticsLogger logger,
             Services.LightListState lightList,
             Services.StageCatalogState stageCatalog,
             Services.VolumeSchemaCache volumeCache,
             Services.DebounceFlusher debounce,
             Diagnostics.StageTabDiagnostics diagnostics) ctx,
            string topic,
            object? payloadValue = null,
            System.Func<object?, bool>? payloadPredicate = null)
        {
            for (int i = 0; i < ctx.ipc.Sent.Count; i++)
            {
                if (!string.Equals(ctx.ipc.Sent[i].Topic, topic, System.StringComparison.Ordinal)) continue;
                if (payloadValue is not null && !object.Equals(ctx.ipc.Sent[i].Payload, payloadValue)) continue;
                if (payloadPredicate is not null && !payloadPredicate(ctx.ipc.Sent[i].Payload)) continue;
                return i;
            }
            return -1;
        }
    }
}
