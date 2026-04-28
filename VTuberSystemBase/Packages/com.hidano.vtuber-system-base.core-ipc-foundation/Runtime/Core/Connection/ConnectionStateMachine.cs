#nullable enable
using System;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Connection
{
    public sealed class ConnectionStateMachine
    {
        private readonly object _sync = new();
        private readonly Action<string>? _logWarning;
        private ConnectionState _state = ConnectionState.Disconnected;
        private bool _shutdownRequested;

        public ConnectionStateMachine()
            : this(null)
        {
        }

        public ConnectionStateMachine(Action<string>? logWarning)
        {
            _logWarning = logWarning;
        }

        public ConnectionState CurrentState
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        public bool ShutdownRequested
        {
            get
            {
                lock (_sync)
                {
                    return _shutdownRequested;
                }
            }
        }

        public event Action<ConnectionState, ConnectionState>? StateChanged;

        public bool TryTransition(ConnectionState target, bool isShutdown = false)
        {
            ConnectionState previous;

            lock (_sync)
            {
                previous = _state;

                if (isShutdown && target != ConnectionState.Disconnected)
                {
                    _logWarning?.Invoke(
                        $"ConnectionStateMachine: shutdown transition must target Disconnected, but got {target}; ignored.");
                    return false;
                }

                if (previous == target)
                {
                    if (isShutdown && !_shutdownRequested)
                    {
                        _shutdownRequested = true;
                    }
                    return false;
                }

                if (target == ConnectionState.Connecting && _shutdownRequested)
                {
                    _logWarning?.Invoke(
                        "ConnectionStateMachine: cannot transition to Connecting after shutdown was requested.");
                    return false;
                }

                if (!IsValidTransition(previous, target))
                {
                    _logWarning?.Invoke(
                        $"ConnectionStateMachine: invalid transition {previous} -> {target}; ignored.");
                    return false;
                }

                _state = target;

                if (isShutdown && !_shutdownRequested)
                {
                    _shutdownRequested = true;
                }
            }

            StateChanged?.Invoke(previous, target);
            return true;
        }

        private static bool IsValidTransition(ConnectionState from, ConnectionState to)
        {
            switch (from)
            {
                case ConnectionState.Disconnected:
                    return to == ConnectionState.Connecting;
                case ConnectionState.Connecting:
                    return to == ConnectionState.Connected
                        || to == ConnectionState.Disconnected;
                case ConnectionState.Connected:
                    return to == ConnectionState.Reconnecting
                        || to == ConnectionState.Disconnected;
                case ConnectionState.Reconnecting:
                    return to == ConnectionState.Connecting
                        || to == ConnectionState.PermanentlyDisconnected
                        || to == ConnectionState.Disconnected;
                case ConnectionState.PermanentlyDisconnected:
                    return false;
                default:
                    return false;
            }
        }
    }
}
