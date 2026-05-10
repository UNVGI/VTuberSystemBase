#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="IConnectionStatus"/>. Tests transition the state
    /// via <see cref="SetStatus"/>; observers receive a synthetic
    /// <see cref="ConnectionStatusEvent"/>.
    /// </summary>
    public sealed class FakeConnectionStatus : IConnectionStatus
    {
        private ConnectionStatusCode _status;

        public FakeConnectionStatus(ConnectionStatusCode initial = ConnectionStatusCode.Disconnected)
        {
            _status = initial;
        }

        public bool IsConnected => _status == ConnectionStatusCode.Connected;

        public ConnectionStatusCode CurrentStatus => _status;

        public event Action<ConnectionStatusEvent>? OnStatusChanged;

        public void SetStatus(ConnectionStatusCode to, string? detail = null)
        {
            var from = _status;
            if (from == to) return;
            _status = to;
            OnStatusChanged?.Invoke(new ConnectionStatusEvent(from, to, DateTimeOffset.UtcNow, detail));
        }
    }
}
