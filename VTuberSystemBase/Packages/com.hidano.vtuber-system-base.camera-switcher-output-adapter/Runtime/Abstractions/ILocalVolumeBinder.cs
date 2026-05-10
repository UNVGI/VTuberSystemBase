#nullable enable
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Per-camera Local Volume operations (CSO-10). Hides the URP <c>Volume</c> /
    /// <c>VolumeProfile</c> / <c>VolumeComponent</c> Reflection details from the
    /// adapter state machine, so different binding strategies (isGlobal toggle vs
    /// trigger-collider Layer Mask) can be swapped without touching the domain.
    /// </summary>
    /// <remarks>
    /// All operations MUST be invoked on the Unity main thread. Failures are
    /// signalled via <see cref="VolumeBindResult"/>; implementations MUST NOT throw
    /// on user-actionable errors (unknown override type, parameter not found, etc.) —
    /// those are reported to <c>FailureAggregator</c> by the caller.
    /// </remarks>
    public interface ILocalVolumeBinder
    {
        /// <summary>
        /// Attaches a child <c>Volume</c> to <paramref name="parent"/> and returns it.
        /// The volume is created with <c>isGlobal=true</c>, <c>weight=1</c>,
        /// <c>priority=<paramref name="priority"/></c>, <c>enabled=false</c>, and a
        /// freshly allocated empty <c>VolumeProfile</c>. Never returns <c>null</c>.
        /// </summary>
        Volume CreateLocalVolume(GameObject parent, CameraId cameraId, int priority);

        /// <summary>
        /// Adds a <see cref="VolumeComponent"/> override of the named type to the
        /// volume's profile.
        /// </summary>
        VolumeBindResult AddOverride(Volume volume, string overrideTypeName);

        /// <summary>Removes the named override from the volume's profile.</summary>
        VolumeBindResult RemoveOverride(Volume volume, string overrideTypeName);

        /// <summary>Sets the override's <c>active</c> property.</summary>
        VolumeBindResult SetOverrideEnabled(Volume volume, string overrideTypeName, bool enabled);

        /// <summary>
        /// Writes <paramref name="value"/> to the named parameter on the named
        /// override, using Reflection. <paramref name="value"/>'s JSON shape MUST
        /// match the parameter's <c>VolumeParamSchema.TypeTag</c>.
        /// </summary>
        VolumeBindResult SetOverrideParam(Volume volume, string overrideTypeName, string paramName, JsonElement value);

        /// <summary>Sets <c>volume.enabled</c>.</summary>
        void SetVolumeEnabled(Volume volume, bool enabled);

        /// <summary>
        /// Destroys the volume's GameObject (and its profile asset) on the main thread.
        /// </summary>
        void DestroyLocalVolume(Volume volume);
    }
}
