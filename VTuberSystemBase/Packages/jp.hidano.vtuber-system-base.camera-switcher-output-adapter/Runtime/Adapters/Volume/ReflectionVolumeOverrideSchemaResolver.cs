#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume
{
    /// <summary>
    /// Builds <see cref="VolumeMetadataResponse"/> by Reflection over
    /// <see cref="VolumeManager.instance"/>'s registered <see cref="VolumeComponent"/>
    /// types (Requirement 7, CSO-11). The result is cached after the first call so
    /// the Reflection cost is paid once.
    /// </summary>
    public sealed class ReflectionVolumeOverrideSchemaResolver : IVolumeOverrideSchemaResolver
    {
        private VolumeMetadataResponse? _cached;

        public VolumeMetadataResponse GetSchema()
        {
            if (_cached.HasValue) return _cached.Value;

            try
            {
                var schemas = BuildSchemas();
                _cached = new VolumeMetadataResponse { Overrides = schemas };
            }
            catch
            {
                _cached = new VolumeMetadataResponse { Overrides = Array.Empty<VolumeOverrideSchema>() };
            }
            return _cached.Value;
        }

        private static IReadOnlyList<VolumeOverrideSchema> BuildSchemas()
        {
            var types = CollectVolumeComponentTypes();
            var result = new List<VolumeOverrideSchema>(types.Length);

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract) continue;
                if (!typeof(VolumeComponent).IsAssignableFrom(t)) continue;

                VolumeComponent? template = null;
                try { template = ScriptableObject.CreateInstance(t) as VolumeComponent; } catch { template = null; }

                var displayName = ResolveDisplayName(t);
                var paramSchemas = new List<VolumeParamSchema>();

                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!typeof(VolumeParameter).IsAssignableFrom(field.FieldType)) continue;

                    VolumeParameter? defaultParam = null;
                    if (template != null)
                    {
                        try { defaultParam = field.GetValue(template) as VolumeParameter; } catch { defaultParam = null; }
                    }

                    var paramSchema = TryBuildParamSchema(field, defaultParam);
                    if (paramSchema.HasValue) paramSchemas.Add(paramSchema.Value);
                }

                if (template != null) UnityEngine.Object.DestroyImmediate(template);

                result.Add(new VolumeOverrideSchema
                {
                    Type = t.Name,
                    DisplayName = displayName,
                    Params = paramSchemas,
                });
            }

            return result;
        }

        private static string ResolveDisplayName(Type t)
        {
            try
            {
                var attr = t.GetCustomAttribute<VolumeComponentMenu>();
                if (attr != null && !string.IsNullOrEmpty(attr.menu))
                {
                    var idx = attr.menu.LastIndexOf('/');
                    return idx >= 0 ? attr.menu.Substring(idx + 1) : attr.menu;
                }
            }
            catch { /* Reflection */ }
            return t.Name;
        }

        private static VolumeParamSchema? TryBuildParamSchema(FieldInfo field, VolumeParameter? defaultParam)
        {
            var inner = ResolveInnerType(field.FieldType);
            if (inner == null)
            {
                Debug.Log($"[CameraSwitcherOutputAdapter] Skipping volume parameter with unresolved inner type: {field.DeclaringType?.Name}.{field.Name}");
                return null;
            }

            var typeTag = ResolveTypeTag(field.FieldType, inner);
            if (typeTag == null)
            {
                Debug.Log($"[CameraSwitcherOutputAdapter] Skipping volume parameter with unsupported type tag: {field.DeclaringType?.Name}.{field.Name} ({field.FieldType.Name})");
                return null;
            }

            JsonElement? min = null;
            JsonElement? max = null;
            ExtractMinMax(field, defaultParam, ref min, ref max);

            JsonElement defaultValue;
            try
            {
                if (defaultParam != null)
                {
                    var raw = ExtractDefault(defaultParam, inner);
                    defaultValue = ToJsonElement(raw);
                }
                else
                {
                    defaultValue = JsonDocument.Parse("null").RootElement;
                }
            }
            catch
            {
                defaultValue = JsonDocument.Parse("null").RootElement;
            }

            IReadOnlyList<string>? enumValues = null;
            if (typeTag == "enum") enumValues = Enum.GetNames(inner);

            return new VolumeParamSchema
            {
                Name = field.Name,
                TypeTag = typeTag,
                Min = min,
                Max = max,
                Default = defaultValue,
                DisplayName = field.Name,
                Unit = null,
                EnumValues = enumValues,
            };
        }

        private static Type[] CollectVolumeComponentTypes() => VolumeComponentTypeCollector.Collect();

        private static Type? ResolveInnerType(Type parameterType)
        {
            for (var t = parameterType; t != null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(VolumeParameter<>))
                    return t.GetGenericArguments()[0];
            }
            return null;
        }

        private static string? ResolveTypeTag(Type parameterType, Type inner)
        {
            if (inner == typeof(float)) return "float";
            if (inner == typeof(int)) return "int";
            if (inner == typeof(bool)) return "bool";
            if (inner == typeof(Color)) return "color";
            if (inner == typeof(Vector2)) return "vector2";
            if (inner == typeof(Vector3)) return "vector3";
            if (inner == typeof(Vector4)) return "vector4";
            if (inner.IsEnum) return "enum";
            return null;
        }

        private static void ExtractMinMax(FieldInfo field, VolumeParameter? defaultParam, ref JsonElement? min, ref JsonElement? max)
        {
            try
            {
                var paramType = field.FieldType;
                var minField = paramType.GetField("min", BindingFlags.Public | BindingFlags.Instance);
                if (minField != null && defaultParam != null)
                {
                    var v = minField.GetValue(defaultParam);
                    if (v != null) min = ToJsonElement(v);
                }
                var maxField = paramType.GetField("max", BindingFlags.Public | BindingFlags.Instance);
                if (maxField != null && defaultParam != null)
                {
                    var v = maxField.GetValue(defaultParam);
                    if (v != null) max = ToJsonElement(v);
                }
            }
            catch
            {
                /* Reflection */
            }
        }

        private static object? ExtractDefault(VolumeParameter parameter, Type inner)
        {
            var paramT = typeof(VolumeParameter<>).MakeGenericType(inner);
            var prop = paramT.GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
            return prop?.GetValue(parameter);
        }

        private static JsonElement ToJsonElement(object? value)
        {
            if (value == null) return JsonDocument.Parse("null").RootElement;
            string json;
            switch (value)
            {
                case Color c:
                    json = $"{{\"r\":{c.r.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"g\":{c.g.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"b\":{c.b.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"a\":{c.a.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
                    break;
                case Vector2 v2:
                    json = $"[{v2.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{v2.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
                    break;
                case Vector3 v3:
                    json = $"[{v3.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{v3.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},{v3.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
                    break;
                case Vector4 v4:
                    json = $"[{v4.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{v4.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},{v4.z.ToString(System.Globalization.CultureInfo.InvariantCulture)},{v4.w.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
                    break;
                case bool b:
                    json = b ? "true" : "false";
                    break;
                case Enum e:
                    json = ((int)(object)e).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default:
                    json = JsonSerializer.Serialize(value);
                    break;
            }
            return JsonDocument.Parse(json).RootElement;
        }
    }
}
