#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Light list + add/remove section. Renders <see cref="StageLightingVolumeTabViewModel.Lights"/>
    /// in stable order, exposes "Add Light" + "Remove" controls, and selects a light on
    /// row click. (Task 6.3, Requirements 4.2, 4.6, 4.9, 4.10.)
    /// </summary>
    public sealed class LightListSectionView : IDisposable
    {
        private const string ItemClass = "vsb-slv-light-list-item";
        private const string SelectedModifier = "vsb-slv-light-list-item--selected";

        private readonly VisualElement _root;
        private readonly StageLightingVolumeTabViewModel _viewModel;
        private readonly Func<LightInitialDto> _initialFactory;
        private readonly VisualElement _list;
        private readonly Button? _addButton;

        public LightListSectionView(
            VisualElement root,
            StageLightingVolumeTabViewModel viewModel,
            Func<LightInitialDto>? initialFactory = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _initialFactory = initialFactory ?? DefaultInitial;
            _list = root.Q<VisualElement>("light-list") ?? throw new InvalidOperationException(
                "Light list section missing element 'light-list'.");

            _addButton = root.Q<Button>("light-add");
            if (_addButton is not null) _addButton.clicked += OnAddClicked;

            _viewModel.OnStateChanged += Refresh;
            _viewModel.OnOperationWarning += OnOperationWarning;
            Refresh();
        }

        public void Dispose()
        {
            _viewModel.OnStateChanged -= Refresh;
            _viewModel.OnOperationWarning -= OnOperationWarning;
        }

        private void OnAddClicked()
        {
            if (_addButton is not null) _addButton.SetEnabled(false);
            try
            {
                _viewModel.AddLight(_initialFactory());
            }
            finally
            {
                if (_addButton is not null) _addButton.SetEnabled(true);
            }
        }

        private void OnOperationWarning(string code)
        {
            // Preserve UI alive state; no-op here. Section views can subclass for richer UI.
        }

        private void Refresh()
        {
            _list.Clear();
            if (_viewModel.Lights.Count == 0)
            {
                var empty = new Label("No lights yet.");
                empty.AddToClassList("vsb-slv-light-list-item--placeholder");
                _list.Add(empty);
                return;
            }
            foreach (var item in _viewModel.Lights)
            {
                var row = new VisualElement();
                row.AddToClassList(ItemClass);
                if (string.Equals(item.LightId, _viewModel.SelectedLightId, StringComparison.Ordinal))
                    row.AddToClassList(SelectedModifier);

                var label = new Label($"{item.DisplayName} ({item.Type})");
                row.Add(label);

                var removeBtn = new Button(() => _viewModel.RemoveLight(item.LightId)) { text = "Remove" };
                row.Add(removeBtn);

                var capturedId = item.LightId;
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target is Button) return;
                    _viewModel.SelectLight(capturedId);
                });
                _list.Add(row);
            }
        }

        private static LightInitialDto DefaultInitial() => new LightInitialDto(
            LightTypeDto.Directional,
            new Vector3Dto(50f, -30f, 0f),
            new ColorDto(1f, 1f, 1f, 1f),
            1.0f, 10f, 30f, "Light");
    }
}
