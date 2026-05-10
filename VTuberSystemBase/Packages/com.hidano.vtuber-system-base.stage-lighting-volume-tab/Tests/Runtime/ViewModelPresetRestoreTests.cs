#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks preset-restore-on-startup behaviour (Task 5.7, Requirements 8.5, 8.6, 8.7,
    /// 8.8, 8.11, 9.1).
    /// </summary>
    [TestFixture]
    public sealed class ViewModelPresetRestoreTests
    {
        [Test]
        public async Task OnActivated_FirstRun_DoesNotRestore_AndPresetsEmpty()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.storage.SimulateMissingOnLoad = true;

            ctx.vm.OnActivated();
            await Task.Delay(25);

            Assert.That(ctx.vm.Presets, Is.Empty);
            Assert.That(ctx.vm.ActivePresetName, Is.Null);
        }

        [Test]
        public async Task OnActivated_RestoresPersistedPresetsAndActivePreset()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.ipc.RequestResponder = _ => new VolumeOverrideSchemaDto(1, new List<VolumeOverrideTypeDto>());

            ctx.storage.SimulateMissingOnLoad = false;
            ctx.storage.SaveAsync(new PresetFileRoot
            {
                SchemaVersion = 1,
                ActivePresetName = "Daylight",
                Presets = new List<PresetDto>
                {
                    new PresetDto { Name = "Daylight" },
                    new PresetDto { Name = "Night" },
                },
            }).Wait();

            ctx.vm.OnActivated();
            await Task.Delay(40);

            Assert.That(ctx.vm.Presets, Has.Count.EqualTo(2));
            Assert.That(ctx.vm.ActivePresetName, Is.EqualTo("Daylight"));
        }
    }
}
