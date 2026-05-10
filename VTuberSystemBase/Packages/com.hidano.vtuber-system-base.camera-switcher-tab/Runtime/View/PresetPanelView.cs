#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherTab.Domain;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Renders the preset row column with CRUD buttons per row. Duplicate-name
    /// validation surfaces as a red <c>--invalid</c> class on the rename input
    /// (Requirement 11.1d).
    /// </summary>
    public sealed class PresetPanelView
    {
        private readonly ICameraSwitcherCoordinator _coordinator;
        private readonly PresetController _presets;
        private readonly VisualElement _container;
        private readonly VisualTreeAsset? _rowTemplate;
        private TextField? _newNameInput;

        public PresetPanelView(
            ICameraSwitcherCoordinator coordinator,
            PresetController presets,
            VisualElement container,
            VisualTreeAsset? rowTemplate = null)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _presets = presets ?? throw new ArgumentNullException(nameof(presets));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _rowTemplate = rowTemplate;
        }

        public void Render()
        {
            _container.Clear();

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            _newNameInput = new TextField { value = "" };
            _newNameInput.style.flexGrow = 1;
            header.Add(_newNameInput);
            var createBtn = new Button(() =>
            {
                var name = _newNameInput.value;
                if (string.IsNullOrEmpty(name)) return;
                if (IsDuplicate(name))
                {
                    _newNameInput.AddToClassList("vsb-preset-row__name--invalid");
                    return;
                }
                _newNameInput.RemoveFromClassList("vsb-preset-row__name--invalid");
                _coordinator.CreatePreset(name);
                _newNameInput.value = "";
                Render();
            })
            { text = "Create" };
            header.Add(createBtn);
            _container.Add(header);

            foreach (var name in _presets.PresetNames)
            {
                VisualElement row;
                if (_rowTemplate != null)
                {
                    row = _rowTemplate.CloneTree();
                }
                else
                {
                    row = new VisualElement();
                    row.AddToClassList("vsb-preset-row");
                    row.Add(new Label() { name = "vsb-preset-row__name" });
                    row.Add(new Button() { name = "vsb-preset-row__activate", text = "Activate" });
                    row.Add(new Button() { name = "vsb-preset-row__rename", text = "Rename" });
                    row.Add(new Button() { name = "vsb-preset-row__duplicate", text = "Duplicate" });
                    row.Add(new Button() { name = "vsb-preset-row__delete", text = "Delete" });
                }
                var nameLabel = row.Q<Label>("vsb-preset-row__name");
                if (nameLabel is not null) nameLabel.text = name;

                var capturedName = name;

                var activateBtn = row.Q<Button>("vsb-preset-row__activate");
                if (activateBtn is not null) activateBtn.clicked += () => _coordinator.ActivatePreset(capturedName);
                var renameBtn = row.Q<Button>("vsb-preset-row__rename");
                if (renameBtn is not null) renameBtn.clicked += () =>
                {
                    var nn = _newNameInput?.value ?? string.Empty;
                    if (string.IsNullOrEmpty(nn) || IsDuplicate(nn)) return;
                    _coordinator.RenamePreset(capturedName, nn);
                    Render();
                };
                var dupBtn = row.Q<Button>("vsb-preset-row__duplicate");
                if (dupBtn is not null) dupBtn.clicked += () =>
                {
                    var nn = _newNameInput?.value ?? string.Empty;
                    if (string.IsNullOrEmpty(nn) || IsDuplicate(nn)) return;
                    _coordinator.DuplicatePreset(capturedName, nn);
                    Render();
                };
                var deleteBtn = row.Q<Button>("vsb-preset-row__delete");
                if (deleteBtn is not null) deleteBtn.clicked += () =>
                {
                    _coordinator.DeletePreset(capturedName);
                    Render();
                };

                if (string.Equals(_presets.ActivePresetName, capturedName, StringComparison.Ordinal))
                    row.AddToClassList("vsb-preset-row--active");

                _container.Add(row);
            }
        }

        private bool IsDuplicate(string name)
        {
            foreach (var existing in _presets.PresetNames)
                if (string.Equals(existing, name, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
