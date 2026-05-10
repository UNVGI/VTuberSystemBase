#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.State;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Builds VisualElement controls from a <see cref="SettingSchemaEntry"/>.
    /// Production maps Float/Int → VsbSlider, Bool → Toggle, Color → VsbColorPicker,
    /// Enum → VsbToggleGroup, Vector3 → 3 stacked VsbSliders, Kind=="command" → Button.
    /// (task 2.3.)
    /// </summary>
    public interface IDynamicSettingControlFactory
    {
        SettingControl Build(SettingSchemaEntry entry, SettingValue initialValue);
    }

    /// <summary>
    /// Result of <see cref="IDynamicSettingControlFactory.Build"/>. <see cref="Root"/>
    /// is null when the entry was unrecognised (caller skips it).
    /// </summary>
    public sealed class SettingControl
    {
        public VisualElement? Root { get; init; }
        public string SettingKey { get; init; } = "";
        public bool IsCommand { get; init; }
        public event Action<SettingValue>? ValueChangedEvent;
        public event Action? CommandTriggeredEvent;
        public event Action<bool>? InteractingChangedEvent;

        internal void RaiseValue(SettingValue v) => ValueChangedEvent?.Invoke(v);
        internal void RaiseCommand() => CommandTriggeredEvent?.Invoke();
        internal void RaiseInteracting(bool isInteracting) => InteractingChangedEvent?.Invoke(isInteracting);
    }
}
