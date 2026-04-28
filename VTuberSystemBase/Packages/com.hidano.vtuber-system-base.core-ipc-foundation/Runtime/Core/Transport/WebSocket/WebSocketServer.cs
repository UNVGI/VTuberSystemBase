#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed class WebSocketServer : IAsyncDisposable
    {
        private readonly ServerBindOptions _bindOptions;
        private readonly WebSocketServerOptions _serverOptions;
        private readonly Action<string>? _logInfo;
        private readonly Action<string>? _logWarning;
        private readonly Action<string>? _logError;

        private readonly object _sync = new();
        private readonly ConcurrentDictionary<Guid, ServerSideClientConnection> _clients = new();

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;
        private int _started;
        private int _stopped;
        private int _disposed;

        public event Action<IClientConnection>? ClientConnected;
        public event Action<IClientConnection>? ClientDisconnected;

        public WebSocketServer(
            ServerBindOptions bindOptions,
            WebSocketServerOptions? serverOptions = null,
            Action<string>? logInfo = null,
            Action<string>? logWarning = null,
            Action<string>? logError = null)
        {
            _bindOptions = bindOptions;
            _serverOptions = serverOptions ?? new WebSocketServerOptions();
            _logInfo = logInfo;
            _logWarning = logWarning;
            _logError = logError;
        }

        public bool IsRunning => Volatile.Read(ref _started) != 0 && Volatile.Read(ref _stopped) == 0;

        public int ConnectedClientCount => _clients.Count;

        public int BoundPort
        {
            get
            {
                lock (_sync)
                {
                    if (_listener == null) return 0;
                    if (_listener.LocalEndpoint is IPEndPoint ipe) return ipe.Port;
                    return 0;
                }
            }
        }

        public Task<IpcResult> StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_disposed != 0)
                {
                    throw new ObjectDisposedException(nameof(WebSocketServer));
                }
                if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                {
                    throw new InvalidOperationException(
                        "WebSocketServer has already been started.");
                }

                IPAddress address;
                if (!IPAddress.TryParse(_bindOptions.Host, out address!))
                {
                    address = IPAddress.Loopback;
                }

                var listener = new TcpListener(address, _bindOptions.Port);
                try
                {
                    listener.Server.SetSocketOption(
                        SocketOptionLevel.Socket,
                        SocketOptionName.ReuseAddress,
                        true);
                }
                catch (SocketException ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer: failed to set SO_REUSEADDR on listening socket: {ex.Message}");
                }

                try
                {
                    listener.Start();
                }
                catch (SocketException ex)
                {
                    Interlocked.Exchange(ref _started, 0);
                    try { listener.Stop(); } catch { /* swallow */ }
                    if (IsAddressInUse(ex))
                    {
                        _logWarning?.Invoke(
                            $"WebSocketServer: port {_bindOptions.Port} is already in use.");
                        return Task.FromResult(IpcResult.Fail(new CoreIpcError.PortInUse(_bindOptions.Port)));
                    }
                    _logError?.Invoke(
                        $"WebSocketServer: TcpListener.Start failed: {ex.Message}");
                    return Task.FromResult(
                        IpcResult.Fail(new CoreIpcError.TransportFailure(ex.Message)));
                }

                _listener = listener;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));

                _logInfo?.Invoke(
                    $"WebSocketServer: listening on {_bindOptions.Host}:{BoundPort}.");
                return Task.FromResult(IpcResult.Ok());
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            TcpListener? listener;
            Task? acceptTask;

            lock (_sync)
            {
                if (Interlocked.CompareExchange(ref _stopped, 1, 0) != 0)
                {
                    listener = null;
                    cts = null;
                    acceptTask = _acceptLoopTask;
                }
                else
                {
                    listener = _listener;
                    cts = _cts;
                    acceptTask = _acceptLoopTask;
                }
            }

            try
            {
                cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                listener?.Stop();
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke($"WebSocketServer: TcpListener.Stop threw: {ex.Message}");
            }

            if (acceptTask != null)
            {
                try
                {
                    await acceptTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer: accept loop terminated with error: {ex.Message}");
                }
            }

            await CloseAllClientsAsync().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            {
                return;
            }

            await StopAsync().ConfigureAwait(false);

            CancellationTokenSource? cts;
            lock (_sync)
            {
                cts = _cts;
                _cts = null;
            }

            cts?.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            var listener = _listener!;
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcpClient;
                try
                {
                    tcpClient = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException ex) when (ct.IsCancellationRequested)
                {
                    _logInfo?.Invoke(
                        $"WebSocketServer: accept loop stopped due to cancellation: {ex.Message}");
                    return;
                }
                catch (SocketException ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer: accept threw {ex.SocketErrorCode}: {ex.Message}");
                    continue;
                }
                catch (InvalidOperationException ex) when (ct.IsCancellationRequested)
                {
                    _logInfo?.Invoke(
                        $"WebSocketServer: accept loop stopped after listener.Stop: {ex.Message}");
                    return;
                }
                catch (Exception ex)
                {
                    _logError?.Invoke(
                        $"WebSocketServer: unexpected accept error: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => HandleAcceptedClientAsync(tcpClient, ct));
            }
        }

        private async Task HandleAcceptedClientAsync(TcpClient tcpClient, CancellationToken ct)
        {
            string remote = SafeRemoteEndpoint(tcpClient);

            if (_clients.Count >= _serverOptions.MaxConcurrentClients)
            {
                _logWarning?.Invoke(
                    $"WebSocketServer: rejecting client {remote}; "
                    + $"max concurrent clients ({_serverOptions.MaxConcurrentClients}) reached.");
                SafeCloseTcpClient(tcpClient);
                return;
            }

            NetworkStream? stream = null;
            try
            {
                tcpClient.NoDelay = true;
                stream = tcpClient.GetStream();

                bool handshakeOk =
                    await PerformHandshakeAsync(stream, remote, ct).ConfigureAwait(false);
                if (!handshakeOk)
                {
                    SafeDispose(stream);
                    SafeCloseTcpClient(tcpClient);
                    return;
                }

                var reader = new WebSocketFrameReader(
                    stream, requireMask: true,
                    maxMessagePayloadBytes: _serverOptions.MaxMessagePayloadBytes);
                var writer = new WebSocketFrameWriter(stream, maskOutgoing: false);

                var connection = new ServerSideClientConnection(
                    Guid.NewGuid(),
                    remote,
                    tcpClient,
                    stream,
                    reader,
                    writer,
                    _serverOptions,
                    OnClientClosed,
                    _logInfo,
                    _logWarning,
                    _logError);

                if (!_clients.TryAdd(connection.Id, connection))
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer: failed to register client {remote}; duplicate id.");
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                connection.StartReceiveAndHeartbeatLoops(ct);

                try
                {
                    ClientConnected?.Invoke(connection);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer: ClientConnected handler threw: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logError?.Invoke(
                    $"WebSocketServer: error while accepting client {remote}: {ex.Message}");
                SafeDispose(stream);
                SafeCloseTcpClient(tcpClient);
            }
        }

        private async Task<bool> PerformHandshakeAsync(
            NetworkStream stream,
            string remote,
            CancellationToken ct)
        {
            var processor = new HandshakeProcessor(_serverOptions.HandshakeMaxRequestBytes);
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(_serverOptions.HandshakeTimeout);

            try
            {
                var result =
                    await processor.ProcessAsync(stream, handshakeCts.Token).ConfigureAwait(false);
                if (result.Status == HandshakeStatus.Success)
                {
                    _logInfo?.Invoke(
                        $"WebSocketServer: handshake completed for {remote}.");
                    return true;
                }

                _logWarning?.Invoke(
                    $"WebSocketServer: handshake failed for {remote} ({result.Status}): "
                    + (result.FailureReason ?? "<no reason>"));
                return false;
            }
            catch (OperationCanceledException)
            {
                _logWarning?.Invoke(
                    $"WebSocketServer: handshake timed out or was cancelled for {remote}.");
                return false;
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke(
                    $"WebSocketServer: handshake threw for {remote}: {ex.Message}");
                return false;
            }
        }

        private void OnClientClosed(ServerSideClientConnection connection)
        {
            if (_clients.TryRemove(connection.Id, out _))
            {
                try
                {
                    ClientDisconnected?.Invoke(connection);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer: ClientDisconnected handler threw: {ex.Message}");
                }
            }
        }

        private async Task CloseAllClientsAsync()
        {
            var snapshot = new List<ServerSideClientConnection>(_clients.Values);
            foreach (var c in snapshot)
            {
                try
                {
                    await c.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer: error disposing client during stop: {ex.Message}");
                }
            }
        }

        private static bool IsAddressInUse(SocketException ex)
        {
            return ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                || ex.SocketErrorCode == SocketError.AccessDenied;
        }

        private static string SafeRemoteEndpoint(TcpClient client)
        {
            try
            {
                return client.Client.RemoteEndPoint?.ToString() ?? "(unknown)";
            }
            catch
            {
                return "(unknown)";
            }
        }

        private static void SafeDispose(IDisposable? disposable)
        {
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch { /* swallow */ }
        }

        private static void SafeCloseTcpClient(TcpClient client)
        {
            try { client.Close(); }
            catch { /* swallow */ }
        }

        internal sealed class ServerSideClientConnection : IClientConnection
        {
            private readonly TcpClient _tcpClient;
            private readonly NetworkStream _stream;
            private readonly WebSocketFrameReader _reader;
            private readonly WebSocketFrameWriter _writer;
            private readonly WebSocketServerOptions _options;
            private readonly Action<ServerSideClientConnection> _onClosed;
            private readonly Action<string>? _logInfo;
            private readonly Action<string>? _logWarning;
            private readonly Action<string>? _logError;

            private readonly InboundQueue _inbound = new();
            private readonly CancellationTokenSource _cts = new();

            private Task? _receiveLoopTask;
            private Task? _heartbeatTask;
            private long _lastInboundUtcTicks;
            private int _closeInitiated;
            private int _disposed;

            public Guid Id { get; }
            public string RemoteEndpoint { get; }

            public ServerSideClientConnection(
                Guid id,
                string remoteEndpoint,
                TcpClient tcpClient,
                NetworkStream stream,
                WebSocketFrameReader reader,
                WebSocketFrameWriter writer,
                WebSocketServerOptions options,
                Action<ServerSideClientConnection> onClosed,
                Action<string>? logInfo,
                Action<string>? logWarning,
                Action<string>? logError)
            {
                Id = id;
                RemoteEndpoint = remoteEndpoint;
                _tcpClient = tcpClient;
                _stream = stream;
                _reader = reader;
                _writer = writer;
                _options = options;
                _onClosed = onClosed;
                _logInfo = logInfo;
                _logWarning = logWarning;
                _logError = logError;
                _lastInboundUtcTicks = DateTime.UtcNow.Ticks;
            }

            public void StartReceiveAndHeartbeatLoops(CancellationToken serverToken)
            {
                var combined = CancellationTokenSource.CreateLinkedTokenSource(serverToken, _cts.Token);
                var token = combined.Token;
                _receiveLoopTask = Task.Run(async () =>
                {
                    try { await ReceiveLoopAsync(token).ConfigureAwait(false); }
                    finally
                    {
                        combined.Dispose();
                        _inbound.Complete();
                        _onClosed(this);
                    }
                });

                if (_options.PingInterval > TimeSpan.Zero)
                {
                    _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(token));
                }
            }

            public async ValueTask SendAsync(
                ReadOnlyMemory<byte> textFramePayload,
                CancellationToken cancellationToken)
            {
                if (_disposed != 0)
                {
                    throw new ObjectDisposedException(nameof(ServerSideClientConnection));
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cts.Token);
                try
                {
                    await _writer.WriteFrameAsync(
                        true, WebSocketOpcode.Text, textFramePayload, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer client {RemoteEndpoint}: send failed: {ex.Message}");
                    throw;
                }
            }

            public IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(
                CancellationToken cancellationToken)
            {
                return _inbound.ReadAllAsync(cancellationToken);
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _closeInitiated, 1, 0) == 0)
                {
                    using var initiateCloseCts = new CancellationTokenSource(_options.CloseTimeout);
                    await TryWriteCloseAsync(
                        WebSocketCloseCode.NormalClosure, null, initiateCloseCts.Token)
                        .ConfigureAwait(false);
                }

                try { _cts.Cancel(); } catch (ObjectDisposedException) { }

                if (_receiveLoopTask != null)
                {
                    try
                    {
                        await _receiveLoopTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke(
                            $"WebSocketServer client {RemoteEndpoint}: receive loop ended with error: {ex.Message}");
                    }
                }

                if (_heartbeatTask != null)
                {
                    try
                    {
                        await _heartbeatTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke(
                            $"WebSocketServer client {RemoteEndpoint}: heartbeat loop ended with error: {ex.Message}");
                    }
                }

                SafeDispose(_writer);
                SafeDispose(_stream);
                SafeCloseTcpClient(_tcpClient);
                _cts.Dispose();
            }

            private async Task ReceiveLoopAsync(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        WebSocketReadResult result;
                        try
                        {
                            result = await _reader.ReadMessageAsync(ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (IOException ex)
                        {
                            _logInfo?.Invoke(
                                $"WebSocketServer client {RemoteEndpoint}: read failed: {ex.Message}");
                            return;
                        }
                        catch (ObjectDisposedException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logWarning?.Invoke(
                                $"WebSocketServer client {RemoteEndpoint}: unexpected read error: {ex.Message}");
                            return;
                        }

                        Volatile.Write(ref _lastInboundUtcTicks, DateTime.UtcNow.Ticks);

                        switch (result.Status)
                        {
                            case WebSocketReadStatus.Frame:
                            {
                                var frame = result.Frame;
                                if (frame.Opcode == WebSocketOpcode.Text)
                                {
                                    _inbound.Enqueue(frame.Payload);
                                }
                                else if (frame.Opcode == WebSocketOpcode.Binary)
                                {
                                    _logWarning?.Invoke(
                                        $"WebSocketServer client {RemoteEndpoint}: binary frames are not supported; closing.");
                                    await TryWriteCloseAsync(WebSocketCloseCode.UnsupportedData, "binary not supported", ct)
                                        .ConfigureAwait(false);
                                    return;
                                }
                                else if (frame.Opcode == WebSocketOpcode.Ping)
                                {
                                    await TryWritePongAsync(frame.Payload, ct).ConfigureAwait(false);
                                }
                                else if (frame.Opcode == WebSocketOpcode.Pong)
                                {
                                    // Pong updates _lastInboundTicks above; nothing else to do.
                                }
                                break;
                            }
                            case WebSocketReadStatus.Close:
                            {
                                await TryWriteCloseAsync(
                                    result.CloseCode ?? WebSocketCloseCode.NormalClosure,
                                    null, ct).ConfigureAwait(false);
                                return;
                            }
                            case WebSocketReadStatus.EndOfStream:
                            {
                                _logInfo?.Invoke(
                                    $"WebSocketServer client {RemoteEndpoint}: peer closed the connection.");
                                return;
                            }
                            case WebSocketReadStatus.MessageTooBig:
                            {
                                await TryWriteCloseAsync(WebSocketCloseCode.MessageTooBig, null, ct)
                                    .ConfigureAwait(false);
                                return;
                            }
                            case WebSocketReadStatus.InvalidUtf8:
                            {
                                await TryWriteCloseAsync(WebSocketCloseCode.InvalidFramePayloadData, null, ct)
                                    .ConfigureAwait(false);
                                return;
                            }
                            case WebSocketReadStatus.MaskRequired:
                            case WebSocketReadStatus.MaskForbidden:
                            case WebSocketReadStatus.ProtocolError:
                            {
                                await TryWriteCloseAsync(WebSocketCloseCode.ProtocolError,
                                    result.ErrorMessage, ct).ConfigureAwait(false);
                                return;
                            }
                        }
                    }
                }
                finally
                {
                    // Close socket gracefully (best-effort).
                    try { _stream.Close(); } catch { /* swallow */ }
                    try { _tcpClient.Close(); } catch { /* swallow */ }
                }
            }

            private async Task HeartbeatLoopAsync(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(_options.PingInterval, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        long nowTicks = DateTime.UtcNow.Ticks;
                        long lastInboundTicks = Volatile.Read(ref _lastInboundUtcTicks);
                        var sincePeer = TimeSpan.FromTicks(nowTicks - lastInboundTicks);

                        if (sincePeer > _options.PongTimeout)
                        {
                            _logWarning?.Invoke(
                                $"WebSocketServer client {RemoteEndpoint}: pong timeout "
                                + $"({(long)sincePeer.TotalMilliseconds} ms); closing.");
                            await TryWriteCloseAsync(WebSocketCloseCode.PolicyViolation, "pong timeout", ct)
                                .ConfigureAwait(false);
                            try { _stream.Close(); } catch { /* swallow */ }
                            return;
                        }

                        await TryWritePingAsync(ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketServer client {RemoteEndpoint}: heartbeat loop terminated: {ex.Message}");
                }
            }

            private async Task TryWriteCloseAsync(
                WebSocketCloseCode code,
                string? reason,
                CancellationToken ct)
            {
                Interlocked.Exchange(ref _closeInitiated, 1);
                using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                try { closeCts.CancelAfter(_options.CloseTimeout); }
                catch (ObjectDisposedException) { }

                try
                {
                    await _writer.WriteCloseAsync(code, reason, closeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception ex)
                {
                    _logInfo?.Invoke(
                        $"WebSocketServer client {RemoteEndpoint}: close write failed: {ex.Message}");
                }
            }

            private async Task TryWritePingAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
            {
                try
                {
                    await _writer.WritePingAsync(payload, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logInfo?.Invoke(
                        $"WebSocketServer client {RemoteEndpoint}: ping write failed: {ex.Message}");
                }
            }

            private async Task TryWritePongAsync(byte[] payload, CancellationToken ct)
            {
                try
                {
                    await _writer.WritePongAsync(payload, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logInfo?.Invoke(
                        $"WebSocketServer client {RemoteEndpoint}: pong write failed: {ex.Message}");
                }
            }
        }

        private sealed class InboundQueue
        {
            private readonly object _lock = new();
            private readonly Queue<byte[]> _items = new();
            private TaskCompletionSource<bool>? _waiter;
            private bool _completed;

            public void Enqueue(byte[] item)
            {
                TaskCompletionSource<bool>? toComplete = null;
                lock (_lock)
                {
                    if (_completed) return;
                    _items.Enqueue(item);
                    toComplete = _waiter;
                    _waiter = null;
                }
                toComplete?.TrySetResult(true);
            }

            public void Complete()
            {
                TaskCompletionSource<bool>? toComplete = null;
                lock (_lock)
                {
                    if (_completed) return;
                    _completed = true;
                    toComplete = _waiter;
                    _waiter = null;
                }
                toComplete?.TrySetResult(true);
            }

            public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (true)
                {
                    byte[]? next = null;
                    Task waitTask;
                    lock (_lock)
                    {
                        if (_items.Count > 0)
                        {
                            next = _items.Dequeue();
                            waitTask = Task.CompletedTask;
                        }
                        else if (_completed)
                        {
                            yield break;
                        }
                        else
                        {
                            _waiter ??= new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously);
                            waitTask = _waiter.Task;
                        }
                    }

                    if (next != null)
                    {
                        yield return next;
                        continue;
                    }

                    using (cancellationToken.Register(() =>
                    {
                        TaskCompletionSource<bool>? w;
                        lock (_lock)
                        {
                            w = _waiter;
                            _waiter = null;
                        }
                        w?.TrySetCanceled();
                    }))
                    {
                        try
                        {
                            await waitTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            yield break;
                        }
                    }
                }
            }
        }
    }
}
