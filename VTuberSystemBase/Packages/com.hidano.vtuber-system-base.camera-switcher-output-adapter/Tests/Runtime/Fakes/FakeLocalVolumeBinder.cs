#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Test double for <see cref="ILocalVolumeBinder"/>. Records every call into
    /// <see cref="Calls"/> in invocation order so tests can assert behaviour without
    /// touching real URP types.
    /// </summary>
    /// <remarks>
    /// Returned <see cref="Volume"/> instances are constructed by adding a
    /// <c>Volume</c> component to a transient stub <see cref="GameObject"/>. They
    /// must therefore be cleaned up in <c>[TearDown]</c> via <see cref="DestroyAllCreated"/>.
    /// </remarks>
    public sealed class FakeLocalVolumeBinder : ILocalVolumeBinder
    {
        private readonly List<GameObject> _createdGameObjects = new();
        private readonly List<VolumeProfile> _createdProfiles = new();

        public List<Call> Calls { get; } = new();
        public VolumeBindResult NextAddOverrideResult { get; set; } = VolumeBindResult.Ok();
        public VolumeBindResult NextRemoveOverrideResult { get; set; } = VolumeBindResult.Ok();
        public VolumeBindResult NextSetOverrideEnabledResult { get; set; } = VolumeBindResult.Ok();
        public VolumeBindResult NextSetOverrideParamResult { get; set; } = VolumeBindResult.Ok();

        public Volume CreateLocalVolume(GameObject parent, CameraId cameraId, int priority)
        {
            Calls.Add(new Call(CallKind.CreateLocalVolume, cameraId.Value, null, null, default));
            var go = new GameObject($"FakeLocalVolume-{cameraId.Value}");
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            _createdGameObjects.Add(go);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.weight = 1f;
            volume.priority = priority;
            volume.enabled = false;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            _createdProfiles.Add(profile);
            return volume;
        }

        public VolumeBindResult AddOverride(Volume volume, string overrideTypeName)
        {
            Calls.Add(new Call(CallKind.AddOverride, null, overrideTypeName, null, default));
            return NextAddOverrideResult;
        }

        public VolumeBindResult RemoveOverride(Volume volume, string overrideTypeName)
        {
            Calls.Add(new Call(CallKind.RemoveOverride, null, overrideTypeName, null, default));
            return NextRemoveOverrideResult;
        }

        public VolumeBindResult SetOverrideEnabled(Volume volume, string overrideTypeName, bool enabled)
        {
            Calls.Add(new Call(CallKind.SetOverrideEnabled, null, overrideTypeName, null, enabled));
            return NextSetOverrideEnabledResult;
        }

        public VolumeBindResult SetOverrideParam(Volume volume, string overrideTypeName, string paramName, JsonElement value)
        {
            Calls.Add(new Call(CallKind.SetOverrideParam, null, overrideTypeName, paramName, value.ToString()));
            return NextSetOverrideParamResult;
        }

        public void SetVolumeEnabled(Volume volume, bool enabled)
        {
            Calls.Add(new Call(CallKind.SetVolumeEnabled, null, null, null, enabled));
            if (volume != null) volume.enabled = enabled;
        }

        public void DestroyLocalVolume(Volume volume)
        {
            Calls.Add(new Call(CallKind.DestroyLocalVolume, null, null, null, default));
            if (volume == null) return;
            var go = volume.gameObject;
            _createdGameObjects.Remove(go);
            Object.Destroy(go);
        }

        public void DestroyAllCreated()
        {
            foreach (var go in _createdGameObjects) if (go != null) Object.Destroy(go);
            _createdGameObjects.Clear();
            foreach (var profile in _createdProfiles) if (profile != null) Object.Destroy(profile);
            _createdProfiles.Clear();
        }

        public enum CallKind
        {
            CreateLocalVolume,
            AddOverride,
            RemoveOverride,
            SetOverrideEnabled,
            SetOverrideParam,
            SetVolumeEnabled,
            DestroyLocalVolume,
        }

        public readonly struct Call
        {
            public Call(CallKind kind, string? cameraId, string? overrideType, string? param, object? value)
            {
                Kind = kind;
                CameraId = cameraId;
                OverrideType = overrideType;
                Param = param;
                Value = value;
            }

            public CallKind Kind { get; }
            public string? CameraId { get; }
            public string? OverrideType { get; }
            public string? Param { get; }
            public object? Value { get; }
        }
    }
}
