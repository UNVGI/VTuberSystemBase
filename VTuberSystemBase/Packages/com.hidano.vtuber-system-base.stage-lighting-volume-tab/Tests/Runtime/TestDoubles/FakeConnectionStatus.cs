#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IConnectionStatus"/> double. Tests drive transitions via
    /// <see cref="SetStatus"/>; subscribers receive a <see cref="ConnectionStatusEvent"/>
    /// for every change. (Task 1.2, Requirement 12.1)
    /// </summary>
    public sealed class FakeConnectionStatus : IConnectionStatus
    {
        public FakeConnectionStatus(ConnectionStatusCode initial = ConnectionStatusCode.Connected)
        {
            CurrentStatus = initial;
        }

        public bool IsConnected => CurrentStatus == ConnectionStatusCode.Connected;

        public ConnectionStatusCode CurrentStatus { get; private set; }

        public event Action<ConnectionStatusEvent>? OnStatusChanged;

        public void SetStatus(ConnectionStatusCode next, string? detail = null)
        {
            if (next == CurrentStatus) return;
            var prev = CurrentStatus;
            CurrentStatus = next;
            OnStatusChanged?.Invoke(new ConnectionStatusEvent(prev, next, DateTimeOffset.UtcNow, detail));
        }
    }
}
