#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.StageLightingVolumeTab.ViewModel;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Property editor for the currently-selected light. Builds a panel of standard
    /// IMGUI-light controls (intensity, range, spot angle, color, rotation) that forward
    /// changes to <see cref="StageLightingVolumeTabViewModel.UpdateLightProperty"/>.
    /// Type changes show / hide the Range and Spot Angle controls per Requirement 5.4.
    /// (Task 6.4, Requirements 5.1, 5.4, 5.6, 5.7, 5.8, 5.10, 5.11, 9.3.)
    /// </summary>
    public sealed class LightPropertyEditorView : IDisposable
    {
        private readonly VisualElement _container;
        private readonly StageLightingVolumeTabViewModel _viewModel;
        private readonly Label _emptyLabel;
        private readonly VisualElement _editor;

        private FloatField? _intensityField;
        private FloatField? _rangeField;
        private FloatField? _spotAngleField;
        private TextField? _displayNameField;
        private EnumField? _typeField;
        private VisualElement? _rangeRow;
        private VisualElement? _spotRow;
        private Label? _validationLabel;

        public LightPropertyEditorView(
            VisualElement container,
            StageLightingVolumeTabViewModel viewModel)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

            _emptyLabel = new Label("Select a light to edit its properties.");
            _editor = new VisualElement();
            BuildEditor();

            _container.Clear();
            _container.Add(_emptyLabel);
            _container.Add(_editor);

            _viewModel.OnStateChanged += Refresh;
            _viewModel.OnValidationError += OnValidationError;
            Refresh();
        }

        public void Dispose()
        {
            _viewModel.OnStateChanged -= Refresh;
            _viewModel.OnValidationError -= OnValidationError;
        }

        private void BuildEditor()
        {
            _displayNameField = new TextField("Display Name");
            _displayNameField.RegisterValueChangedCallback(evt =>
                Send(StageLightingTopics.PropertyDisplayName, evt.newValue));
            _editor.Add(_displayNameField);

            _typeField = new EnumField("Type", LightTypeDto.Directional);
            _typeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is LightTypeDto t)
                {
                    Send(StageLightingTopics.PropertyType, t);
                    UpdateTypeSensitiveControls(t);
                }
            });
            _editor.Add(_typeField);

            _intensityField = new FloatField("Intensity");
            _intensityField.RegisterCallback<PointerDownEvent>(_ => SetDragging(StageLightingTopics.PropertyIntensity, true));
            _intensityField.RegisterCallback<PointerUpEvent>(_ => SetDragging(StageLightingTopics.PropertyIntensity, false));
            _intensityField.RegisterValueChangedCallback(evt =>
                Send(StageLightingTopics.PropertyIntensity, evt.newValue));
            _editor.Add(_intensityField);

            _rangeRow = new VisualElement();
            _rangeField = new FloatField("Range");
            _rangeField.RegisterCallback<PointerDownEvent>(_ => SetDragging(StageLightingTopics.PropertyRange, true));
            _rangeField.RegisterCallback<PointerUpEvent>(_ => SetDragging(StageLightingTopics.PropertyRange, false));
            _rangeField.RegisterValueChangedCallback(evt =>
                Send(StageLightingTopics.PropertyRange, evt.newValue));
            _rangeRow.Add(_rangeField);
            _editor.Add(_rangeRow);

            _spotRow = new VisualElement();
            _spotAngleField = new FloatField("Spot Angle");
            _spotAngleField.RegisterCallback<PointerDownEvent>(_ => SetDragging(StageLightingTopics.PropertySpotAngle, true));
            _spotAngleField.RegisterCallback<PointerUpEvent>(_ => SetDragging(StageLightingTopics.PropertySpotAngle, false));
            _spotAngleField.RegisterValueChangedCallback(evt =>
                Send(StageLightingTopics.PropertySpotAngle, evt.newValue));
            _spotRow.Add(_spotAngleField);
            _editor.Add(_spotRow);

            _validationLabel = new Label();
            _validationLabel.AddToClassList("vsb-slv-validation-error");
            _editor.Add(_validationLabel);
        }

        private void Refresh()
        {
            var hasSelection = _viewModel.SelectedLightId is not null;
            _emptyLabel.style.display = hasSelection ? DisplayStyle.None : DisplayStyle.Flex;
            _editor.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnValidationError(string code)
        {
            if (_validationLabel is null) return;
            _validationLabel.text = code;
        }

        private void SetDragging(string property, bool isDragging)
        {
            if (_viewModel.SelectedLightId is null) return;
            _viewModel.SetLightPropertyDragging(_viewModel.SelectedLightId, property, isDragging);
        }

        private void Send(string property, object? value)
        {
            if (_viewModel.SelectedLightId is null) return;
            _viewModel.UpdateLightProperty(_viewModel.SelectedLightId, property, value);
        }

        private void UpdateTypeSensitiveControls(LightTypeDto type)
        {
            if (_rangeRow is not null)
                _rangeRow.style.display = type == LightTypeDto.Directional
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            if (_spotRow is not null)
                _spotRow.style.display = type == LightTypeDto.Spot
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        }
    }
}
