#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Maps <see cref="ParamKind"/> values to concrete <see cref="VisualElement"/>
    /// controls available in Unity runtime UI Toolkit (<c>UnityEngine.UIElements</c>).
    /// Returns null for <see cref="ParamKind.Unknown"/> (with a diagnostic log) so
    /// callers can skip rendering per Requirement 6.10.
    /// (Task 6.5, Requirements 6.2, 6.7, 6.10, 6.11.)
    /// </summary>
    /// <remarks>
    /// Only runtime-side controls are used here so the assembly compiles in player
    /// builds. Color / Vector controls are composed from primitive
    /// <see cref="FloatField"/> rows because Unity's <c>ColorField</c> /
    /// <c>Vector2Field</c> / <c>Vector3Field</c> / <c>Vector4Field</c> live in
    /// <c>UnityEditor.UIElements</c> and would not be available in player builds.
    /// </remarks>
    public sealed class VolumeOverrideParamFactory : IVolumeOverrideParamFactory
    {
        private readonly IDiagnosticsLogger? _log;

        public VolumeOverrideParamFactory(IDiagnosticsLogger? logger = null)
        {
            _log = logger;
        }

        public VisualElement? CreateControl(
            VolumeOverrideParamDto param,
            VolumeOverrideParamValueDto currentValue,
            Action<VolumeOverrideParamValueDto> onChanged)
        {
            switch (param.Kind)
            {
                case ParamKind.Bool:
                {
                    var t = new Toggle(param.DisplayName)
                    {
                        value = currentValue.BoolValue ?? false,
                    };
                    t.RegisterValueChangedCallback(e =>
                        onChanged(new VolumeOverrideParamValueDto(
                            ParamKind.Bool, e.newValue, null, null, null, null, null)));
                    return t;
                }
                case ParamKind.Int:
                {
                    var f = new IntegerField(param.DisplayName) { value = currentValue.IntValue ?? 0 };
                    f.RegisterValueChangedCallback(e =>
                        onChanged(new VolumeOverrideParamValueDto(
                            ParamKind.Int, null, e.newValue, null, null, null, null)));
                    return f;
                }
                case ParamKind.Float:
                case ParamKind.ClampedFloat:
                {
                    var f = new FloatField(param.DisplayName) { value = currentValue.FloatValue ?? 0f };
                    f.RegisterValueChangedCallback(e =>
                        onChanged(new VolumeOverrideParamValueDto(
                            param.Kind, null, null, e.newValue, null, null, null)));
                    return f;
                }
                case ParamKind.Color:
                {
                    var c = currentValue.ColorValue ?? new ColorDto(1, 1, 1, 1);
                    var box = new VisualElement();
                    box.AddToClassList("vsb-slv-color-row");
                    box.Add(new Label(param.DisplayName));
                    float r = c.R, g = c.G, b = c.B, a = c.A;
                    var rField = new FloatField("R") { value = r };
                    var gField = new FloatField("G") { value = g };
                    var bField = new FloatField("B") { value = b };
                    var aField = new FloatField("A") { value = a };
                    void Push() => onChanged(new VolumeOverrideParamValueDto(
                        ParamKind.Color, null, null, null,
                        new ColorDto(r, g, b, a), null, null));
                    rField.RegisterValueChangedCallback(e => { r = e.newValue; Push(); });
                    gField.RegisterValueChangedCallback(e => { g = e.newValue; Push(); });
                    bField.RegisterValueChangedCallback(e => { b = e.newValue; Push(); });
                    aField.RegisterValueChangedCallback(e => { a = e.newValue; Push(); });
                    box.Add(rField);
                    box.Add(gField);
                    box.Add(bField);
                    box.Add(aField);
                    return box;
                }
                case ParamKind.Vector2:
                case ParamKind.Vector3:
                case ParamKind.Vector4:
                {
                    var v = currentValue.VectorValue ?? new Vector4Dto(0, 0, 0, 0);
                    var box = new VisualElement();
                    box.AddToClassList("vsb-slv-vector-row");
                    box.Add(new Label(param.DisplayName));
                    float x = v.X, y = v.Y, z = v.Z, w = v.W;
                    var capturedKind = param.Kind;
                    void Push() => onChanged(new VolumeOverrideParamValueDto(
                        capturedKind, null, null, null, null,
                        new Vector4Dto(x, y, z, w), null));
                    var xField = new FloatField("X") { value = x };
                    xField.RegisterValueChangedCallback(e => { x = e.newValue; Push(); });
                    box.Add(xField);
                    var yField = new FloatField("Y") { value = y };
                    yField.RegisterValueChangedCallback(e => { y = e.newValue; Push(); });
                    box.Add(yField);
                    if (param.Kind == ParamKind.Vector3 || param.Kind == ParamKind.Vector4)
                    {
                        var zField = new FloatField("Z") { value = z };
                        zField.RegisterValueChangedCallback(e => { z = e.newValue; Push(); });
                        box.Add(zField);
                    }
                    if (param.Kind == ParamKind.Vector4)
                    {
                        var wField = new FloatField("W") { value = w };
                        wField.RegisterValueChangedCallback(e => { w = e.newValue; Push(); });
                        box.Add(wField);
                    }
                    return box;
                }
                case ParamKind.Enum:
                {
                    var dropdown = new DropdownField(param.DisplayName);
                    if (param.Range is { } range && range.EnumValues is { } values)
                    {
                        var choices = new List<string>(values);
                        dropdown.choices = choices;
                        dropdown.value = currentValue.EnumValue ?? (choices.Count > 0 ? choices[0] : "");
                    }
                    dropdown.RegisterValueChangedCallback(e =>
                        onChanged(new VolumeOverrideParamValueDto(
                            ParamKind.Enum, null, null, null, null, null, e.newValue)));
                    return dropdown;
                }
                case ParamKind.Unknown:
                default:
                {
                    _log?.Log(LogLevel.Debug, LogCategory.TabSpec,
                        $"VolumeOverrideParamFactory skipping unknown ParamKind for '{param.ParamName}'",
                        new { param.ParamName, param.Kind });
                    return null;
                }
            }
        }
    }

    /// <summary>Abstraction so the section view can swap the factory in tests.</summary>
    public interface IVolumeOverrideParamFactory
    {
        VisualElement? CreateControl(
            VolumeOverrideParamDto param,
            VolumeOverrideParamValueDto currentValue,
            Action<VolumeOverrideParamValueDto> onChanged);
    }
}
