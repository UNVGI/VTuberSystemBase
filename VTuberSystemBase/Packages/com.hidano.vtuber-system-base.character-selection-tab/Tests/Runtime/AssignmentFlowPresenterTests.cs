#nullable enable
using NUnit.Framework;
using System;
using System.Collections.Generic;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Ipc;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

using AvatarCatalogEntry = VTuberSystemBase.CharacterSelectionTab.Contracts.AvatarCatalogEntry;
namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 5.3 acceptance: assignment fans out via the IpcBinder, status replies
    /// release the InFlight lock, timeout produces TimedOut + failure callback,
    /// and an unknown avatar key is suppressed before any send happens.
    /// </summary>
    [TestFixture]
    public sealed class AssignmentFlowPresenterTests
    {
        private static SlotCatalogPayload Catalog(params string[] ids)
        {
            var entries = new List<SlotCatalogEntry>();
            for (int i = 0; i < ids.Length; i++) entries.Add(new SlotCatalogEntry { SlotId = ids[i], OrderHint = i });
            return new SlotCatalogPayload { Slots = entries };
        }

        private static AvatarCatalogPayload AvatarCatalog(params string[] keys)
        {
            var list = new List<AvatarCatalogEntry>();
            foreach (var k in keys) list.Add(new AvatarCatalogEntry { AvatarKey = k, DisplayName = k });
            return new AvatarCatalogPayload { Avatars = list };
        }

        [Test]
        public void RequestAssignment_PublishesStateAndStartsInFlight()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            var clock = new ManualClock();
            var logger = new FakeDiagnosticsLogger();
            using var presenter = new AssignmentFlowPresenter(
                store, binder, clock, TimeSpan.FromSeconds(5), logger);
            store.ApplySlotCatalog(Catalog("slot-01"));
            store.ApplyAvatarCatalog(AvatarCatalog("avatars/alice"));
            presenter.SelectSlot("slot-01");

            var result = presenter.RequestAssignment("avatars/alice");

            Assert.IsTrue(result.Success);
            // 1 send to slot/slot-01/assignment
            Assert.AreEqual(1, cmd.Sent.FindAll(s => s.Topic == CharacterTopics.SlotAssignment("slot-01")).Count);
            // InFlight set on the slot.
            Assert.AreEqual(InFlightOperationKind.Assignment, store.GetSlot("slot-01")!.InFlight);
        }

        [Test]
        public void StatusAssigned_ReleasesInFlight()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            var clock = new ManualClock();
            using var presenter = new AssignmentFlowPresenter(
                store, binder, clock, TimeSpan.FromSeconds(5));
            store.ApplySlotCatalog(Catalog("slot-01"));
            store.ApplyAvatarCatalog(AvatarCatalog("avatars/alice"));
            presenter.SelectSlot("slot-01");
            presenter.RequestAssignment("avatars/alice");

            // Simulate status state event from main output side.
            sub.Emit(CharacterTopics.SlotStatus("slot-01"),
                new SlotStatusPayload { Status = "Assigned" });

            Assert.IsNull(store.GetSlot("slot-01")!.InFlight);
        }

        [Test]
        public void Timeout_FlipsToTimedOutAndInvokesFailureCallback()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            var clock = new ManualClock();
            using var presenter = new AssignmentFlowPresenter(
                store, binder, clock, TimeSpan.FromSeconds(5));
            string? failedSlot = null;
            InFlightOutcome? outcome = null;
            presenter.OnAssignmentFailed = (s, o, _) => { failedSlot = s; outcome = o; };
            store.ApplySlotCatalog(Catalog("slot-01"));
            store.ApplyAvatarCatalog(AvatarCatalog("avatars/alice"));
            presenter.SelectSlot("slot-01");
            presenter.RequestAssignment("avatars/alice");

            clock.Advance(TimeSpan.FromSeconds(5) + TimeSpan.FromMilliseconds(1));

            Assert.AreEqual("slot-01", failedSlot);
            Assert.AreEqual(InFlightOutcome.TimedOut, outcome);
            Assert.IsNull(store.GetSlot("slot-01")!.InFlight);
        }

        [Test]
        public void ErrorState_FailsInFlightAndCallsFailureCallback()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            var clock = new ManualClock();
            using var presenter = new AssignmentFlowPresenter(
                store, binder, clock, TimeSpan.FromSeconds(5));
            InFlightOutcome? outcome = null;
            presenter.OnAssignmentFailed = (_, o, _) => outcome = o;
            store.ApplySlotCatalog(Catalog("slot-01"));
            store.ApplyAvatarCatalog(AvatarCatalog("avatars/alice"));
            presenter.SelectSlot("slot-01");
            presenter.RequestAssignment("avatars/alice");

            sub.Emit(CharacterTopics.SlotError("slot-01"),
                new SlotErrorPayload { ErrorCode = "ApplyFailed", Detail = "rig" },
                UiToolkitShell.Commands.MessageKind.Event);

            Assert.AreEqual(InFlightOutcome.Failed, outcome);
            Assert.IsNull(store.GetSlot("slot-01")!.InFlight);
            Assert.AreEqual(SlotStatus.Error, store.GetSlot("slot-01")!.Status);
        }

        [Test]
        public void UnknownAvatarKey_DoesNotPublish()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            var clock = new ManualClock();
            using var presenter = new AssignmentFlowPresenter(
                store, binder, clock, TimeSpan.FromSeconds(5));
            store.ApplySlotCatalog(Catalog("slot-01"));
            store.ApplyAvatarCatalog(AvatarCatalog("avatars/alice"));
            presenter.SelectSlot("slot-01");

            var result = presenter.RequestAssignment("avatars/unknown");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, cmd.Sent.FindAll(s => s.Topic.Contains("/assignment")).Count);
        }

        [Test]
        public void DuplicateAssignmentForSameSlot_IsSuppressed()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            var clock = new ManualClock();
            using var presenter = new AssignmentFlowPresenter(
                store, binder, clock, TimeSpan.FromSeconds(5));
            store.ApplySlotCatalog(Catalog("slot-01"));
            store.ApplyAvatarCatalog(AvatarCatalog("avatars/alice", "avatars/bob"));
            presenter.SelectSlot("slot-01");

            var first = presenter.RequestAssignment("avatars/alice");
            var second = presenter.RequestAssignment("avatars/bob");

            Assert.IsTrue(first.Success);
            Assert.IsFalse(second.Success);
            Assert.AreEqual(1, cmd.Sent.FindAll(s => s.Topic == CharacterTopics.SlotAssignment("slot-01")).Count);
        }

        [Test]
        public void RequestOperation_PublishesEventAndReleasesOnStatus()
        {
            var store = new CharacterTabStateStore();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var binder = new CharacterTabIpcBinder(cmd, sub, store);
            binder.SubscribeAll();
            var clock = new ManualClock();
            using var presenter = new AssignmentFlowPresenter(
                store, binder, clock, TimeSpan.FromSeconds(5));
            store.ApplySlotCatalog(Catalog("slot-01"));

            var r = presenter.RequestOperation("slot-01", AssignmentOperation.Reset);

            Assert.IsTrue(r.Success);
            Assert.AreEqual(1, cmd.Sent.FindAll(s => s.Topic == CharacterTopics.SlotCommand("slot-01")).Count);
        }
    }
}
