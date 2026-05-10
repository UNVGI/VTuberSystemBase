#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Renders the camera lineup as a vertical list of cards. Each card carries
    /// the camera id, display name, type, plus Activate / Edit / Delete
    /// buttons. The 0-camera state shows a CTA button (Requirement 7.9).
    /// </summary>
    public sealed class CameraListView
    {
        private readonly ICameraSwitcherCoordinator _coordinator;
        private readonly VisualElement _container;
        private readonly VisualTreeAsset? _cardTemplate;

        public Action? OnAddCameraClicked;

        public CameraListView(ICameraSwitcherCoordinator coordinator, VisualElement container, VisualTreeAsset? cardTemplate = null)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _cardTemplate = cardTemplate;
        }

        public void Render()
        {
            _container.Clear();
            var cameras = _coordinator.Cameras;
            if (cameras.Count == 0)
            {
                var empty = new VisualElement();
                empty.AddToClassList("vsb-empty-state");
                empty.Add(new Label("No cameras yet"));
                var add = new Button(() => OnAddCameraClicked?.Invoke()) { text = "Add Camera" };
                empty.Add(add);
                _container.Add(empty);
                return;
            }

            var addBtn = new Button(() => OnAddCameraClicked?.Invoke()) { text = "+ Add Camera" };
            _container.Add(addBtn);

            var editing = _coordinator.EditingCameraId;
            var active = _coordinator.ActiveCameraId;
            for (var i = 0; i < cameras.Count; i++)
            {
                var meta = cameras[i];
                VisualElement card;
                if (_cardTemplate != null)
                {
                    card = _cardTemplate.CloneTree();
                }
                else
                {
                    card = new VisualElement();
                    card.AddToClassList("vsb-camera-card");
                    card.Add(new Label() { name = "vsb-camera-card__index" });
                    card.Add(new Label() { name = "vsb-camera-card__name" });
                    card.Add(new Label() { name = "vsb-camera-card__type" });
                    card.Add(new Button() { name = "vsb-camera-card__activate", text = "Activate" });
                    card.Add(new Button() { name = "vsb-camera-card__edit", text = "Edit" });
                    card.Add(new Button() { name = "vsb-camera-card__delete", text = "Delete" });
                }

                var nameLabel = card.Q<Label>("vsb-camera-card__name");
                if (nameLabel is not null) nameLabel.text = meta.DisplayName;
                var typeLabel = card.Q<Label>("vsb-camera-card__type");
                if (typeLabel is not null) typeLabel.text = meta.Type.ToString();
                var idxLabel = card.Q<Label>("vsb-camera-card__index");
                if (idxLabel is not null) idxLabel.text = (i + 1).ToString();

                var activateBtn = card.Q<Button>("vsb-camera-card__activate");
                if (activateBtn is not null) activateBtn.clicked += () => _coordinator.ActivateCamera(meta.Id);
                var editBtn = card.Q<Button>("vsb-camera-card__edit");
                if (editBtn is not null) editBtn.clicked += () => _coordinator.SelectEditTarget(meta.Id);
                var deleteBtn = card.Q<Button>("vsb-camera-card__delete");
                if (deleteBtn is not null) deleteBtn.clicked += () => _coordinator.RequestDeleteCamera(meta.Id);

                if (active.HasValue && string.Equals(active.Value, meta.Id.Value, StringComparison.Ordinal))
                    card.AddToClassList("vsb-camera-card--active");
                if (editing.HasValue && string.Equals(editing.Value, meta.Id.Value, StringComparison.Ordinal))
                    card.AddToClassList("vsb-camera-card--editing");

                _container.Add(card);
            }
        }
    }
}
