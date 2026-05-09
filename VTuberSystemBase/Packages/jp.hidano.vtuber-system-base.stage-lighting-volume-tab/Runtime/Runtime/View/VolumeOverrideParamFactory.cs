#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Maps <see cref="ParamKind"/> values to concrete <see cref="VisualElement"/>
    /// controls. Returns null for <see cref="ParamKind.Unknown"/> (with a diagnostic log)
    /// so callers can skip rendering per Requirement 6.10.
    /// (Task 6.5, Requirements 6.2, 6.7, 6.10, 6.11.)
    /// </summary>
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
                    var cf = new ColorField(param.DisplayName) { value = new Color(c.R, c.G, c.B, c.A) };
                    cf.RegisterValueChangedCallback(e =>
                    {
                        var col = e.newValue;
                        onChanged(new VolumeOverrideParamValueDto(
                            ParamKind.Color, null, null, null,
                            new ColorDto(col.r, col.g, col.b, col.a), null, null));
                    });
                    return cf;
                }
                case ParamKind.Vector2:
                {
                    var v = currentValue.VectorValue ?? new Vector4Dto(0, 0, 0, 0);
                    var f = new Vector2Field(param.DisplayName) { value = new Vector2(v.X, v.Y) };
                    f.RegisterValueChangedCallback(e =>
                        onChanged(new VolumeOverrideParamValueDto(
                            ParamKind.Vector2, null, null, null, null,
                            new Vector4Dto(e.newValue.x, e.newValue.y, 0, 0), null)));
                    return f;
                }
                case ParamKind.Vector3:
                {
                    var v = currentValue.VectorValue ?? new Vector4Dto(0, 0, 0, 0);
                    var f = new Vector3Field(param.DisplayName) { value = new Vector3(v.X, v.Y, v.Z) };
                    f.RegisterValueChangedCallback(e =>
                        onChanged(new VolumeOverrideParamValueDto(
                            ParamKind.Vector3, null, null, null, null,
                            new Vector4Dto(e.newValue.x, e.newValue.y, e.newValue.z, 0), null)));
                    return f;
                }
                case ParamKind.Vector4:
                {
                    var v = currentValue.VectorValue ?? new Vector4Dto(0, 0, 0, 0);
                    var f = new Vector4Field(param.DisplayName) { value = new Vector4(v.X, v.Y, v.Z, v.W) };
                    f.RegisterValueChangedCallback(e =>
                        onChanged(new VolumeOverrideParamValueDto(
                            ParamKind.Vector4, null, null, null, null,
                            new Vector4Dto(e.newValue.x, e.newValue.y, e.newValue.z, e.newValue.w), null)));
                    return f;
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
