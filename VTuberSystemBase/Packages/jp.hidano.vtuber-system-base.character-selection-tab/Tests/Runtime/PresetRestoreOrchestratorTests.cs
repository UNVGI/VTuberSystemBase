#nullable enable
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Ipc;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

using AvatarCatalogEntry = VTuberSystemBase.CharacterSelectionTab.Contracts.AvatarCatalogEntry;
namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    [TestFixture]
    public sealed class PresetRestoreOrchestratorTests
    {
        [Test]
        public async Task Connected_TriggersReplay()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            var conn = new FakeConnectionStatus();
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var presets = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            store.ApplySlotCatalog(new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "s1" } },
            });
            store.ApplyAvatarCatalog(new AvatarCatalogPayload
            {
                Avatars = new[] { new AvatarCatalogEntry { AvatarKey = "avatars/alice", DisplayName = "A" } },
            });
            var c = await presets.CreateAsync("X");
            await presets.SetActiveAsync(c.PresetId!);
            presets.MarkSlotAssignmentChanged("s1", "avatars/alice");
            await presets.FlushPendingAsync();

            using var orch = new PresetRestoreOrchestrator(presets, binder, store, conn);
            conn.SetStatus(ConnectionStatusCode.Connected);

            // Allow the fire-and-forget Task to settle.
            for (int i = 0; i < 50 && cmd.Sent.Count == 0; i++) await Task.Delay(10);
            Assert.GreaterOrEqual(cmd.Sent.Count, 1);
            Assert.AreEqual(CharacterTopics.SlotAssignment("s1"), cmd.Sent[0].Topic);
        }

        [Test]
        public async Task UnresolvedAvatar_PublishesEmptyAndReportsProgress()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            var conn = new FakeConnectionStatus();
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var presets = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            store.ApplySlotCatalog(new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "s1" } },
            });
            // Catalog has only "alice"; preset references a missing key.
            store.ApplyAvatarCatalog(new AvatarCatalogPayload
            {
                Avatars = new[] { new AvatarCatalogEntry { AvatarKey = "avatars/alice", DisplayName = "A" } },
            });
            var c = await presets.CreateAsync("X");
            await presets.SetActiveAsync(c.PresetId!);
            presets.MarkSlotAssignmentChanged("s1", "avatars/missing");
            await presets.FlushPendingAsync();

            using var orch = new PresetRestoreOrchestrator(presets, binder, store, conn);
            RestoreProgressEvent? capturedProgress = null;
            orch.OnProgress += e => capturedProgress = e;

            await orch.ReplayActivePresetAsync(CancellationToken.None);

            Assert.IsNotNull(capturedProgress);
            Assert.AreEqual(1, capturedProgress!.UnresolvedAvatarKeys.Count);
            // First (and only) send should be empty assignment for s1.
            Assert.GreaterOrEqual(cmd.Sent.Count, 1);
            var first = cmd.Sent[0];
            var payload = first.Payload as SlotAssignmentPayload;
            Assert.IsNotNull(payload);
            Assert.IsNull(payload!.AvatarKey);
        }
    }
}
