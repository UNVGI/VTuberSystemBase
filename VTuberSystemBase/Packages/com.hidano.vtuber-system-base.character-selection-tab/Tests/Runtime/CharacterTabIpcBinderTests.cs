#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Ipc;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    [TestFixture]
    public sealed class CharacterTabIpcBinderTests
    {
        [Test]
        public void Subscribe_RoutesSlotsCatalog_ToStore()
        {
            var store = new CharacterTabStateStore();
            var sub = new FakeUiSubscriptionClient();
            var cmd = new FakeUiCommandClient();
            using var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();

            sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "s1" } },
            });

            Assert.IsNotNull(store.GetSlot("s1"));
        }

        [Test]
        public void DynamicSubscriptions_AddedOnNewSlot()
        {
            var store = new CharacterTabStateStore();
            var sub = new FakeUiSubscriptionClient();
            var cmd = new FakeUiCommandClient();
            using var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            // 2 static + 0 dynamic
            int before = sub.Subscriptions.Count;
            sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "s1" } },
            });
            int after = sub.Subscriptions.Count;
            Assert.Greater(after, before);

            // Now drive a per-slot status into the dynamically created sub.
            sub.Emit(CharacterTopics.SlotStatus("s1"), new SlotStatusPayload { Status = "Assigned" });
            Assert.AreEqual(SlotStatus.Assigned, store.GetSlot("s1")!.Status);
        }

        [Test]
        public void PublishAssignment_RecordsTopic()
        {
            var store = new CharacterTabStateStore();
            var sub = new FakeUiSubscriptionClient();
            var cmd = new FakeUiCommandClient();
            using var binder = new CharacterTabIpcBinder(cmd, sub, store);
            store.ApplySlotCatalog(new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "s1" } },
            });
            binder.PublishAssignment("s1", "avatars/alice");
            Assert.AreEqual(1, cmd.Sent.Count);
            Assert.AreEqual(CharacterTopics.SlotAssignment("s1"), cmd.Sent[0].Topic);
            Assert.AreEqual(MessageKind.State, cmd.Sent[0].Kind);
        }

        [Test]
        public void PublishSlotCommand_FiresEvent()
        {
            var store = new CharacterTabStateStore();
            var sub = new FakeUiSubscriptionClient();
            var cmd = new FakeUiCommandClient();
            using var binder = new CharacterTabIpcBinder(cmd, sub, store);
            store.ApplySlotCatalog(new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "s1" } },
            });
            binder.PublishSlotCommand("s1", new SlotCommandPayload { Kind = "Reload" });
            Assert.AreEqual(MessageKind.Event, cmd.Sent[0].Kind);
            Assert.AreEqual(CharacterTopics.SlotCommand("s1"), cmd.Sent[0].Topic);
        }

        [Test]
        public void Dispose_DropsAllSubscriptions()
        {
            var store = new CharacterTabStateStore();
            var sub = new FakeUiSubscriptionClient();
            var cmd = new FakeUiCommandClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            sub.Emit(CharacterTopics.SlotsCatalog, new SlotCatalogPayload
            {
                Slots = new[] { new SlotCatalogEntry { SlotId = "s1" } },
            });
            binder.Dispose();
            int active = 0;
            foreach (var s in sub.Subscriptions) if (s.IsActive) active++;
            Assert.AreEqual(0, active);
        }
    }
}
