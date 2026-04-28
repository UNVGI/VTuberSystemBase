#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed class WebSocketClient : IClientConnection
    {
        private readonly ClientBindOptions _bindOptions;
        private readonly WebSocketClientOptions _clientOptions;
        private readonly Action<string>? _logInfo;
        private readonly Action<string>? _logWarning;
        private readonly Action<string>? _logError;

        private readonly object _sync = new();
        private readonly InboundQueue _inbound = new();
        private readonly CancellationTokenSource _lifetimeCts = new();

        private ClientWebSocket? _socket;
        private Task? _receiveLoopTask;
        private string _remoteEndpoint = string.Empty;
        private int _connectStarted;
        private int _connected;
        private int _disposed;
        private int _disconnectNotified;

        public WebSocketClient(
            ClientBindOptions bindOptions,
            WebSocketClientOptions? clientOptions = null,
            Action<string>? logInfo = null,
            Action<string>? logWarning = null,
            Action<string>? logError = null)
        {
            _bindOptions = bindOptions;
            _clientOptions = clientOptions ?? new WebSocketClientOptions();
            _logInfo = logInfo;
            _logWarning = logWarning;
            _logError = logError;
        }

        public string RemoteEndpoint => _remoteEndpoint;

        public bool IsConnected
        {
            get
            {
                if (Volatile.Read(ref _disposed) != 0) return false;
                return Volatile.Read(ref _connected) != 0
                    && _socket?.State == WebSocketState.Open;
            }
        }

        public event Action<DisconnectReason>? Disconnected;

        public async Task<IpcResult> ConnectAsync(CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(WebSocketClient));
            }
            if (Interlocked.CompareExchange(ref _connectStarted, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "WebSocketClient.ConnectAsync has already been called. "
                    + "ClientWebSocket cannot be reused; create a new instance for each connect.");
            }

            ClientWebSocket socket;
            lock (_sync)
            {
                socket = new ClientWebSocket();
                _socket = socket;
            }

            string host = string.IsNullOrEmpty(_bindOptions.Host) ? "127.0.0.1" : _bindOptions.Host;
            var uri = new Uri($"ws://{host}:{_bindOptions.Port}/");
            _remoteEndpoint = $"{host}:{_bindOptions.Port}";

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetimeCts.Token);

            TimeSpan connectTimeout = _bindOptions.ConnectTimeout > TimeSpan.Zero
                ? _bindOptions.ConnectTimeout
                : TimeSpan.FromSeconds(5);

            try
            {
                connectCts.CancelAfter(connectTimeout);
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                await socket.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logInfo?.Invoke($"WebSocketClient: connect to {uri} was cancelled by caller.");
                SafeDisposeSocket();
                throw;
            }
            catch (OperationCanceledException)
            {
                _logWarning?.Invoke(
                    $"WebSocketClient: connect to {uri} timed out after "
                    + $"{(long)connectTimeout.TotalMilliseconds} ms.");
                SafeDisposeSocket();
                return IpcResult.Fail(
                    new CoreIpcError.TransportFailure(
                        $"Connect to {uri} timed out after "
                        + $"{(long)connectTimeout.TotalMilliseconds} ms."));
            }
            catch (WebSocketException ex)
            {
                _logWarning?.Invoke(
                    $"WebSocketClient: connect to {uri} failed: {ex.Message}");
                SafeDisposeSocket();
                return IpcResult.Fail(new CoreIpcError.TransportFailure(ex.Message));
            }
            catch (Exception ex)
            {
                _logError?.Invoke(
                    $"WebSocketClient: unexpected error connecting to {uri}: {ex.Message}");
                SafeDisposeSocket();
                return IpcResult.Fail(new CoreIpcError.TransportFailure(ex.Message));
            }

            Volatile.Write(ref _connected, 1);
            _logInfo?.Invoke($"WebSocketClient: connected to {uri}.");

            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_lifetimeCts.Token));
            return IpcResult.Ok();
        }

        public async ValueTask SendAsync(
            ReadOnlyMemory<byte> textFramePayload,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(WebSocketClient));
            }
            if (Volatile.Read(ref _connected) == 0)
            {
                throw new InvalidOperationException(
                    "WebSocketClient is not connected; call ConnectAsync first.");
            }

            ClientWebSocket? socket;
            lock (_sync)
            {
                socket = _socket;
            }
            if (socket == null || socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException(
                    "WebSocketClient underlying socket is not open.");
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _lifetimeCts.Token);

            try
            {
                await socket.SendAsync(
                    SafeArraySegment(textFramePayload),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logWarning?.Invoke(
                    $"WebSocketClient: send to {_remoteEndpoint} failed: {ex.Message}");
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

            try { _lifetimeCts.Cancel(); } catch (ObjectDisposedException) { }

            ClientWebSocket? socket;
            lock (_sync)
            {
                socket = _socket;
            }

            if (socket != null && socket.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(_clientOptions.CloseTimeout);
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "client-dispose",
                        closeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logInfo?.Invoke(
                        $"WebSocketClient: close handshake with {_remoteEndpoint} failed: {ex.Message}");
                }
            }

            if (_receiveLoopTask != null)
            {
                try
                {
                    await _receiveLoopTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"WebSocketClient: receive loop ended with error: {ex.Message}");
                }
            }

            SafeDisposeSocket();
            _inbound.Complete();

            try { _lifetimeCts.Dispose(); } catch { /* swallow */ }

            NotifyDisconnected(DisconnectReason.LocalClose);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            ClientWebSocket? socket;
            lock (_sync)
            {
                socket = _socket;
            }
            if (socket == null)
            {
                _inbound.Complete();
                return;
            }

            byte[] buffer = new byte[_clientOptions.ReceiveBufferSize];
            using var assemblyBuffer = new MemoryStream();
            DisconnectReason exitReason = DisconnectReason.Unknown;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        exitReason = DisconnectReason.LocalClose;
                        return;
                    }
                    catch (WebSocketException ex)
                    {
                        _logWarning?.Invoke(
                            $"WebSocketClient: receive from {_remoteEndpoint} failed: {ex.Message}");
                        exitReason = DisconnectReason.TransportError;
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        exitReason = DisconnectReason.LocalClose;
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logError?.Invoke(
                            $"WebSocketClient: unexpected receive error from {_remoteEndpoint}: {ex.Message}");
                        exitReason = DisconnectReason.TransportError;
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logInfo?.Invoke(
                            $"WebSocketClient: peer {_remoteEndpoint} closed (code={(int?)result.CloseStatus}, "
                            + $"reason={result.CloseStatusDescription ?? string.Empty}).");
                        exitReason = DisconnectReason.PeerClose;
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        if (assemblyBuffer.Length > 0)
                        {
                            assemblyBuffer.SetLength(0);
                        }
                        if (!result.EndOfMessage)
                        {
                            await DrainBinaryFragmentsAsync(socket, buffer, ct).ConfigureAwait(false);
                        }
                        _logWarning?.Invoke(
                            $"WebSocketClient: received Binary frame from {_remoteEndpoint}; "
                            + "discarding (only Text frames are supported).");
                        continue;
                    }

                    long projected = assemblyBuffer.Length + result.Count;
                    if (projected > _clientOptions.MaxMessagePayloadBytes)
                    {
                        _logWarning?.Invoke(
                            $"WebSocketClient: received message of {projected} bytes from "
                            + $"{_remoteEndpoint} exceeds limit "
                            + $"{_clientOptions.MaxMessagePayloadBytes} bytes; closing.");
                        await TryCloseWithCodeAsync(
                            socket,
                            WebSocketCloseStatus.MessageTooBig,
                            "message too big",
                            ct).ConfigureAwait(false);
                        exitReason = DisconnectReason.PolicyViolation;
                        return;
                    }

                    assemblyBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        byte[] payload = assemblyBuffer.ToArray();
                        assemblyBuffer.SetLength(0);
                        _inbound.Enqueue(payload);
                    }
                }
            }
            finally
            {
                _inbound.Complete();
                NotifyDisconnected(exitReason);
            }
        }

        private static async Task DrainBinaryFragmentsAsync(
            ClientWebSocket socket,
            byte[] buffer,
            CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }
                if (result.EndOfMessage) return;
                if (result.MessageType == WebSocketMessageType.Close) return;
            }
        }

        private async Task TryCloseWithCodeAsync(
            ClientWebSocket socket,
            WebSocketCloseStatus status,
            string description,
            CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try { linked.CancelAfter(_clientOptions.CloseTimeout); }
            catch (ObjectDisposedException) { }

            try
            {
                await socket.CloseAsync(status, description, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logInfo?.Invoke(
                    $"WebSocketClient: failed to send close frame to {_remoteEndpoint}: {ex.Message}");
            }
        }

        private void SafeDisposeSocket()
        {
            ClientWebSocket? socket;
            lock (_sync)
            {
                socket = _socket;
                _socket = null;
            }
            if (socket == null) return;
            try { socket.Dispose(); }
            catch { /* swallow */ }
        }

        private void NotifyDisconnected(DisconnectReason reason)
        {
            if (Interlocked.CompareExchange(ref _disconnectNotified, 1, 0) != 0)
            {
                return;
            }
            try
            {
                Disconnected?.Invoke(reason);
            }
            catch (Exception ex)
            {
                _logWarning?.Invoke(
                    $"WebSocketClient: Disconnected handler threw: {ex.Message}");
            }
        }

        private static ArraySegment<byte> SafeArraySegment(ReadOnlyMemory<byte> memory)
        {
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> seg))
            {
                return seg;
            }
            byte[] copy = memory.ToArray();
            return new ArraySegment<byte>(copy);
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

        public enum DisconnectReason
        {
            Unknown = 0,
            LocalClose = 1,
            PeerClose = 2,
            TransportError = 3,
            PolicyViolation = 4,
        }
    }
}
