#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume
{
    /// <summary>
    /// Generic reflection-based setter for any URP <c>VolumeParameter&lt;T&gt;</c> derivative.
    /// One implementation handles 30+ derived types so we never need a per-type switch
    /// statement; the IL2CPP <c>link.xml</c> shipped with this package keeps the relevant
    /// types from being stripped.
    /// </summary>
    internal static class VolumeParameterReflectionSetter
    {
        private static readonly FieldInfo? OverrideStateField = typeof(VolumeParameter)
            .GetField("overrideState", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool ApplyValue(VolumeComponent component, string paramName, VolumeOverrideParamValueDto value, AdapterLogger? logger = null)
        {
            if (component == null) return false;
            if (string.IsNullOrEmpty(paramName)) return false;

            FieldInfo? field;
            try
            {
                field = component.GetType().GetField(paramName, BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                logger?.Warning("VolumeParameterReflectionSetter", "field_lookup_failed",
                    context: ex.Message, typeFullName: component.GetType().FullName, paramName: paramName, exception: ex);
                return false;
            }
            if (field == null)
            {
                logger?.Warning("VolumeParameterReflectionSetter", "field_not_found",
                    context: "skipped", typeFullName: component.GetType().FullName, paramName: paramName);
                return false;
            }

            object? paramObj;
            try { paramObj = field.GetValue(component); }
            catch (Exception ex)
            {
                logger?.Warning("VolumeParameterReflectionSetter", "param_read_failed",
                    context: ex.Message, typeFullName: component.GetType().FullName, paramName: paramName, exception: ex);
                return false;
            }
            if (paramObj is not VolumeParameter volumeParameter)
            {
                logger?.Warning("VolumeParameterReflectionSetter", "field_not_volume_parameter",
                    context: "skipped", typeFullName: component.GetType().FullName, paramName: paramName);
                return false;
            }

            try
            {
                if (!ConvertAndAssign(volumeParameter, value, logger))
                {
                    return false;
                }
                // Set overrideState = true (field is on the base type).
                OverrideStateField?.SetValue(volumeParameter, true);
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warning("VolumeParameterReflectionSetter", "assign_failed",
                    context: ex.Message, typeFullName: component.GetType().FullName, paramName: paramName, exception: ex);
                return false;
            }
        }

        private static bool ConvertAndAssign(VolumeParameter param, VolumeOverrideParamValueDto value, AdapterLogger? logger)
        {
            var paramType = param.GetType();
            var valueProp = paramType.GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
            if (valueProp == null || !valueProp.CanWrite) return false;

            object? converted = value.Kind switch
            {
                ParamKind.Bool => (object?)value.BoolValue,
                ParamKind.Int => (object?)value.IntValue,
                ParamKind.Float => (object?)value.FloatValue,
                ParamKind.ClampedFloat => (object?)value.FloatValue,
                ParamKind.Color when value.ColorValue is { } c => new Color(c.R, c.G, c.B, c.A),
                ParamKind.Vector2 when value.VectorValue is { } v => new Vector2(v.X, v.Y),
                ParamKind.Vector3 when value.VectorValue is { } v => new Vector3(v.X, v.Y, v.Z),
                ParamKind.Vector4 when value.VectorValue is { } v => new Vector4(v.X, v.Y, v.Z, v.W),
                ParamKind.Enum when value.EnumValue != null => ParseEnum(paramType, value.EnumValue, logger),
                _ => null,
            };

            if (converted == null) return false;
            try
            {
                valueProp.SetValue(param, converted);
                return true;
            }
            catch (Exception ex)
            {
                logger?.Warning("VolumeParameterReflectionSetter", "set_value_failed",
                    context: ex.Message, paramName: valueProp.Name);
                return false;
            }
        }

        private static object? ParseEnum(Type paramType, string text, AdapterLogger? logger)
        {
            // Walk up to find VolumeParameter<TEnum> and parse the enum.
            var t = paramType;
            while (t != null && t != typeof(object))
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(VolumeParameter<>))
                {
                    var inner = t.GetGenericArguments().FirstOrDefault();
                    if (inner != null && inner.IsEnum)
                    {
                        try { return Enum.Parse(inner, text, ignoreCase: false); }
                        catch (Exception ex)
                        {
                            logger?.Warning("VolumeParameterReflectionSetter", "enum_parse_failed",
                                context: ex.Message, typeFullName: paramType.FullName, paramName: text);
                            return null;
                        }
                    }
                }
                t = t.BaseType;
            }
            return null;
        }
    }
}
