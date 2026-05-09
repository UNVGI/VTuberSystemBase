#nullable enable

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Lifecycle state of <c>CameraSwitcherCoordinator</c>. Monotonic from
    /// <see cref="Initializing"/> until <see cref="Disposing"/>; <see cref="Suspended"/>
    /// is reachable from <see cref="Ready"/> and may transition back.
    /// </summary>
    public enum TabStatus
    {
        Initializing = 0,
        ConnectionPending,
        Ready,
        Suspended,
        Disposing,
    }
}
