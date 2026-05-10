#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Domain event raised whenever <see cref="IConnectionStatus.CurrentStatus"/> transitions to a new
    /// <see cref="ConnectionStatusCode"/>. Carries both endpoints of the transition, the wall-clock
    /// instant of dispatch, and an optional human-readable detail string for diagnostic surfaces.
    /// See design.md §Commands §IConnectionStatus.
    /// </summary>
    public readonly struct ConnectionStatusEvent
    {
        public ConnectionStatusEvent(ConnectionStatusCode from, ConnectionStatusCode to, DateTimeOffset at, string? detail = null)
        {
            From = from;
            To = to;
            At = at;
            Detail = detail;
        }

        public ConnectionStatusCode From { get; }
        public ConnectionStatusCode To { get; }
        public DateTimeOffset At { get; }
        public string? Detail { get; }
    }
}
