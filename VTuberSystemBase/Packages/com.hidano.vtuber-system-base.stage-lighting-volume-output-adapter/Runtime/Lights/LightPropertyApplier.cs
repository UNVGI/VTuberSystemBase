#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights
{
    /// <summary>
    /// Applies per-light property updates received via dynamic <c>light/{lightId}/{prop}</c>
    /// state topics to the live <see cref="UnityEngine.Light"/> components managed by
    /// <see cref="LightRegistry"/>. Unknown light ids and conversion failures are logged at
    /// warning level and otherwise ignored so a stale message never aborts the renderer.
    /// </summary>
    internal sealed class LightPropertyApplier
    {
        private readonly LightRegistry _registry;
        private readonly AdapterLogger _logger;

        public LightPropertyApplier(LightRegistry registry, AdapterLogger logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void ApplyIntensity(string lightId, StateCommand<float> cmd)
        {
            if (!TryGet(lightId, out var entry, "intensity")) return;
            try { entry!.Light.intensity = cmd.Payload; }
            catch (Exception ex) { LogApplyError(lightId, "intensity", ex); }
        }

        public void ApplyColor(string lightId, StateCommand<ColorDto> cmd)
        {
            if (!TryGet(lightId, out var entry, "color")) return;
            try { entry!.Light.color = DtoConverters.ToUnity(cmd.Payload); }
            catch (Exception ex) { LogApplyError(lightId, "color", ex); }
        }

        public void ApplyRotation(string lightId, StateCommand<Vector3Dto> cmd)
        {
            if (!TryGet(lightId, out var entry, "rotation")) return;
            try { entry!.GameObject.transform.localRotation = DtoConverters.ToQuaternion(cmd.Payload); }
            catch (Exception ex) { LogApplyError(lightId, "rotation", ex); }
        }

        public void ApplyType(string lightId, StateCommand<LightTypeDto> cmd)
        {
            if (!TryGet(lightId, out var entry, "type")) return;
            try { entry!.Light.type = LightTypeMapper.ToUnity(cmd.Payload); }
            catch (Exception ex) { LogApplyError(lightId, "type", ex); }
        }

        public void ApplyRange(string lightId, StateCommand<float> cmd)
        {
            if (!TryGet(lightId, out var entry, "range")) return;
            try { entry!.Light.range = cmd.Payload; }
            catch (Exception ex) { LogApplyError(lightId, "range", ex); }
        }

        public void ApplySpotAngle(string lightId, StateCommand<float> cmd)
        {
            if (!TryGet(lightId, out var entry, "spotAngle")) return;
            try { entry!.Light.spotAngle = cmd.Payload; }
            catch (Exception ex) { LogApplyError(lightId, "spotAngle", ex); }
        }

        public void ApplyDisplayName(string lightId, StateCommand<string> cmd)
        {
            if (!TryGet(lightId, out var entry, "displayName")) return;
            try { entry!.DisplayName = cmd.Payload ?? entry.DisplayName; }
            catch (Exception ex) { LogApplyError(lightId, "displayName", ex); }
        }

        public void ApplyInitial(LightEntry entry, LightInitialDto initial)
        {
            if (entry == null) return;
            try
            {
                entry.Light.type = LightTypeMapper.ToUnity(initial.Type);
                entry.GameObject.transform.localRotation = DtoConverters.ToQuaternion(initial.Rotation);
                entry.Light.color = DtoConverters.ToUnity(initial.Color);
                entry.Light.intensity = initial.Intensity;
                entry.Light.range = initial.Range;
                entry.Light.spotAngle = initial.SpotAngle;
                entry.DisplayName = initial.DisplayName;
                entry.Initial = initial;
            }
            catch (Exception ex) { LogApplyError(entry.LightId, "initial", ex); }
        }

        private bool TryGet(string lightId, out LightEntry? entry, string prop)
        {
            if (string.IsNullOrEmpty(lightId) || !_registry.TryGet(lightId, out var found))
            {
                _logger.Warning("LightPropertyApplier", "unknown_light_id", context: "skipped",
                    lightId: lightId, paramName: prop);
                entry = null;
                return false;
            }
            entry = found;
            if (entry.Light == null)
            {
                _logger.Warning("LightPropertyApplier", "missing_light_component", context: "skipped",
                    lightId: lightId, paramName: prop);
                return false;
            }
            return true;
        }

        private void LogApplyError(string lightId, string prop, Exception ex)
        {
            _logger.Error("LightPropertyApplier", "apply_failed", context: ex.Message,
                lightId: lightId, paramName: prop, exception: ex);
        }
    }
}
