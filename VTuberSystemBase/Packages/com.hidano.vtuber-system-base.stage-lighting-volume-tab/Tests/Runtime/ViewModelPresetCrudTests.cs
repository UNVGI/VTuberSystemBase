#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks preset CRUD behaviour (Task 5.5, Requirements 7.1, 7.2, 7.3, 7.5, 7.9, 8.3).
    /// </summary>
    [TestFixture]
    public sealed class ViewModelPresetCrudTests
    {
        [Test]
        public void CreatePreset_AddsToCollection_AndSchedulesFlush()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();

            var result = ctx.vm.CreatePreset("Daylight");

            Assert.That(result.Success, Is.True);
            Assert.That(ctx.vm.Presets, Has.Count.EqualTo(1));
            Assert.That(ctx.vm.Presets[0].Name, Is.EqualTo("Daylight"));
        }

        [Test]
        public async Task CreatePreset_FlushesAfterDebounce()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();

            ctx.vm.CreatePreset("Daylight");
            // Advance past debounce window.
            ctx.clock.Advance(TimeSpan.FromMilliseconds(600));
            await Task.Delay(20);

            Assert.That(ctx.storage.SaveCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void CreatePreset_DuplicateName_Rejected()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.CreatePreset("Daylight");

            var result = ctx.vm.CreatePreset("Daylight");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(PresetOpError.DuplicateName));
        }

        [Test]
        public void CreatePreset_EmptyName_Rejected()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();

            var result = ctx.vm.CreatePreset("   ");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(PresetOpError.EmptyName));
        }

        [Test]
        public void RenamePreset_DuplicateNewName_Rejected()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.CreatePreset("A");
            ctx.vm.CreatePreset("B");

            var result = ctx.vm.RenamePreset("A", "B");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(PresetOpError.DuplicateName));
        }

        [Test]
        public void RenamePreset_UpdatesActivePresetNameWhenMatching()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.CreatePreset("Original");
            ctx.vm.ActivatePreset("Original");

            ctx.vm.RenamePreset("Original", "Renamed");

            Assert.That(ctx.vm.ActivePresetName, Is.EqualTo("Renamed"));
        }

        [Test]
        public void DuplicatePreset_CreatesIndependentClone()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.CreatePreset("A");

            var result = ctx.vm.DuplicatePreset("A", "A-copy");

            Assert.That(result.Success, Is.True);
            Assert.That(ctx.vm.Presets, Has.Count.EqualTo(2));
        }

        [Test]
        public void DeletePreset_OfActivePreset_ResetsActiveToNull()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();
            ctx.vm.CreatePreset("A");
            ctx.vm.ActivatePreset("A");

            ctx.vm.DeletePreset("A");

            Assert.That(ctx.vm.Presets, Is.Empty);
            Assert.That(ctx.vm.ActivePresetName, Is.Null);
        }

        [Test]
        public void DeletePreset_NotFound_ReturnsError()
        {
            var ctx = ViewModelTestFactory.Build();
            ctx.vm.OnActivated();

            var result = ctx.vm.DeletePreset("missing");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(PresetOpError.NotFound));
        }
    }
}
