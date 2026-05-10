#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume
{
    /// <summary>
    /// Writes <see cref="JsonElement"/> values into <c>VolumeParameter&lt;T&gt;</c>
    /// fields on a <see cref="VolumeComponent"/> via Reflection (Requirement 6.5,
    /// 6.10).
    /// </summary>
    /// <remarks>
    /// Supported parameter inner types: <c>float</c>, <c>int</c>, <c>bool</c>,
    /// <see cref="Color"/>, <see cref="UnityEngine.Vector2"/>, <see cref="UnityEngine.Vector3"/>,
    /// <see cref="UnityEngine.Vector4"/>, and any <see cref="Enum"/> derivative.
    /// JSON shapes:
    /// <list type="bullet">
    /// <item><c>float</c> / <c>int</c>: numeric.</item>
    /// <item><c>bool</c>: JSON true/false (or 0/1).</item>
    /// <item><c>color</c>: <c>{ "r":..,"g":..,"b":..,"a":.. }</c> — alpha defaults to 1 if absent.</item>
    /// <item><c>vector{2,3,4}</c>: array <c>[x,y,(z,(w))]</c>.</item>
    /// <item><c>enum</c>: integer value of the underlying enum.</item>
    /// </list>
    /// Each successful write also flips <c>overrideState</c> to <c>true</c> (URP's
    /// override marker).
    /// </remarks>
    public sealed class VolumeParameterValueWriter : IVolumeParameterValueWriter
    {
        private readonly Action<string, Exception>? _onParamFailure;
        private readonly Dictionary<(Type, string), FieldInfo?> _fieldCache =
            new Dictionary<(Type, string), FieldInfo?>();

        public VolumeParameterValueWriter(Action<string, Exception>? onParamFailure = null)
        {
            _onParamFailure = onParamFailure;
        }

        public VolumeBindResult Write(VolumeComponent component, string paramName, JsonElement value)
        {
            if (component == null)
                return VolumeBindResult.Error(VolumeBindFailureReasons.ParamNotFound, "component is null");
            if (string.IsNullOrEmpty(paramName))
                return VolumeBindResult.Error(VolumeBindFailureReasons.ParamNotFound, "param name is empty");

            var componentType = component.GetType();
            var field = ResolveField(componentType, paramName);
            if (field == null)
                return VolumeBindResult.Error(VolumeBindFailureReasons.ParamNotFound,
                    $"{componentType.Name}.{paramName}");

            var parameter = field.GetValue(component) as VolumeParameter;
            if (parameter == null)
                return VolumeBindResult.Error(VolumeBindFailureReasons.ParamTypeMismatch,
                    $"{componentType.Name}.{paramName} is not a VolumeParameter");

            try
            {
                var inner = ResolveInnerType(parameter.GetType());
                if (inner == null)
                    return VolumeBindResult.Error(VolumeBindFailureReasons.ParamTypeMismatch,
                        $"unsupported parameter type {parameter.GetType().Name}");

                var converted = Convert(value, inner);
                if (converted is FailureMarker fm)
                    return VolumeBindResult.Error(VolumeBindFailureReasons.ParamTypeMismatch, fm.Detail);

                AssignValue(parameter, inner, converted);
                parameter.overrideState = true;
                return VolumeBindResult.Ok();
            }
            catch (Exception ex)
            {
                _onParamFailure?.Invoke(paramName, ex);
                return VolumeBindResult.Error(VolumeBindFailureReasons.ReflectionFailed, ex.Message, ex);
            }
        }

        private FieldInfo? ResolveField(Type componentType, string paramName)
        {
            var key = (componentType, paramName);
            if (_fieldCache.TryGetValue(key, out var cached)) return cached;

            FieldInfo? field = null;
            for (var t = componentType; t != null && t != typeof(VolumeComponent); t = t.BaseType)
            {
                field = t.GetField(paramName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null) break;
            }
            _fieldCache[key] = field;
            return field;
        }

        private static Type? ResolveInnerType(Type parameterType)
        {
            for (var t = parameterType; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(VolumeParameter<>))
                {
                    return t.GetGenericArguments()[0];
                }
            }
            return null;
        }

        private static object Convert(JsonElement value, Type inner)
        {
            if (inner == typeof(float)) return value.ValueKind switch
            {
                JsonValueKind.Number => value.GetSingle(),
                JsonValueKind.String when float.TryParse(value.GetString(), out var f) => f,
                _ => new FailureMarker($"expected float, got {value.ValueKind}"),
            };
            if (inner == typeof(int)) return value.ValueKind switch
            {
                JsonValueKind.Number => value.GetInt32(),
                JsonValueKind.String when int.TryParse(value.GetString(), out var i) => i,
                _ => new FailureMarker($"expected int, got {value.ValueKind}"),
            };
            if (inner == typeof(bool)) return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => value.GetInt32() != 0,
                _ => new FailureMarker($"expected bool, got {value.ValueKind}"),
            };
            if (inner == typeof(Color)) return ConvertColor(value);
            if (inner == typeof(Vector2)) return ConvertVector(value, 2);
            if (inner == typeof(Vector3)) return ConvertVector(value, 3);
            if (inner == typeof(Vector4)) return ConvertVector(value, 4);
            if (inner.IsEnum) return value.ValueKind switch
            {
                JsonValueKind.Number => Enum.ToObject(inner, value.GetInt32()),
                JsonValueKind.String when Enum.TryParse(inner, value.GetString(), ignoreCase: false, out var parsed)
                    => parsed!,
                _ => new FailureMarker($"expected enum {inner.Name}, got {value.ValueKind}"),
            };
            return new FailureMarker($"unsupported inner type {inner.Name}");
        }

        private static object ConvertColor(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                float r = 0, g = 0, b = 0, a = 1f;
                if (value.TryGetProperty("r", out var rp)) r = rp.GetSingle();
                if (value.TryGetProperty("g", out var gp)) g = gp.GetSingle();
                if (value.TryGetProperty("b", out var bp)) b = bp.GetSingle();
                if (value.TryGetProperty("a", out var ap)) a = ap.GetSingle();
                return new Color(r, g, b, a);
            }
            if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= 3)
            {
                var arr = value.EnumerateArray();
                arr.MoveNext(); var r = arr.Current.GetSingle();
                arr.MoveNext(); var g = arr.Current.GetSingle();
                arr.MoveNext(); var b = arr.Current.GetSingle();
                var a = 1f;
                if (arr.MoveNext()) a = arr.Current.GetSingle();
                return new Color(r, g, b, a);
            }
            return new FailureMarker($"expected color object/array, got {value.ValueKind}");
        }

        private static object ConvertVector(JsonElement value, int size)
        {
            if (value.ValueKind != JsonValueKind.Array)
                return new FailureMarker($"expected array of length {size}, got {value.ValueKind}");
            var len = value.GetArrayLength();
            if (len < size) return new FailureMarker($"expected array length {size}, got {len}");

            float x = 0f, y = 0f, z = 0f, w = 0f;
            var idx = 0;
            foreach (var element in value.EnumerateArray())
            {
                if (idx == 0) x = element.GetSingle();
                else if (idx == 1) y = element.GetSingle();
                else if (idx == 2) z = element.GetSingle();
                else if (idx == 3) w = element.GetSingle();
                idx++;
                if (idx >= size) break;
            }
            return size switch
            {
                2 => (object)new Vector2(x, y),
                3 => new Vector3(x, y, z),
                4 => new Vector4(x, y, z, w),
                _ => new FailureMarker("unsupported vector size"),
            };
        }

        private static void AssignValue(VolumeParameter parameter, Type inner, object newValue)
        {
            // Use the strongly-typed `value` setter via reflection over VolumeParameter<T>.
            var paramT = typeof(VolumeParameter<>).MakeGenericType(inner);
            var prop = paramT.GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
            if (prop == null) throw new InvalidOperationException($"VolumeParameter<{inner.Name}>.value not found");
            prop.SetValue(parameter, newValue);
        }

        private readonly struct FailureMarker
        {
            public FailureMarker(string detail) { Detail = detail; }
            public string Detail { get; }
        }
    }
}
