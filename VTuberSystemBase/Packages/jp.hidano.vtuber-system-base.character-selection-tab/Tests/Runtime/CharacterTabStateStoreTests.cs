#nullable enable
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 2.1 acceptance tests covering catalog application, assignment,
    /// status transitions, in-flight locks, setting buffering on interaction
    /// and main-thread enforcement.
    /// </summary>
    [TestFixture]
    public sealed class CharacterTabStateStoreTests
    {
        private static SlotCatalogPayload Catalog(params string[] ids)
        {
            var entries = new List<SlotCatalogEntry>();
            for (int i = 0; i < ids.Length; i++)
                entries.Add(new SlotCatalogEntry { SlotId = ids[i], OrderHint = i });
            return new SlotCatalogPayload { Slots = entries };
        }

        [Test]
        public void ApplySlotCatalog_AddsSlotsInIdOrderAndFiresScope()
        {
            var store = new CharacterTabStateStore();
            StateChangeScope last = StateChangeScope.None;
            store.OnChanged += s => last |= s;

            store.ApplySlotCatalog(Catalog("slot-02", "slot-01", "slot-03"));

            var list = store.ListSlots();
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual("slot-01", list[0].SlotId);
            Assert.AreEqual("slot-02", list[1].SlotId);
            Assert.AreEqual("slot-03", list[2].SlotId);
            Assert.IsTrue((last & StateChangeScope.SlotCatalog) != 0);
        }

        [Test]
        public void ApplyAvatarCatalog_DeduplicatesByAvatarKey()
        {
            var store = new CharacterTabStateStore();
            store.ApplyAvatarCatalog(new AvatarCatalogPayload
            {
                Avatars = new[]
                {
                    new AvatarCatalogEntry { AvatarKey = "a", DisplayName = "A" },
                    new AvatarCatalogEntry { AvatarKey = "a", DisplayName = "A-dup" },
                    new AvatarCatalogEntry { AvatarKey = "b", DisplayName = "B" },
                },
            });
            Assert.AreEqual(2, store.AvatarCatalog.Count);
        }

        [Test]
        public void ApplyAssignment_UnknownSlotIsIgnored()
        {
            var store = new CharacterTabStateStore();
            string? warnedSlot = null;
            store.OnDiagnosticWarning = (slot, _, __) => warnedSlot = slot;

            store.ApplyAssignment("ghost", "avatar/x");
            Assert.AreEqual("ghost", warnedSlot);
            Assert.IsEmpty(store.ListSlots());
        }

        [Test]
        public void Assignment_FiresOnlyAssignmentScope()
        {
            var store = new CharacterTabStateStore();
            store.ApplySlotCatalog(Catalog("s1"));
            StateChangeScope captured = StateChangeScope.None;
            store.OnChanged += s => captured |= s;
            store.ApplyAssignment("s1", "avatar/foo");
            Assert.IsTrue((captured & StateChangeScope.Assignment) != 0);
            Assert.IsFalse((captured & StateChangeScope.SlotStatus) != 0);
            Assert.AreEqual("avatar/foo", store.GetSlot("s1")!.AssignedAvatarKey);
        }

        [Test]
        public void TryBeginInFlight_RejectsDuplicate()
        {
            var store = new CharacterTabStateStore();
            store.ApplySlotCatalog(Catalog("s1"));
            Assert.IsTrue(store.TryBeginInFlight("s1", InFlightOperationKind.Assignment, out var t1));
            Assert.IsFalse(store.TryBeginInFlight("s1", InFlightOperationKind.Reload, out _));
            Assert.IsTrue(store.EndInFlight(t1, InFlightOutcome.CompletedOk));
            Assert.IsTrue(store.TryBeginInFlight("s1", InFlightOperationKind.Reload, out _));
        }

        [Test]
        public void EndInFlight_StaleTokenIsRejected()
        {
            var store = new CharacterTabStateStore();
            store.ApplySlotCatalog(Catalog("s1"));
            Assert.IsTrue(store.TryBeginInFlight("s1", InFlightOperationKind.Assignment, out var t1));
            store.EndInFlight(t1, InFlightOutcome.CompletedOk);
            // Same slot, fresh begin produces a new token id; stale token must not free it.
            Assert.IsTrue(store.TryBeginInFlight("s1", InFlightOperationKind.Assignment, out var t2));
            Assert.IsFalse(store.EndInFlight(t1, InFlightOutcome.CompletedOk));
            Assert.AreEqual(InFlightOperationKind.Assignment, store.GetSlot("s1")!.InFlight);
            Assert.IsTrue(store.EndInFlight(t2, InFlightOutcome.CompletedOk));
        }

        [Test]
        public void InteractingBuffer_DefersRemoteAndFlushesOnEnd()
        {
            var store = new CharacterTabStateStore();
            store.ApplySlotCatalog(Catalog("s1"));
            store.MarkInteracting("s1", "smile");
            store.ApplySettingValue("s1", "smile", SettingValue.Float(0.7f), isFromRemote: true);
            // The remote write was buffered, not applied yet.
            Assert.IsFalse(store.GetSlot("s1")!.SettingValues.ContainsKey("smile"));
            // Local edit (isFromRemote=false) flows through immediately.
            store.ApplySettingValue("s1", "smile", SettingValue.Float(0.3f), isFromRemote: false);
            Assert.AreEqual(0.3f, store.GetSlot("s1")!.SettingValues["smile"].FloatValue);
            // Ending interaction flushes the buffered remote value.
            store.FlushBufferedSetting("s1", "smile");
            Assert.AreEqual(0.7f, store.GetSlot("s1")!.SettingValues["smile"].FloatValue);
        }

        [Test]
        public void ApplyError_TransitionsToErrorAndFiresStatusScope()
        {
            var store = new CharacterTabStateStore();
            store.ApplySlotCatalog(Catalog("s1"));
            StateChangeScope captured = StateChangeScope.None;
            store.OnChanged += s => captured |= s;
            store.ApplyError("s1", new SlotErrorPayload { ErrorCode = "KeyNotFound" });
            Assert.AreEqual(SlotStatus.Error, store.GetSlot("s1")!.Status);
            Assert.IsTrue((captured & StateChangeScope.SlotError) != 0);
            Assert.IsTrue((captured & StateChangeScope.SlotStatus) != 0);
        }

        [Test]
        public void NonOwnerThreadWrite_Throws()
        {
            var store = new CharacterTabStateStore();
            Exception? caught = null;
            var th = new Thread(() =>
            {
                try { store.ApplySlotCatalog(Catalog("s1")); }
                catch (Exception ex) { caught = ex; }
            });
            th.Start();
            th.Join();
            Assert.IsInstanceOf<InvalidOperationException>(caught);
        }
    }
}
