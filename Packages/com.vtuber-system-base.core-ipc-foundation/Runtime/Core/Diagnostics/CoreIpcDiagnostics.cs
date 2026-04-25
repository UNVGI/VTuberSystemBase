#nullable enable
using System;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Connection;

namespace VTuberSystemBase.CoreIpc.Core.Diagnostics
{
    public sealed class CoreIpcDiagnostics : IConnectionDiagnostics, IDisposable
    {
        private static readonly Func<int> ZeroProvider = () => 0;

        private readonly ConnectionStateMachine _stateMachine;
        private readonly Func<int> _reconnectAttemptCountProvider;
        private readonly Func<int> _pendingRequestCountProvider;
        private readonly Func<int> _stateSlotCountProvider;
        private readonly Func<int> _eventQueueCountProvider;
        private readonly Func<int> _connectedClientCountProvider;
        private readonly Func<DateTimeOffset> _nowProvider;
        private int _disposed;

        public CoreIpcDiagnostics(ConnectionStateMachine stateMachine)
            : this(stateMachine, null, null, null, null, null, null)
        {
        }

        public CoreIpcDiagnostics(
            ConnectionStateMachine stateMachine,
            Func<int>? reconnectAttemptCountProvider = null,
            Func<int>? pendingRequestCountProvider = null,
            Func<int>? stateSlotCountProvider = null,
            Func<int>? eventQueueCountProvider = null,
            Func<int>? connectedClientCountProvider = null,
            Func<DateTimeOffset>? nowProvider = null)
        {
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _reconnectAttemptCountProvider = reconnectAttemptCountProvider ?? ZeroProvider;
            _pendingRequestCountProvider = pendingRequestCountProvider ?? ZeroProvider;
            _stateSlotCountProvider = stateSlotCountProvider ?? ZeroProvider;
            _eventQueueCountProvider = eventQueueCountProvider ?? ZeroProvider;
            _connectedClientCountProvider = connectedClientCountProvider ?? ZeroProvider;
            _nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);

            _stateMachine.StateChanged += OnStateMachineStateChanged;
        }

        public ConnectionState CurrentState => _stateMachine.CurrentState;

        public int ReconnectAttemptCount => _reconnectAttemptCountProvider();

        public int PendingRequestCount => _pendingRequestCountProvider();

        public int StateSlotCount => _stateSlotCountProvider();

        public int EventQueueCount => _eventQueueCountProvider();

        public int ConnectedClientCount => _connectedClientCountProvider();

        public event Action<ConnectionState, ConnectionState>? ConnectionStateChanged;

        public DiagnosticsSnapshot TakeSnapshot()
        {
            var clientState = _stateMachine.CurrentState;
            var serverConnectedCount = _connectedClientCountProvider();
            var reconnectAttemptCount = _reconnectAttemptCountProvider();
            var pendingRequestCount = _pendingRequestCountProvider();
            var stateSlotCount = _stateSlotCountProvider();
            var eventQueueCount = _eventQueueCountProvider();
            var takenAt = _nowProvider();

            return new DiagnosticsSnapshot(
                TakenAt: takenAt,
                ClientState: clientState,
                ServerConnectedCount: serverConnectedCount,
                ReconnectAttemptCount: reconnectAttemptCount,
                PendingRequestCount: pendingRequestCount,
                StateSlotCount: stateSlotCount,
                EventQueueCount: eventQueueCount);
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _stateMachine.StateChanged -= OnStateMachineStateChanged;
        }

        private void OnStateMachineStateChanged(ConnectionState previous, ConnectionState current)
        {
            ConnectionStateChanged?.Invoke(previous, current);
        }
    }
}
