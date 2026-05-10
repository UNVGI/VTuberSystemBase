#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume
{
    /// <summary>
    /// Resolves a concrete <c>VolumeParameter</c>-derived type to a wire-friendly
    /// <see cref="ParamKind"/>. Uses inheritance + the generic argument of
    /// <c>VolumeParameter&lt;T&gt;</c> so we cover the dozens of URP / HDRP derived
    /// parameter classes without enumerating each one.
    /// </summary>
    internal static class VolumeParameterKindResolver
    {
        public static ParamKind Resolve(Type? volumeParameterType)
        {
            if (volumeParameterType == null) return ParamKind.Unknown;

            // Walk up the inheritance chain to detect ClampedFloat first (it derives from
            // FloatParameter and we want to surface the clamping fact via ParamKind).
            if (typeof(ClampedFloatParameter).IsAssignableFrom(volumeParameterType)) return ParamKind.ClampedFloat;

            // Find the closed VolumeParameter<T> base.
            var t = volumeParameterType;
            while (t != null && t != typeof(object))
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(VolumeParameter<>))
                {
                    var inner = t.GetGenericArguments()[0];
                    if (inner == typeof(bool)) return ParamKind.Bool;
                    if (inner == typeof(int)) return ParamKind.Int;
                    if (inner == typeof(float)) return ParamKind.Float;
                    if (inner == typeof(Color)) return ParamKind.Color;
                    if (inner == typeof(Vector2)) return ParamKind.Vector2;
                    if (inner == typeof(Vector3)) return ParamKind.Vector3;
                    if (inner == typeof(Vector4)) return ParamKind.Vector4;
                    if (inner.IsEnum) return ParamKind.Enum;
                    return ParamKind.Unknown;
                }
                t = t.BaseType;
            }
            return ParamKind.Unknown;
        }
    }
}
