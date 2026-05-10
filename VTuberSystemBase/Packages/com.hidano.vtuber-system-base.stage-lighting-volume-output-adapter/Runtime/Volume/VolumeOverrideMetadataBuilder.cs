#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume
{
    /// <summary>
    /// Reflects over a list of URP <c>VolumeComponent</c>-derived types and produces a
    /// <see cref="VolumeOverrideSchemaDto"/> describing every public instance field that is
    /// itself a <c>VolumeParameter</c> derivative. Default values are taken from a freshly
    /// instantiated <c>ScriptableObject.CreateInstance</c> of the volume component (so a
    /// project's tweaked field initializers are honored).
    /// </summary>
    public sealed class VolumeOverrideMetadataBuilder
    {
        public const int SchemaVersion = 1;

        private readonly AdapterLogger? _logger;

        public VolumeOverrideMetadataBuilder() : this(null) { }
        internal VolumeOverrideMetadataBuilder(AdapterLogger? logger) { _logger = logger; }

        public VolumeOverrideSchemaDto Build(IReadOnlyList<Type> volumeComponentTypes)
        {
            var types = new List<VolumeOverrideTypeDto>();
            if (volumeComponentTypes == null) return new VolumeOverrideSchemaDto(SchemaVersion, types);

            foreach (var t in volumeComponentTypes)
            {
                if (t == null || t.IsAbstract) continue;
                try
                {
                    var dto = BuildTypeDto(t);
                    types.Add(dto);
                }
                catch (Exception ex)
                {
                    _logger?.Warning("VolumeOverrideMetadataBuilder", "type_skipped",
                        context: ex.Message, typeFullName: t.FullName, exception: ex);
                }
            }
            return new VolumeOverrideSchemaDto(SchemaVersion, types);
        }

        private VolumeOverrideTypeDto BuildTypeDto(Type t)
        {
            var displayName = ResolveDisplayName(t);

            // Default values come from a transient instance.
            VolumeComponent? instance = null;
            try
            {
                if (typeof(VolumeComponent).IsAssignableFrom(t))
                {
                    instance = (VolumeComponent)ScriptableObject.CreateInstance(t);
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning("VolumeOverrideMetadataBuilder", "instance_failed",
                    context: ex.Message, typeFullName: t.FullName);
                instance = null;
            }

            try
            {
                var paramFields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var paramDtos = new List<VolumeOverrideParamDto>();
                foreach (var f in paramFields)
                {
                    if (!typeof(VolumeParameter).IsAssignableFrom(f.FieldType)) continue;
                    var kind = VolumeParameterKindResolver.Resolve(f.FieldType);
                    var defaultValue = TryReadDefault(instance, f, kind);
                    var range = ResolveRange(instance, f, kind);
                    paramDtos.Add(new VolumeOverrideParamDto(
                        ParamName: f.Name,
                        Kind: kind,
                        DisplayName: f.Name,
                        DefaultValue: defaultValue,
                        Range: range));
                }
                return new VolumeOverrideTypeDto(t.FullName ?? t.Name, displayName, paramDtos);
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
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
            catch { /* fall through */ }
            return t.Name;
        }

        private VolumeOverrideParamValueDto TryReadDefault(VolumeComponent? component, FieldInfo field, ParamKind kind)
        {
            if (component == null) return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
            try
            {
                var paramObj = field.GetValue(component);
                if (paramObj == null) return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
                var paramType = paramObj.GetType();
                var valueProp = paramType.GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
                if (valueProp == null) return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
                var raw = valueProp.GetValue(paramObj);
                return Encode(kind, raw);
            }
            catch
            {
                return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
            }
        }

        private static VolumeOverrideParamValueDto Encode(ParamKind kind, object? raw)
        {
            switch (kind)
            {
                case ParamKind.Bool:
                    return new VolumeOverrideParamValueDto(kind, raw is bool b ? b : null, null, null, null, null, null);
                case ParamKind.Int:
                    return new VolumeOverrideParamValueDto(kind, null, raw is int i ? i : null, null, null, null, null);
                case ParamKind.Float:
                case ParamKind.ClampedFloat:
                    return new VolumeOverrideParamValueDto(kind, null, null, raw is float f ? f : null, null, null, null);
                case ParamKind.Color:
                    if (raw is Color c)
                        return new VolumeOverrideParamValueDto(kind, null, null, null, new ColorDto(c.r, c.g, c.b, c.a), null, null);
                    return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
                case ParamKind.Vector2:
                    if (raw is Vector2 v2)
                        return new VolumeOverrideParamValueDto(kind, null, null, null, null, new Vector4Dto(v2.x, v2.y, 0f, 0f), null);
                    return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
                case ParamKind.Vector3:
                    if (raw is Vector3 v3)
                        return new VolumeOverrideParamValueDto(kind, null, null, null, null, new Vector4Dto(v3.x, v3.y, v3.z, 0f), null);
                    return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
                case ParamKind.Vector4:
                    if (raw is Vector4 v4)
                        return new VolumeOverrideParamValueDto(kind, null, null, null, null, new Vector4Dto(v4.x, v4.y, v4.z, v4.w), null);
                    return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
                case ParamKind.Enum:
                    return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, raw?.ToString());
                default:
                    return new VolumeOverrideParamValueDto(kind, null, null, null, null, null, null);
            }
        }

        private static VolumeOverrideParamRangeDto? ResolveRange(VolumeComponent? component, FieldInfo field, ParamKind kind)
        {
            try
            {
                var paramObj = component != null ? field.GetValue(component) : null;
                if (kind == ParamKind.Enum)
                {
                    // The generic argument carries the enum type.
                    var t = field.FieldType;
                    while (t != null && t != typeof(object))
                    {
                        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(VolumeParameter<>))
                        {
                            var inner = t.GetGenericArguments()[0];
                            if (inner.IsEnum)
                            {
                                var names = Enum.GetNames(inner);
                                return new VolumeOverrideParamRangeDto(null, null, null, null, names);
                            }
                        }
                        t = t.BaseType;
                    }
                    return null;
                }

                if (paramObj == null) return null;
                var paramType = paramObj.GetType();

                if (kind == ParamKind.Float || kind == ParamKind.ClampedFloat)
                {
                    var min = ReadFloatField(paramType, paramObj, "min");
                    var max = ReadFloatField(paramType, paramObj, "max");
                    if (min.HasValue || max.HasValue)
                        return new VolumeOverrideParamRangeDto(min, max, null, null, null);
                    return null;
                }
                if (kind == ParamKind.Int)
                {
                    var min = ReadIntField(paramType, paramObj, "min");
                    var max = ReadIntField(paramType, paramObj, "max");
                    if (min.HasValue || max.HasValue)
                        return new VolumeOverrideParamRangeDto(null, null, min, max, null);
                    return null;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static float? ReadFloatField(Type t, object instance, string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return null;
            try { return f.GetValue(instance) is float v ? v : (float?)null; }
            catch { return null; }
        }

        private static int? ReadIntField(Type t, object instance, string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return null;
            try { return f.GetValue(instance) is int v ? v : (int?)null; }
            catch { return null; }
        }
    }
}
