#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Transport.Loopback
{
    public sealed class InMemoryLoopbackTransport : ITransportAdapter
    {
        private readonly object _sync = new();
        private readonly List<LoopbackPair> _pairs = new();

        private int _serverStarted;
        private int _disposed;

        public event Action<IClientConnection>? ClientConnected;
        public event Action<IClientConnection>? ClientDisconnected;

        public bool IsServerRunning => Volatile.Read(ref _serverStarted) != 0
            && Volatile.Read(ref _disposed) == 0;

        public int ConnectedClientCount
        {
            get
            {
                lock (_sync)
                {
                    return _pairs.Count;
                }
            }
        }

        public Task StartServerAsync(ServerBindOptions options, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (Interlocked.CompareExchange(ref _serverStarted, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "InMemoryLoopbackTransport: server has already been started.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IClientConnection> ConnectClientAsync(
            ClientBindOptions options,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _serverStarted) == 0)
            {
                throw new InvalidOperationException(
                    "InMemoryLoopbackTransport: server has not been started; "
                    + "call StartServerAsync first.");
            }
            cancellationToken.ThrowIfCancellationRequested();

            string remote = string.IsNullOrEmpty(options.Host)
                ? $"loopback:{options.Port}"
                : $"{options.Host}:{options.Port}";

            var clientToServer = new LoopbackByteQueue();
            var serverToClient = new LoopbackByteQueue();

            var pair = new LoopbackPair(this, remote, clientToServer, serverToClient);
            lock (_sync)
            {
                _pairs.Add(pair);
            }

            try
            {
                ClientConnected?.Invoke(pair.ServerSide);
            }
            catch
            {
                // Handler exceptions must not break the transport contract.
            }

            return Task.FromResult<IClientConnection>(pair.ClientSide);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            {
                return;
            }

            LoopbackPair[] snapshot;
            lock (_sync)
            {
                snapshot = _pairs.ToArray();
                _pairs.Clear();
            }

            foreach (var pair in snapshot)
            {
                await pair.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal void OnPairDisposed(LoopbackPair pair, IClientConnection serverSide)
        {
            bool removed;
            lock (_sync)
            {
                removed = _pairs.Remove(pair);
            }
            if (!removed) return;
            try
            {
                ClientDisconnected?.Invoke(serverSide);
            }
            catch
            {
                // Suppress handler exceptions per contract.
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(InMemoryLoopbackTransport));
            }
        }

        internal sealed class LoopbackPair
        {
            private readonly InMemoryLoopbackTransport _owner;
            private readonly LoopbackByteQueue _clientToServer;
            private readonly LoopbackByteQueue _serverToClient;
            private int _disposed;

            public LoopbackEndpoint ClientSide { get; }
            public LoopbackEndpoint ServerSide { get; }

            public LoopbackPair(
                InMemoryLoopbackTransport owner,
                string remote,
                LoopbackByteQueue clientToServer,
                LoopbackByteQueue serverToClient)
            {
                _owner = owner;
                _clientToServer = clientToServer;
                _serverToClient = serverToClient;
                ClientSide = new LoopbackEndpoint(
                    this,
                    remoteEndpoint: $"server@{remote}",
                    outgoing: clientToServer,
                    incoming: serverToClient);
                ServerSide = new LoopbackEndpoint(
                    this,
                    remoteEndpoint: $"client@{remote}",
                    outgoing: serverToClient,
                    incoming: clientToServer);
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                {
                    return default;
                }
                _clientToServer.Complete();
                _serverToClient.Complete();
                _owner.OnPairDisposed(this, ServerSide);
                return default;
            }

            public ValueTask EndpointDisposedAsync(LoopbackEndpoint endpoint)
            {
                _clientToServer.Complete();
                _serverToClient.Complete();
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                {
                    _owner.OnPairDisposed(this, ServerSide);
                }
                return default;
            }
        }

        internal sealed class LoopbackEndpoint : IClientConnection
        {
            private readonly LoopbackPair _pair;
            private readonly LoopbackByteQueue _outgoing;
            private readonly LoopbackByteQueue _incoming;
            private int _disposed;

            public string RemoteEndpoint { get; }

            public LoopbackEndpoint(
                LoopbackPair pair,
                string remoteEndpoint,
                LoopbackByteQueue outgoing,
                LoopbackByteQueue incoming)
            {
                _pair = pair;
                RemoteEndpoint = remoteEndpoint;
                _outgoing = outgoing;
                _incoming = incoming;
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> textFramePayload,
                CancellationToken cancellationToken)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(LoopbackEndpoint));
                }
                cancellationToken.ThrowIfCancellationRequested();
                _outgoing.Enqueue(textFramePayload.ToArray());
                return default;
            }

            public IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(
                CancellationToken cancellationToken)
            {
                return _incoming.ReadAllAsync(cancellationToken);
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                {
                    return;
                }
                await _pair.EndpointDisposedAsync(this).ConfigureAwait(false);
            }
        }

        internal sealed class LoopbackByteQueue
        {
            private readonly object _lock = new();
            private readonly Queue<byte[]> _items = new();
            private TaskCompletionSource<bool>? _waiter;
            private bool _completed;

            public void Enqueue(byte[] item)
            {
                TaskCompletionSource<bool>? toComplete;
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
                TaskCompletionSource<bool>? toComplete;
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
