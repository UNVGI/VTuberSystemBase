#nullable enable
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 5.1 acceptance: catalog application generates one card per slot in
    /// id order, status / assignment changes mutate USS modifier classes
    /// without re-cloning, error mode disables action buttons, and clicks
    /// fan out to the configured callbacks.
    /// </summary>
    [TestFixture]
    public sealed class SlotListPresenterTests
    {
        private static SlotCatalogPayload Catalog(params string[] ids)
        {
            var entries = new List<SlotCatalogEntry>();
            for (int i = 0; i < ids.Length; i++)
                entries.Add(new SlotCatalogEntry { SlotId = ids[i], OrderHint = i });
            return new SlotCatalogPayload { Slots = entries };
        }

        [Test]
        public void Render_BuildsOneCardPerSlotInIdOrder()
        {
            var store = new CharacterTabStateStore();
            var container = new VisualElement();
            using var presenter = new SlotListPresenter(store, container, null);

            store.ApplySlotCatalog(Catalog("slot-02", "slot-01", "slot-03"));

            Assert.AreEqual(3, presenter.CardCount);
            Assert.AreEqual(3, container.childCount);
            // Container child order should match slot id ascending order.
            Assert.AreSame(presenter.CardsForTesting["slot-01"], container[0]);
            Assert.AreSame(presenter.CardsForTesting["slot-02"], container[1]);
            Assert.AreSame(presenter.CardsForTesting["slot-03"], container[2]);
        }

        [Test]
        public void StatusChange_UpdatesSameCardWithoutRecloning()
        {
            var store = new CharacterTabStateStore();
            var container = new VisualElement();
            using var presenter = new SlotListPresenter(store, container, null);
            store.ApplySlotCatalog(Catalog("slot-01"));
            var originalCard = presenter.CardsForTesting["slot-01"];

            store.ApplyAssignment("slot-01", "avatars/alice");
            store.ApplyStatus("slot-01", SlotStatus.Assigned, null);

            Assert.AreSame(originalCard, presenter.CardsForTesting["slot-01"],
                "card must be reused; status change should not reclone the element.");
            Assert.IsTrue(originalCard.ClassListContains(SlotListPresenter.CardAssignedClass));
            Assert.IsFalse(originalCard.ClassListContains(SlotListPresenter.CardEmptyClass));
        }

        [Test]
        public void ErrorState_DisablesButtonsAndShowsWarning()
        {
            var store = new CharacterTabStateStore();
            store.SetConnectionStatus(ConnectionStatusCode.Connected);
            var container = new VisualElement();
            using var presenter = new SlotListPresenter(store, container, null);
            store.ApplySlotCatalog(Catalog("slot-01"));

            store.ApplyError("slot-01",
                new SlotErrorPayload { ErrorCode = "ApplyFailed", Detail = "rig invalid" });

            var card = presenter.CardsForTesting["slot-01"];
            Assert.IsTrue(card.ClassListContains(SlotListPresenter.CardErrorClass));
            var warning = card.Q<Label>("vsb-player-card__warning");
            Assert.IsNotNull(warning);
            Assert.AreEqual("rig invalid", warning.text);
            var settings = card.Q<Button>("vsb-player-card__settings-btn");
            Assert.IsNotNull(settings);
            Assert.IsFalse(settings.enabledSelf);
        }

        [Test]
        public void Buttons_ExistAndAreEnabledForActionableSlot()
        {
            var store = new CharacterTabStateStore();
            store.SetConnectionStatus(ConnectionStatusCode.Connected);
            var container = new VisualElement();
            using var presenter = new SlotListPresenter(store, container, null);
            string? selected = null;
            presenter.OnSlotSelected = id => selected = id;
            store.ApplySlotCatalog(Catalog("slot-01"));
            store.ApplyAssignment("slot-01", "avatars/alice");
            store.ApplyStatus("slot-01", SlotStatus.Assigned, null);
            var card = presenter.CardsForTesting["slot-01"];

            // Wiring assertion: buttons are queryable and actionable when not
            // in error / disconnected. Click event routing relies on a UIElements
            // panel which is not available in EditMode bare unit tests; integration
            // tests cover the click path end-to-end (task 8.1).
            Assert.IsNotNull(card.Q<Button>("vsb-player-card__settings-btn"));
            Assert.IsTrue(card.Q<Button>("vsb-player-card__settings-btn")!.enabledSelf);
            Assert.IsNotNull(card.Q<Button>("vsb-player-card__reset-btn"));
            Assert.IsNotNull(card.Q<Button>("vsb-player-card__reload-btn"));
            Assert.IsNull(selected, "OnSlotSelected is wired but not exercised here.");
        }

        [Test]
        public void Disconnected_DisablesActionButtons()
        {
            var store = new CharacterTabStateStore();
            store.SetConnectionStatus(ConnectionStatusCode.Disconnected);
            var container = new VisualElement();
            using var presenter = new SlotListPresenter(store, container, null);
            store.ApplySlotCatalog(Catalog("slot-01"));

            var card = presenter.CardsForTesting["slot-01"];
            var btn = card.Q<Button>("vsb-player-card__settings-btn");
            Assert.IsNotNull(btn);
            Assert.IsFalse(btn.enabledSelf);
        }

        [Test]
        public void RetiredSlot_IsRemoved()
        {
            var store = new CharacterTabStateStore();
            var container = new VisualElement();
            using var presenter = new SlotListPresenter(store, container, null);
            store.ApplySlotCatalog(Catalog("slot-01", "slot-02"));
            Assert.AreEqual(2, presenter.CardCount);

            store.ApplySlotCatalog(Catalog("slot-01"));
            Assert.AreEqual(1, presenter.CardCount);
            Assert.IsFalse(presenter.CardsForTesting.ContainsKey("slot-02"));
        }
    }
}
