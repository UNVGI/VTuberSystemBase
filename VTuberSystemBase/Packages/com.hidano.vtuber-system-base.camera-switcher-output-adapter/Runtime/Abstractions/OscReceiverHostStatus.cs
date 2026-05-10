#nullable enable

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Lifecycle phase of the OSC receiver host (<see cref="IOscReceiverHost"/>).
    /// </summary>
    /// <remarks>
    /// Transitions:
    /// <c>Stopped</c> →(<see cref="IOscReceiverHost.StartAsync"/>)→ <c>Starting</c> →(server bound)→
    /// <c>Running</c> →(<see cref="IOscReceiverHost.StopAsync"/>)→ <c>Stopped</c>; or
    /// <c>Starting</c> →(bind failure)→ <c>Failed</c>. Restart from <c>Failed</c> is allowed via
    /// <see cref="IOscReceiverHost.StartAsync"/> after a successful <see cref="IOscReceiverHost.StopAsync"/>.
    /// </remarks>
    public enum OscReceiverHostStatus
    {
        Stopped = 0,
        Starting = 1,
        Running = 2,
        Failed = 3,
    }
}
