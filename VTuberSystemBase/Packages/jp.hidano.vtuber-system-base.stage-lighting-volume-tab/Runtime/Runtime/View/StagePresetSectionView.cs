#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Preset CRUD section. Renders the preset list, exposes Create / Rename / Duplicate
    /// / Delete / Activate buttons, and reflects the active preset name. The View itself
    /// is dumb: every action delegates to <see cref="StageLightingVolumeTabViewModel"/>.
    /// (Task 6.2, Requirements 7.2, 7.3.)
    /// </summary>
    public sealed class StagePresetSectionView : IDisposable
    {
        private const string ItemClass = "vsb-slv-preset-list-item";
        private const string ItemActiveModifier = "vsb-slv-preset-list-item--active";

        private readonly VisualElement _root;
        private readonly StageLightingVolumeTabViewModel _viewModel;

        private readonly VisualElement _list;
        private readonly Label _activeLabel;
        private readonly Func<string?> _nameProvider;
        private readonly Action<string>? _onValidationError;

        private string? _selectedPresetName;

        public StagePresetSectionView(
            VisualElement root,
            StageLightingVolumeTabViewModel viewModel,
            Func<string?>? nameProvider = null,
            Action<string>? onValidationError = null)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _nameProvider = nameProvider ?? (() => "untitled");
            _onValidationError = onValidationError;

            _list = root.Q<VisualElement>("preset-list") ?? throw MissingElement("preset-list");
            _activeLabel = root.Q<Label>("active-preset-label") ?? new Label();

            BindButton("preset-create", OnCreate);
            BindButton("preset-rename", OnRename);
            BindButton("preset-duplicate", OnDuplicate);
            BindButton("preset-delete", OnDelete);
            BindButton("preset-activate", OnActivate);

            _viewModel.OnStateChanged += Refresh;
            Refresh();
        }

        public string? SelectedPresetName => _selectedPresetName;

        public void SelectPreset(string? name)
        {
            _selectedPresetName = name;
            Refresh();
        }

        public void Dispose()
        {
            _viewModel.OnStateChanged -= Refresh;
        }

        private void BindButton(string name, Action handler)
        {
            var btn = _root.Q<Button>(name);
            if (btn is null) return;
            btn.clicked += handler;
        }

        private void Refresh()
        {
            _list.Clear();
            foreach (var preset in _viewModel.Presets)
            {
                var item = new Label(preset.Name);
                item.AddToClassList(ItemClass);
                if (string.Equals(preset.Name, _viewModel.ActivePresetName, StringComparison.Ordinal))
                    item.AddToClassList(ItemActiveModifier);
                if (string.Equals(preset.Name, _selectedPresetName, StringComparison.Ordinal))
                    item.AddToClassList("vsb-slv-preset-list-item--selected");
                var capturedName = preset.Name;
                item.RegisterCallback<ClickEvent>(_ => SelectPreset(capturedName));
                _list.Add(item);
            }
            _activeLabel.text = _viewModel.ActivePresetName ?? "(none)";
        }

        private void OnCreate()
        {
            var name = _nameProvider() ?? string.Empty;
            var result = _viewModel.CreatePreset(name);
            if (!result.Success && result.Error.HasValue)
                _onValidationError?.Invoke(result.Error.Value.ToString());
        }

        private void OnRename()
        {
            if (_selectedPresetName is null) return;
            var newName = _nameProvider() ?? string.Empty;
            _viewModel.RenamePreset(_selectedPresetName, newName);
        }

        private void OnDuplicate()
        {
            if (_selectedPresetName is null) return;
            var newName = _nameProvider() ?? string.Empty;
            _viewModel.DuplicatePreset(_selectedPresetName, newName);
        }

        private void OnDelete()
        {
            if (_selectedPresetName is null) return;
            _viewModel.DeletePreset(_selectedPresetName);
            _selectedPresetName = null;
        }

        private void OnActivate()
        {
            if (_selectedPresetName is null) return;
            _viewModel.ActivatePreset(_selectedPresetName);
        }

        private static InvalidOperationException MissingElement(string id) =>
            new InvalidOperationException($"Preset section missing element '{id}'.");
    }
}
