#nullable enable
using System;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume
{
    /// <summary>
    /// Default <see cref="ILocalVolumeBinder"/> using the <c>isGlobal=true</c> +
    /// <c>enabled</c> toggle approach (CSO-10). Each camera owns one child
    /// <c>Volume</c> with an empty <see cref="VolumeProfile"/>; the active-set state
    /// machine flips <c>enabled</c> rather than rebuilding profiles.
    /// </summary>
    public sealed class GlobalEnabledLocalVolumeBinder : ILocalVolumeBinder
    {
        private readonly VolumeComponentTypeResolver _typeResolver;
        private readonly IVolumeParameterValueWriter? _valueWriter;

        public GlobalEnabledLocalVolumeBinder(
            VolumeComponentTypeResolver? typeResolver = null,
            IVolumeParameterValueWriter? valueWriter = null)
        {
            _typeResolver = typeResolver ?? new VolumeComponentTypeResolver();
            _valueWriter = valueWriter;
        }

        public UnityEngine.Rendering.Volume CreateLocalVolume(GameObject parent, CameraId cameraId, int priority)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            var go = new GameObject($"LocalVolume-{cameraId.Value}");
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            var volume = go.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.weight = 1f;
            volume.priority = priority;
            volume.enabled = false;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = $"LocalVolumeProfile-{cameraId.Value}";
            volume.sharedProfile = profile;
            return volume;
        }

        public VolumeBindResult AddOverride(UnityEngine.Rendering.Volume volume, string overrideTypeName)
        {
            if (volume == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume is null");
            var profile = volume.sharedProfile;
            if (profile == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume profile is null");

            var type = _typeResolver.Resolve(overrideTypeName);
            if (type == null) return VolumeBindResult.Error(VolumeBindFailureReasons.UnknownOverrideType, overrideTypeName);

            try
            {
                if (profile.Has(type)) return VolumeBindResult.Ok(); // idempotent
                profile.Add(type, overrides: false);
                return VolumeBindResult.Ok();
            }
            catch (Exception ex)
            {
                return VolumeBindResult.Error(VolumeBindFailureReasons.ReflectionFailed, ex.Message, ex);
            }
        }

        public VolumeBindResult RemoveOverride(UnityEngine.Rendering.Volume volume, string overrideTypeName)
        {
            if (volume == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume is null");
            var profile = volume.sharedProfile;
            if (profile == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume profile is null");

            var type = _typeResolver.Resolve(overrideTypeName);
            if (type == null) return VolumeBindResult.Error(VolumeBindFailureReasons.UnknownOverrideType, overrideTypeName);

            try
            {
                if (!profile.Has(type)) return VolumeBindResult.Ok(); // idempotent
                profile.Remove(type);
                return VolumeBindResult.Ok();
            }
            catch (Exception ex)
            {
                return VolumeBindResult.Error(VolumeBindFailureReasons.ReflectionFailed, ex.Message, ex);
            }
        }

        public VolumeBindResult SetOverrideEnabled(UnityEngine.Rendering.Volume volume, string overrideTypeName, bool enabled)
        {
            if (volume == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume is null");
            var profile = volume.sharedProfile;
            if (profile == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume profile is null");

            var type = _typeResolver.Resolve(overrideTypeName);
            if (type == null) return VolumeBindResult.Error(VolumeBindFailureReasons.UnknownOverrideType, overrideTypeName);

            try
            {
                if (!profile.TryGet(type, out var component) || component == null)
                    return VolumeBindResult.Error(VolumeBindFailureReasons.UnknownOverrideType,
                        $"override not attached: {overrideTypeName}");
                component.active = enabled;
                return VolumeBindResult.Ok();
            }
            catch (Exception ex)
            {
                return VolumeBindResult.Error(VolumeBindFailureReasons.ReflectionFailed, ex.Message, ex);
            }
        }

        public VolumeBindResult SetOverrideParam(UnityEngine.Rendering.Volume volume, string overrideTypeName, string paramName, JsonElement value)
        {
            if (volume == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume is null");
            var profile = volume.sharedProfile;
            if (profile == null) return VolumeBindResult.Error(VolumeBindFailureReasons.Unknown, "volume profile is null");

            var type = _typeResolver.Resolve(overrideTypeName);
            if (type == null) return VolumeBindResult.Error(VolumeBindFailureReasons.UnknownOverrideType, overrideTypeName);

            VolumeComponent? component;
            try
            {
                if (!profile.TryGet(type, out component) || component == null)
                    return VolumeBindResult.Error(VolumeBindFailureReasons.UnknownOverrideType,
                        $"override not attached: {overrideTypeName}");
            }
            catch (Exception ex)
            {
                return VolumeBindResult.Error(VolumeBindFailureReasons.ReflectionFailed, ex.Message, ex);
            }

            if (_valueWriter == null)
                return VolumeBindResult.Error(VolumeBindFailureReasons.ReflectionFailed,
                    "no value writer configured");
            return _valueWriter.Write(component, paramName, value);
        }

        public void SetVolumeEnabled(UnityEngine.Rendering.Volume volume, bool enabled)
        {
            if (volume != null) volume.enabled = enabled;
        }

        public void DestroyLocalVolume(UnityEngine.Rendering.Volume volume)
        {
            if (volume == null) return;
            var profile = volume.sharedProfile;
            if (profile != null)
            {
                volume.sharedProfile = null;
                UnityEngine.Object.Destroy(profile);
            }
            UnityEngine.Object.Destroy(volume.gameObject);
        }
    }
}
