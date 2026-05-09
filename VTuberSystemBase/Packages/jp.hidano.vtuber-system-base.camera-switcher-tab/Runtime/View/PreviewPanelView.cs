#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Renders the multi-preview row + the large active-camera preview.
    /// Each preview card uses
    /// <c>VisualElement.style.backgroundImage = Background.FromRenderTexture(rt)</c>
    /// when a handle is resolved; missing handles show a placeholder colour.
    /// </summary>
    public sealed class PreviewPanelView
    {
        private readonly ICameraSwitcherCoordinator _coordinator;
        private readonly PreviewSubscriptionController _preview;
        private readonly VisualElement _activeContainer;
        private readonly VisualElement _multiContainer;

        public PreviewPanelView(
            ICameraSwitcherCoordinator coordinator,
            PreviewSubscriptionController preview,
            VisualElement activeContainer,
            VisualElement multiContainer)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _preview = preview ?? throw new ArgumentNullException(nameof(preview));
            _activeContainer = activeContainer ?? throw new ArgumentNullException(nameof(activeContainer));
            _multiContainer = multiContainer ?? throw new ArgumentNullException(nameof(multiContainer));
        }

        public void Render()
        {
            _activeContainer.Clear();
            _multiContainer.Clear();

            var active = _coordinator.ActiveCameraId;
            if (active.HasValue && _preview.Slots.TryGetValue(active.Value, out var slot))
            {
                _activeContainer.Add(BuildPreviewCard(slot));
            }
            else
            {
                _activeContainer.Add(new Label("(no active preview)"));
            }

            foreach (var s in _preview.Slots.Values)
            {
                var card = BuildPreviewCard(s);
                card.style.width = 192;
                card.style.height = 108;
                _multiContainer.Add(card);
            }
        }

        private static VisualElement BuildPreviewCard(PreviewSubscriptionController.PreviewSlot slot)
        {
            var card = new VisualElement();
            card.style.flexGrow = 1;
            card.style.backgroundColor = new StyleColor(new Color(0.05f, 0.05f, 0.05f));
            if (slot.Handle is RenderTexture rt)
            {
                card.style.backgroundImage = Background.FromRenderTexture(rt);
            }
            else
            {
                card.Add(new Label(slot.ResolveFailed ? "(preview unavailable)" : "(loading)"));
            }
            return card;
        }
    }
}
