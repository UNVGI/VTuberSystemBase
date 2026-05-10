#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Presenters
{
    /// <summary>
    /// Renders the player-card list (one card per slot) and forwards
    /// click / settings / reset / reload UI events to the corresponding
    /// presenter callbacks. Cards are cloned once per slot and updated
    /// in-place on subsequent state changes (Req 2.3, task 5.1).
    /// </summary>
    public sealed class SlotListPresenter : IDisposable
    {
        public const string CardEmptyClass = "vsb-player-card--empty";
        public const string CardAssignedClass = "vsb-player-card--assigned";
        public const string CardErrorClass = "vsb-player-card--error";
        public const string CardInFlightClass = "vsb-player-card--in-flight";
        public const string CardSelectedClass = "vsb-player-card--selected";

        private readonly ICharacterTabStateStore _store;
        private readonly VisualElement _container;
        private readonly VisualTreeAsset? _cardTemplate;
        private readonly IDiagnosticsLogger? _log;
        private readonly Dictionary<string, VisualElement> _cards =
            new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private bool _disposed;

        public Action<string>? OnSlotSelected { get; set; }
        public Action<string>? OnSettingsRequested { get; set; }
        public Action<string>? OnResetRequested { get; set; }
        public Action<string>? OnReloadRequested { get; set; }

        public SlotListPresenter(
            ICharacterTabStateStore store,
            VisualElement container,
            VisualTreeAsset? cardTemplate,
            IDiagnosticsLogger? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _cardTemplate = cardTemplate;
            _log = logger;
            _store.OnChanged += OnStoreChanged;
            Render();
        }

        public IReadOnlyDictionary<string, VisualElement> CardsForTesting => _cards;

        public int CardCount => _cards.Count;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _store.OnChanged -= OnStoreChanged;
            _container.Clear();
            _cards.Clear();
        }

        private void OnStoreChanged(StateChangeScope scope)
        {
            const StateChangeScope mask = StateChangeScope.SlotCatalog
                | StateChangeScope.SlotStatus
                | StateChangeScope.Assignment
                | StateChangeScope.InFlight
                | StateChangeScope.SlotError
                | StateChangeScope.Connection;
            if ((scope & mask) == 0) return;
            Render();
        }

        public void Render()
        {
            if (_disposed) return;
            var slots = _store.ListSlots();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                seen.Add(slot.SlotId);
                if (!_cards.TryGetValue(slot.SlotId, out var card))
                {
                    card = CloneOrBuildCard(slot.SlotId);
                    _cards[slot.SlotId] = card;
                    _container.Add(card);
                    WireButtons(card, slot.SlotId);
                }
                UpdateCard(card, slot);
            }
            // Drop cards no longer in the catalog.
            var toRemove = new List<string>();
            foreach (var k in _cards.Keys)
            {
                if (!seen.Contains(k)) toRemove.Add(k);
            }
            foreach (var k in toRemove)
            {
                _container.Remove(_cards[k]);
                _cards.Remove(k);
            }
            // Slot ID 昇順で並べ替え (Store の SortedDictionary が昇順を保証している前提で
            // VisualElement の hierarchy 順序を Store 順序と揃えるための保険)
            for (int i = 0; i < slots.Count; i++)
            {
                var card = _cards[slots[i].SlotId];
                if (_container.IndexOf(card) != i)
                {
                    _container.Remove(card);
                    _container.Insert(i, card);
                }
            }
            // Apply selection visualization at the end.
            string? selected = _store.SelectedSlotId;
            foreach (var (slotId, card) in EnumerateCards())
            {
                if (string.Equals(selected, slotId, StringComparison.Ordinal))
                    card.AddToClassList(CardSelectedClass);
                else
                    card.RemoveFromClassList(CardSelectedClass);
            }
        }

        private VisualElement CloneOrBuildCard(string slotId)
        {
            if (_cardTemplate is not null)
            {
                var root = _cardTemplate.CloneTree();
                // CloneTree returns a wrapper; descend to the player-card root if present.
                var inner = root.Q<VisualElement>("vsb-player-card");
                return inner ?? root;
            }
            return BuildFallbackCard();
        }

        private VisualElement BuildFallbackCard()
        {
            var card = new VisualElement { name = "vsb-player-card" };
            card.AddToClassList("vsb-player-card");
            card.AddToClassList(CardEmptyClass);
            card.Add(new Label { name = "vsb-player-card__title" });
            card.Add(new Label { name = "vsb-player-card__avatar" });
            card.Add(new Label { name = "vsb-player-card__warning" });
            var buttons = new VisualElement { name = "vsb-player-card__buttons" };
            buttons.Add(new Button { name = "vsb-player-card__settings-btn", text = "Settings" });
            buttons.Add(new Button { name = "vsb-player-card__reset-btn", text = "Reset" });
            buttons.Add(new Button { name = "vsb-player-card__reload-btn", text = "Reload" });
            card.Add(buttons);
            return card;
        }

        private void WireButtons(VisualElement card, string slotId)
        {
            // Card click selects the slot.
            card.RegisterCallback<ClickEvent>(_ => OnSlotSelected?.Invoke(slotId));
            var settings = card.Q<Button>("vsb-player-card__settings-btn");
            if (settings is not null) settings.clicked += () => OnSettingsRequested?.Invoke(slotId);
            var reset = card.Q<Button>("vsb-player-card__reset-btn");
            if (reset is not null) reset.clicked += () => OnResetRequested?.Invoke(slotId);
            var reload = card.Q<Button>("vsb-player-card__reload-btn");
            if (reload is not null) reload.clicked += () => OnReloadRequested?.Invoke(slotId);
        }

        private void UpdateCard(VisualElement card, SlotSnapshot slot)
        {
            var title = card.Q<Label>("vsb-player-card__title");
            if (title is not null) title.text = slot.DisplayName ?? slot.SlotId;
            var avatar = card.Q<Label>("vsb-player-card__avatar");
            if (avatar is not null) avatar.text = slot.AssignedAvatarKey ?? "(empty)";
            var warning = card.Q<Label>("vsb-player-card__warning");
            if (warning is not null)
            {
                warning.text = slot.Status == SlotStatus.Error
                    ? (slot.StatusDetail ?? "error")
                    : string.Empty;
                warning.style.display = slot.Status == SlotStatus.Error
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            // Status modifier classes (mutually exclusive among empty/assigned/error).
            card.RemoveFromClassList(CardEmptyClass);
            card.RemoveFromClassList(CardAssignedClass);
            card.RemoveFromClassList(CardErrorClass);
            switch (slot.Status)
            {
                case SlotStatus.Error:
                    card.AddToClassList(CardErrorClass);
                    break;
                case SlotStatus.Assigned:
                case SlotStatus.Assigning:
                    card.AddToClassList(CardAssignedClass);
                    break;
                default:
                    card.AddToClassList(CardEmptyClass);
                    break;
            }

            // In-flight overlay.
            if (slot.InFlight is not null) card.AddToClassList(CardInFlightClass);
            else card.RemoveFromClassList(CardInFlightClass);

            // Disable action buttons in degraded state (connection lost / catalog
            // not received / error). Controls are kept visible so the user can
            // retry once the underlying condition clears.
            bool actionable = _store.ConnectionStatus == ConnectionStatusCode.Connected
                && slot.Status != SlotStatus.Error
                && slot.InFlight is null;
            SetButtonsEnabled(card, actionable);
        }

        private static void SetButtonsEnabled(VisualElement card, bool enabled)
        {
            var settings = card.Q<Button>("vsb-player-card__settings-btn");
            settings?.SetEnabled(enabled);
            var reset = card.Q<Button>("vsb-player-card__reset-btn");
            reset?.SetEnabled(enabled);
            var reload = card.Q<Button>("vsb-player-card__reload-btn");
            reload?.SetEnabled(enabled);
        }

        private IEnumerable<(string slotId, VisualElement card)> EnumerateCards()
        {
            foreach (var kv in _cards) yield return (kv.Key, kv.Value);
        }
    }
}
