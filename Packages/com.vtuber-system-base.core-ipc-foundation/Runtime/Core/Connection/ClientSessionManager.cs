#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Connection
{
    public sealed class ClientSessionManager : IAsyncDisposable
    {
        private readonly ITransportAdapter _transport;
        private readonly ClientBindOptions _bindOptions;
        private readonly ConnectionStateMachine _stateMachine;
        private readonly ReconnectBackoff _backoff;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Action<ReadOnlyMemory<byte>>? _onMessageReceived;
        private readonly Action<string>? _logWarning;
        private readonly Action<string>? _logError;

        private readonly object _sync = new();
        private CancellationTokenSource? _cts;
        private Task? _runLoopTask;
        private IClientConnection? _currentConnection;
        private bool _shutdownRequested;
        private bool _disposed;

        public ClientSessionManager(
            ITransportAdapter transport,
            ClientBindOptions bindOptions,
            ConnectionStateMachine stateMachine,
            ReconnectBackoff backoff,
            Func<TimeSpan, CancellationToken, Task>? delay = null,
            Action<ReadOnlyMemory<byte>>? onMessageReceived = null,
            Action<string>? logWarning = null,
            Action<string>? logError = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _backoff = backoff ?? throw new ArgumentNullException(nameof(backoff));
            _bindOptions = bindOptions;
            _delay = delay ?? Task.Delay;
            _onMessageReceived = onMessageReceived;
            _logWarning = logWarning;
            _logError = logError;
        }

        public IClientConnection? CurrentConnection
        {
            get
            {
                lock (_sync) return _currentConnection;
            }
        }

        public bool IsShutdownRequested
        {
            get
            {
                lock (_sync) return _shutdownRequested;
            }
        }

        public Task RunLoopTask
        {
            get
            {
                lock (_sync) return _runLoopTask ?? Task.CompletedTask;
            }
        }

        public Task StartAsync(CancellationToken externalToken = default)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ClientSessionManager));
                }

                if (_runLoopTask != null)
                {
                    throw new InvalidOperationException(
                        "ClientSessionManager has already been started.");
                }

                _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
                var token = _cts.Token;
                _runLoopTask = Task.Run(() => RunAsync(token));
            }

            return Task.CompletedTask;
        }

        public async Task ShutdownAsync()
        {
            CancellationTokenSource? cts;
            Task? runLoopTask;
            IClientConnection? connection;

            lock (_sync)
            {
                if (_shutdownRequested)
                {
                    runLoopTask = _runLoopTask;
                    cts = null;
                    connection = null;
                }
                else
                {
                    _shutdownRequested = true;
                    cts = _cts;
                    runLoopTask = _runLoopTask;
                    connection = _currentConnection;
                }
            }

            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (connection != null)
            {
                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"ClientSessionManager: error disposing connection during shutdown: "
                        + ex.Message);
                }
            }

            if (runLoopTask != null)
            {
                try
                {
                    await runLoopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"ClientSessionManager: run loop ended with error: " + ex.Message);
                }
            }

            _stateMachine.TryTransition(ConnectionState.Disconnected, isShutdown: true);
        }

        public async ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }

            await ShutdownAsync().ConfigureAwait(false);

            CancellationTokenSource? cts;
            lock (_sync)
            {
                cts = _cts;
                _cts = null;
            }

            cts?.Dispose();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                if (!await PerformInitialConnectAsync(ct).ConfigureAwait(false))
                {
                    return;
                }

                await ReceiveAndReconnectLoopAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logError?.Invoke(
                    $"ClientSessionManager: run loop terminated unexpectedly: " + ex);
            }
        }

        private async Task<bool> PerformInitialConnectAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested || IsShutdownRequested)
            {
                return false;
            }

            _stateMachine.TryTransition(ConnectionState.Connecting);

            while (!ct.IsCancellationRequested && !IsShutdownRequested)
            {
                var connection = await TryConnectAsync(ct).ConfigureAwait(false);
                if (connection != null)
                {
                    SetCurrentConnection(connection);
                    _backoff.Reset();
                    _stateMachine.TryTransition(ConnectionState.Connected);
                    return true;
                }

                if (ct.IsCancellationRequested || IsShutdownRequested)
                {
                    return false;
                }

                if (_backoff.ExceededMaxAttempts)
                {
                    _logError?.Invoke(
                        "ClientSessionManager: initial connect attempts exhausted; giving up. "
                        + "PermanentlyDisconnected is only reachable from Reconnecting "
                        + "(after at least one successful Connected).");
                    return false;
                }

                var delay = _backoff.NextDelay();

                try
                {
                    await _delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }

                if (ct.IsCancellationRequested || IsShutdownRequested)
                {
                    return false;
                }

                if (_stateMachine.CurrentState == ConnectionState.Connecting)
                {
                    _stateMachine.TryTransition(ConnectionState.Disconnected);
                }

                _stateMachine.TryTransition(ConnectionState.Connecting);
            }

            return false;
        }

        private async Task ReceiveAndReconnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsShutdownRequested)
            {
                IClientConnection? connection;
                lock (_sync) connection = _currentConnection;
                if (connection == null) break;

                try
                {
                    await ReceiveLoopAsync(connection, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        "ClientSessionManager: receive loop terminated with error: "
                        + ex.Message);
                }

                try
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        "ClientSessionManager: error disposing dropped connection: "
                        + ex.Message);
                }

                ClearCurrentConnection();

                if (ct.IsCancellationRequested || IsShutdownRequested)
                {
                    return;
                }

                _stateMachine.TryTransition(ConnectionState.Reconnecting);

                if (!await TryReconnectAsync(ct).ConfigureAwait(false))
                {
                    return;
                }
            }
        }

        private async Task ReceiveLoopAsync(
            IClientConnection connection,
            CancellationToken ct)
        {
            await foreach (var frame in connection.ReceiveAsync(ct).WithCancellation(ct))
            {
                try
                {
                    _onMessageReceived?.Invoke(frame);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        "ClientSessionManager: onMessageReceived handler threw: "
                        + ex.Message);
                }
            }
        }

        private async Task<bool> TryReconnectAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !IsShutdownRequested)
            {
                if (_backoff.ExceededMaxAttempts)
                {
                    _stateMachine.TryTransition(ConnectionState.PermanentlyDisconnected);
                    return false;
                }

                var delay = _backoff.NextDelay();

                try
                {
                    await _delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }

                if (ct.IsCancellationRequested || IsShutdownRequested)
                {
                    return false;
                }

                var connection = await TryConnectAsync(ct).ConfigureAwait(false);
                if (connection == null)
                {
                    continue;
                }

                _stateMachine.TryTransition(ConnectionState.Connecting);
                SetCurrentConnection(connection);
                _backoff.Reset();
                _stateMachine.TryTransition(ConnectionState.Connected);
                return true;
            }

            return false;
        }

        private async Task<IClientConnection?> TryConnectAsync(CancellationToken ct)
        {
            try
            {
                var connection = await _transport
                    .ConnectClientAsync(_bindOptions, ct)
                    .ConfigureAwait(false);
                return connection;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke(
                    "ClientSessionManager: connect attempt failed: " + ex.Message);
                return null;
            }
        }

        private void SetCurrentConnection(IClientConnection connection)
        {
            lock (_sync) _currentConnection = connection;
        }

        private void ClearCurrentConnection()
        {
            lock (_sync) _currentConnection = null;
        }
    }
}
