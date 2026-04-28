#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed class WebSocketTransportAdapter : ITransportAdapter
    {
        private readonly IMessageCodec _codec;
        private readonly WebSocketServerOptions? _serverOptions;
        private readonly WebSocketClientOptions? _clientOptions;
        private readonly Action<string>? _logInfo;
        private readonly Action<string>? _logWarning;
        private readonly Action<string>? _logError;

        private readonly object _sync = new();
        private readonly ConcurrentDictionary<WebSocketClient, byte> _clients = new();

        private WebSocketServer? _server;
        private int _serverStarted;
        private int _disposed;

        public event Action<IClientConnection>? ClientConnected;
        public event Action<IClientConnection>? ClientDisconnected;

        public WebSocketTransportAdapter(
            IMessageCodec codec,
            WebSocketServerOptions? serverOptions = null,
            WebSocketClientOptions? clientOptions = null,
            Action<string>? logInfo = null,
            Action<string>? logWarning = null,
            Action<string>? logError = null)
        {
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _serverOptions = serverOptions;
            _clientOptions = clientOptions;
            _logInfo = logInfo;
            _logWarning = logWarning;
            _logError = logError;
        }

        public IMessageCodec Codec => _codec;

        public bool IsServerRunning
        {
            get
            {
                lock (_sync)
                {
                    return _server?.IsRunning ?? false;
                }
            }
        }

        public int BoundPort
        {
            get
            {
                lock (_sync)
                {
                    return _server?.BoundPort ?? 0;
                }
            }
        }

        public int ConnectedClientCount
        {
            get
            {
                lock (_sync)
                {
                    return _server?.ConnectedClientCount ?? 0;
                }
            }
        }

        public async Task StartServerAsync(
            ServerBindOptions options,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (Interlocked.CompareExchange(ref _serverStarted, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "WebSocketTransportAdapter: server has already been started.");
            }

            WebSocketServer server;
            lock (_sync)
            {
                server = new WebSocketServer(
                    options, _serverOptions, _logInfo, _logWarning, _logError);
                _server = server;
            }

            server.ClientConnected += OnServerClientConnected;
            server.ClientDisconnected += OnServerClientDisconnected;

            IpcResult startResult;
            try
            {
                startResult = await server.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await TearDownFailedServerAsync(server).ConfigureAwait(false);
                throw;
            }

            if (!startResult.Success)
            {
                await TearDownFailedServerAsync(server).ConfigureAwait(false);
                throw new CoreIpcTransportException(startResult.Error!);
            }
        }

        public async Task<IClientConnection> ConnectClientAsync(
            ClientBindOptions options,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var client = new WebSocketClient(
                options, _clientOptions, _logInfo, _logWarning, _logError);

            IpcResult connectResult;
            try
            {
                connectResult = await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await SafeDisposeClientAsync(client).ConfigureAwait(false);
                throw;
            }

            if (!connectResult.Success)
            {
                await SafeDisposeClientAsync(client).ConfigureAwait(false);
                throw new CoreIpcTransportException(connectResult.Error!);
            }

            _clients.TryAdd(client, 0);
            client.Disconnected += _ =>
            {
                _clients.TryRemove(client, out byte _);
            };
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            {
                return;
            }

            WebSocketServer? server;
            lock (_sync)
            {
                server = _server;
                _server = null;
            }

            if (server != null)
            {
                server.ClientConnected -= OnServerClientConnected;
                server.ClientDisconnected -= OnServerClientDisconnected;
                try
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketTransportAdapter: server dispose threw: {ex.Message}");
                }
            }

            foreach (var client in _clients.Keys)
            {
                await SafeDisposeClientAsync(client).ConfigureAwait(false);
            }
            _clients.Clear();
        }

        private async Task TearDownFailedServerAsync(WebSocketServer server)
        {
            server.ClientConnected -= OnServerClientConnected;
            server.ClientDisconnected -= OnServerClientDisconnected;
            lock (_sync)
            {
                if (ReferenceEquals(_server, server))
                {
                    _server = null;
                }
            }
            Interlocked.Exchange(ref _serverStarted, 0);

            try
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke(
                    $"WebSocketTransportAdapter: failed-server dispose threw: {ex.Message}");
            }
        }

        private async ValueTask SafeDisposeClientAsync(WebSocketClient client)
        {
            try
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke(
                    $"WebSocketTransportAdapter: client dispose threw: {ex.Message}");
            }
        }

        private void OnServerClientConnected(IClientConnection connection)
        {
            try
            {
                ClientConnected?.Invoke(connection);
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke(
                    $"WebSocketTransportAdapter: ClientConnected handler threw: {ex.Message}");
            }
        }

        private void OnServerClientDisconnected(IClientConnection connection)
        {
            try
            {
                ClientDisconnected?.Invoke(connection);
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke(
                    $"WebSocketTransportAdapter: ClientDisconnected handler threw: {ex.Message}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(WebSocketTransportAdapter));
            }
        }
    }
}
