#nullable enable
using System.Text.Json;
using UnityEngine.Rendering;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume
{
    /// <summary>
    /// Internal hook for writing <c>VolumeParameter&lt;T&gt;</c> values via Reflection.
    /// Decoupled from <see cref="GlobalEnabledLocalVolumeBinder"/> so the binder can
    /// be unit-tested without exercising the full Reflection writer (Task 2.6).
    /// </summary>
    public interface IVolumeParameterValueWriter
    {
        /// <summary>Writes <paramref name="value"/> into the named parameter on
        /// <paramref name="component"/>.</summary>
        /// <returns>A success / failure result with structured detail.</returns>
        Abstractions.VolumeBindResult Write(VolumeComponent component, string paramName, JsonElement value);
    }
}
