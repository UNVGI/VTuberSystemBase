#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.CommonUi.Controls;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Production <see cref="IDynamicSettingControlFactory"/>. Creates
    /// <see cref="VsbSlider"/> / <see cref="Toggle"/> / <see cref="VsbColorPicker"/> /
    /// <see cref="VsbToggleGroup"/> wired to a <see cref="SettingControl"/> facade
    /// the panel presenter consumes. Skips entries with malformed metadata
    /// (returns Root=null) and logs to <see cref="IDiagnosticsLogger"/>.
    /// </summary>
    public sealed class DynamicSettingControlFactory : IDynamicSettingControlFactory
    {
        public const string SettingRowClass = "vsb-char-tab__setting-row";
        public const string LabelClass = "vsb-char-tab__setting-row__label";
        public const string InputClass = "vsb-char-tab__setting-row__input";

        private readonly IDiagnosticsLogger? _log;

        public DynamicSettingControlFactory(IDiagnosticsLogger? logger = null)
        {
            _log = logger;
        }

        public SettingControl Build(SettingSchemaEntry entry, SettingValue initialValue)
        {
            if (entry is null) throw new ArgumentNullException(nameof(entry));
            var control = new SettingControl { SettingKey = entry.Key };

            if (string.IsNullOrEmpty(entry.Key))
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec, "DynamicControl: empty key, skipping.");
                return control; // Root = null
            }

            if (string.Equals(entry.Kind, "command", StringComparison.OrdinalIgnoreCase))
            {
                return BuildButton(entry);
            }

            switch (entry.Type)
            {
                case SettingType.Float:
                case SettingType.Int:
                    return BuildSlider(entry, initialValue);
                case SettingType.Bool:
                    return BuildToggle(entry, initialValue);
                case SettingType.Color:
                    return BuildColor(entry, initialValue);
                case SettingType.Enum:
                    return BuildEnum(entry, initialValue);
                case SettingType.Vector3:
                    return BuildVector3(entry, initialValue);
                default:
                    _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                        $"DynamicControl: unknown SettingType {(int)entry.Type} for key '{entry.Key}', skipping.");
                    return control;
            }
        }

        private SettingControl BuildButton(SettingSchemaEntry entry)
        {
            var root = MakeRow(entry);
            var btn = new Button { text = entry.Label, name = entry.Key };
            btn.AddToClassList(InputClass);
            root.Add(btn);
            var control = new SettingControl { Root = root, SettingKey = entry.Key, IsCommand = true };
            btn.clicked += control.RaiseCommand;
            return control;
        }

        private SettingControl BuildSlider(SettingSchemaEntry entry, SettingValue initial)
        {
            var root = MakeRow(entry);
            var slider = new VsbSlider(_log)
            {
                name = entry.Key,
                min = entry.Min?.FloatValue ?? 0f,
                max = entry.Max?.FloatValue ?? 1f,
                step = entry.Step ?? 0f,
            };
            // Validate range; coerce on disorder rather than crashing.
            if (slider.min > slider.max)
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"DynamicControl: '{entry.Key}' min ({slider.min}) > max ({slider.max}); disabling.");
                slider.SetEnabled(false);
            }
            slider.AddToClassList(InputClass);
            slider.value = entry.Type == SettingType.Int ? initial.IntValue : initial.FloatValue;
            root.Add(slider);

            var control = new SettingControl { Root = root, SettingKey = entry.Key };
            slider.ValueChanged += v =>
            {
                control.RaiseValue(entry.Type == SettingType.Int
                    ? SettingValue.Int(Mathf.RoundToInt(v))
                    : SettingValue.Float(v));
            };
            slider.RegisterCallback<PointerDownEvent>(_ => control.RaiseInteracting(true));
            slider.RegisterCallback<PointerUpEvent>(_ => control.RaiseInteracting(false));
            return control;
        }

        private SettingControl BuildToggle(SettingSchemaEntry entry, SettingValue initial)
        {
            var root = MakeRow(entry);
            var toggle = new Toggle { name = entry.Key, value = initial.BoolValue };
            toggle.AddToClassList(InputClass);
            root.Add(toggle);
            var control = new SettingControl { Root = root, SettingKey = entry.Key };
            toggle.RegisterValueChangedCallback(evt => control.RaiseValue(SettingValue.Bool(evt.newValue)));
            return control;
        }

        private SettingControl BuildColor(SettingSchemaEntry entry, SettingValue initial)
        {
            var root = MakeRow(entry);
            var picker = new VsbColorPicker(_log) { name = entry.Key, value = initial.ColorValue };
            picker.AddToClassList(InputClass);
            root.Add(picker);
            var control = new SettingControl { Root = root, SettingKey = entry.Key };
            picker.ValueChanged += c => control.RaiseValue(SettingValue.Color(c));
            picker.RegisterCallback<PointerDownEvent>(_ => control.RaiseInteracting(true));
            picker.RegisterCallback<PointerUpEvent>(_ => control.RaiseInteracting(false));
            return control;
        }

        private SettingControl BuildEnum(SettingSchemaEntry entry, SettingValue initial)
        {
            var root = MakeRow(entry);
            var group = new VsbToggleGroup(_log) { name = entry.Key };
            if (entry.Options is { Count: > 0 })
            {
                group.keys = string.Join(",", entry.Options);
                if (!string.IsNullOrEmpty(initial.EnumValue))
                {
                    foreach (var k in group.Keys)
                    {
                        if (k == initial.EnumValue) { group.Select(k); break; }
                    }
                }
            }
            else
            {
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"DynamicControl: enum '{entry.Key}' has no options; disabling.");
                group.SetEnabled(false);
            }
            group.AddToClassList(InputClass);
            root.Add(group);
            var control = new SettingControl { Root = root, SettingKey = entry.Key };
            group.SelectionChanged += k => control.RaiseValue(SettingValue.Enum(k));
            return control;
        }

        private SettingControl BuildVector3(SettingSchemaEntry entry, SettingValue initial)
        {
            var root = MakeRow(entry);
            var container = new VisualElement();
            container.AddToClassList(InputClass);
            root.Add(container);

            var s = new[] { "x", "y", "z" };
            var sliders = new VsbSlider[3];
            var v = initial.Vector3Value;
            float[] components = { v.x, v.y, v.z };
            float minV = entry.Min?.FloatValue ?? 0f;
            float maxV = entry.Max?.FloatValue ?? 1f;
            for (int i = 0; i < 3; i++)
            {
                var slider = new VsbSlider(_log)
                {
                    name = $"{entry.Key}.{s[i]}",
                    min = minV,
                    max = maxV,
                    step = entry.Step ?? 0f,
                    value = components[i],
                };
                slider.AddToClassList(InputClass + "__component");
                sliders[i] = slider;
                container.Add(slider);
            }

            var control = new SettingControl { Root = root, SettingKey = entry.Key };
            void Push()
            {
                var next = new Vector3(sliders[0].value, sliders[1].value, sliders[2].value);
                control.RaiseValue(SettingValue.Vector3(next));
            }
            for (int i = 0; i < 3; i++) sliders[i].ValueChanged += _ => Push();
            return control;
        }

        private static VisualElement MakeRow(SettingSchemaEntry entry)
        {
            var row = new VisualElement { name = $"row.{entry.Key}" };
            row.AddToClassList(SettingRowClass);
            var label = new Label(entry.Label);
            label.AddToClassList(LabelClass);
            row.Add(label);
            return row;
        }
    }
}
