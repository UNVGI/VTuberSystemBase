#nullable enable
using UnityEngine;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Osc
{
    /// <summary>
    /// Marker MonoBehaviour attached to the GameObject that hosts <c>uOSC.uOscServer</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The adapter creates this GameObject lazily inside
    /// <see cref="UoscReceiverHostAdapter.StartAsync"/> and destroys it from
    /// <see cref="UoscReceiverHostAdapter.StopAsync"/> / <c>Dispose</c>. The script
    /// itself contains no logic — its sole purpose is to give the host GameObject
    /// a recognisable type for inspector debugging and to participate in the
    /// adapter's <see cref="HideFlags.HideAndDontSave"/> lifecycle hardening.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CameraOscReceiverHost : MonoBehaviour
    {
        public const string DefaultGameObjectName = "[CameraSwitcherOutputAdapter.OscReceiver]";
    }
}
