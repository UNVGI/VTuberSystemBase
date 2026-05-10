#nullable enable
using System;
using System.Text.Json;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Builds Local Volume override controls dynamically from the cached
    /// <see cref="VolumeMetadataResponse"/>. Uses plain UIElements controls
    /// (Slider / Toggle / TextField) for forward-compatibility — the View
    /// can be upgraded to <c>VsbSlider</c> / <c>VsbColorPicker</c> later
    /// without changing the data flow.
    /// </summary>
    public sealed class LocalVolumeEditorView
    {
        private readonly ICameraSwitcherCoordinator _coordinator;
        private readonly VolumeUiStateManager _volumeUi;
        private readonly VisualElement _container;

        public LocalVolumeEditorView(
            ICameraSwitcherCoordinator coordinator,
            VolumeUiStateManager volumeUi,
            VisualElement container)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _volumeUi = volumeUi ?? throw new ArgumentNullException(nameof(volumeUi));
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public void Render()
        {
            _container.Clear();
            var editing = _coordinator.EditingCameraId;
            if (!editing.HasValue)
            {
                _container.Add(new Label("Select a camera to edit"));
                return;
            }

            if (!_volumeUi.TryGet(editing, out var state) || state.Schema is null)
            {
                _container.Add(new Label(state?.SchemaFailed == true
                    ? $"Volume schema unavailable: {state.SchemaFailureDetail}"
                    : "Loading volume schema..."));
                return;
            }

            var schema = state.Schema.Value;

            var enabledToggle = new Toggle("Local Volume Enabled") { value = state.VolumeEnabled };
            enabledToggle.RegisterValueChangedCallback(ev =>
                _coordinator.SetVolumeEnabled(editing, ev.newValue));
            _container.Add(enabledToggle);

            foreach (var ov in schema.Overrides)
            {
                var item = new VisualElement();
                item.AddToClassList("vsb-volume-override-item");

                var header = new VisualElement();
                header.AddToClassList("vsb-volume-override-item__header");
                var ovEnabled = state.OverrideEnabled.TryGetValue(ov.Type, out var ovOn) && ovOn;
                var ovToggle = new Toggle { value = ovEnabled };
                var capturedType = ov.Type;
                ovToggle.RegisterValueChangedCallback(ev =>
                    _coordinator.SetVolumeOverrideEnabled(editing, capturedType, ev.newValue));
                header.Add(ovToggle);
                header.Add(new Label(ov.DisplayName) { name = "vsb-volume-override-item__title" });
                var removeBtn = new Button(() => _coordinator.RemoveVolumeOverride(editing, capturedType)) { text = "Remove" };
                header.Add(removeBtn);
                item.Add(header);

                foreach (var p in ov.Params)
                {
                    item.Add(BuildParamControl(editing, capturedType, p, state));
                }
                _container.Add(item);
            }
        }

        private VisualElement BuildParamControl(CameraId cameraId, string overrideType, VolumeParamSchema p, VolumeUiStateManager.CameraVolumeState state)
        {
            var label = new Label($"{p.DisplayName} ({p.TypeTag})");
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(label);

            var hasValue = state.ParamValues.TryGetValue((overrideType, p.Name), out var current);
            switch (p.TypeTag)
            {
                case "float":
                {
                    var slider = new Slider();
                    slider.lowValue = p.Min.HasValue ? p.Min.Value.GetSingle() : 0f;
                    slider.highValue = p.Max.HasValue ? p.Max.Value.GetSingle() : 1f;
                    slider.value = hasValue ? current.GetSingle() : (p.Default.ValueKind == JsonValueKind.Number ? p.Default.GetSingle() : 0f);
                    slider.RegisterCallback<MouseDownEvent>(_ => _volumeUi.BeginDrag(overrideType, p.Name));
                    slider.RegisterCallback<MouseUpEvent>(_ => _volumeUi.EndDrag(overrideType, p.Name));
                    slider.RegisterValueChangedCallback(ev =>
                        _coordinator.SetVolumeOverrideParam(cameraId, overrideType, p.Name,
                            JsonSerializer.SerializeToElement(ev.newValue)));
                    row.Add(slider);
                    break;
                }
                case "bool":
                {
                    var toggle = new Toggle();
                    toggle.value = hasValue ? current.GetBoolean() : (p.Default.ValueKind == JsonValueKind.True);
                    toggle.RegisterValueChangedCallback(ev =>
                        _coordinator.SetVolumeOverrideParam(cameraId, overrideType, p.Name,
                            JsonSerializer.SerializeToElement(ev.newValue)));
                    row.Add(toggle);
                    break;
                }
                default:
                {
                    var tf = new TextField { value = hasValue ? current.GetRawText() : p.Default.GetRawText() };
                    tf.RegisterValueChangedCallback(ev =>
                    {
                        try
                        {
                            var elem = JsonSerializer.Deserialize<JsonElement>(ev.newValue);
                            _coordinator.SetVolumeOverrideParam(cameraId, overrideType, p.Name, elem);
                        }
                        catch { /* ignore malformed input */ }
                    });
                    row.Add(tf);
                    break;
                }
            }
            return row;
        }
    }
}
