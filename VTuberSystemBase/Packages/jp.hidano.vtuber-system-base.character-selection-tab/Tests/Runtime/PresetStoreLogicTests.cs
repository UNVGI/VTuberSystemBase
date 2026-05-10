#nullable enable
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    [TestFixture]
    public sealed class PresetStoreLogicTests
    {
        [Test]
        public async Task Create_ReturnsOkAndPersists()
        {
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));

            var result = await logic.CreateAsync("Morning");
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.PresetId);
            Assert.AreEqual(1, storage.AllRecords.Count);
        }

        [Test]
        public async Task Create_RejectsDuplicateName()
        {
            var logic = new PresetStoreLogic(new InMemoryPresetStorage(), new ManualClock(), TimeSpan.FromMilliseconds(500));
            await logic.CreateAsync("X");
            var dup = await logic.CreateAsync("X");
            Assert.IsFalse(dup.Success);
            Assert.AreEqual(PresetOperationErrorCode.DuplicateName, dup.Error);
        }

        [Test]
        public async Task Delete_RejectsActive()
        {
            var logic = new PresetStoreLogic(new InMemoryPresetStorage(), new ManualClock(), TimeSpan.FromMilliseconds(500));
            var c = await logic.CreateAsync("X");
            await logic.SetActiveAsync(c.PresetId!);
            var d = await logic.DeleteAsync(c.PresetId!);
            Assert.IsFalse(d.Success);
            Assert.AreEqual(PresetOperationErrorCode.CannotDeleteActive, d.Error);
        }

        [Test]
        public async Task Debounce_GroupsMultipleChanges()
        {
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));

            var c = await logic.CreateAsync("X");
            await logic.SetActiveAsync(c.PresetId!);
            // Initial CreateAsync caused 1 save call (preset itself); reset counter
            // by capturing the baseline.
            var baseline = storage.SaveCallCount;

            logic.MarkSlotAssignmentChanged("slot-01", "avatars/alice");
            logic.MarkSettingValueChanged("slot-01", "smile", SettingValue.Float(0.5f));

            // 499ms — debounce not yet expired.
            clock.Advance(TimeSpan.FromMilliseconds(499));
            Assert.AreEqual(baseline, storage.SaveCallCount, "should not save yet");

            // Cross threshold; OnTick triggers async flush.
            clock.Advance(TimeSpan.FromMilliseconds(2));
            // Wait briefly for the fire-and-forget Task to settle.
            for (int i = 0; i < 50 && storage.SaveCallCount == baseline; i++)
            {
                await Task.Delay(20);
            }
            Assert.AreEqual(baseline + 1, storage.SaveCallCount);
        }

        [Test]
        public async Task FlushPending_RetriesOnFailure()
        {
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            var c = await logic.CreateAsync("X");
            await logic.SetActiveAsync(c.PresetId!);
            var baseline = storage.SaveCallCount;

            storage.ThrowOnSave = true;
            logic.MarkSlotAssignmentChanged("s1", "k");
            await logic.FlushPendingAsync();
            // Save attempted, failed; dirty re-marked.
            // Flip back to success and try again.
            storage.ThrowOnSave = false;
            await logic.FlushPendingAsync();
            Assert.GreaterOrEqual(storage.SaveCallCount, baseline + 2);
        }
    }
}
