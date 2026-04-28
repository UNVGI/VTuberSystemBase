#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class CoreIpcRuntimeHostTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
        }

        private static int FindFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static CoreIpcOptions FastOptions(int port) => new()
        {
            Host = "127.0.0.1",
            Port = port,
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
            ReconnectMaxAttempts = 3,
        };

        private static CoreIpcRuntimeHost NewHostWithFakeTransport(
            FakeTransportAdapter transport,
            bool installPlayerLoop = false,
            bool registerAsCurrent = false)
        {
            return new CoreIpcRuntimeHost(
                transportFactory: _ => transport,
                installPlayerLoop: installPlayerLoop,
                registerAsCurrent: registerAsCurrent,
                clientReconnectDelay: (delay, ct) => Task.Delay(TimeSpan.FromMilliseconds(5), ct));
        }

        // ---------- State transitions ----------

        [Test]
        public async Task Initialize_TransitionsThroughInitializingToRunning()
        {
            var transport = new FakeTransportAdapter();
            using var host = NewHostWithFakeTransport(transport);

            Assert.AreEqual(RuntimeState.NotInitialized, host.State);

            using var cts = new CancellationTokenSource(TestTimeout);
            await host.InitializeAsync(FastOptions(0), cts.Token);

            Assert.AreEqual(RuntimeState.Running, host.State);
            Assert.IsTrue(transport.StartServerCalled);
            Assert.GreaterOrEqual(transport.ConnectClientCallCount, 1);
        }

        [Test]
        public void InitializeAsync_NullOptions_Throws()
        {
            var transport = new FakeTransportAdapter();
            using var host = NewHostWithFakeTransport(transport);

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await host.InitializeAsync(null!));
        }

        [Test]
        public async Task DoubleInitialize_Throws_InvalidOperationException()
        {
            var transport = new FakeTransportAdapter();
            using var host = NewHostWithFakeTransport(transport);

            using var cts = new CancellationTokenSource(TestTimeout);
            await host.InitializeAsync(FastOptions(0), cts.Token);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await host.InitializeAsync(FastOptions(0), cts.Token));
        }

        [Test]
        public async Task Dispose_ReachesDisposedState_AndIsIdempotent()
        {
            var transport = new FakeTransportAdapter();
            var host = NewHostWithFakeTransport(transport);

            using var cts = new CancellationTokenSource(TestTimeout);
            await host.InitializeAsync(FastOptions(0), cts.Token);

            host.Dispose();
            Assert.AreEqual(RuntimeState.Disposed, host.State);
            Assert.IsTrue(transport.DisposeCalled);
            int disposeCallsAfterFirst = transport.DisposeCallCount;

            // Second dispose should be no-op.
            host.Dispose();
            Assert.AreEqual(RuntimeState.Disposed, host.State);
            Assert.AreEqual(disposeCallsAfterFirst, transport.DisposeCallCount,
                "Transport should only be disposed once even when host.Dispose is called twice.");
        }

        [Test]
        public async Task Dispose_BeforeInitialize_TransitionsToDisposed()
        {
            var host = new CoreIpcRuntimeHost(
                transportFactory: _ => new FakeTransportAdapter(),
                installPlayerLoop: false,
                registerAsCurrent: false);

            host.Dispose();
            Assert.AreEqual(RuntimeState.Disposed, host.State);

            // Initialize after dispose must throw.
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await host.InitializeAsync(FastOptions(0)));
        }

        [Test]
        public async Task Bus_ThrowsBeforeInitialize()
        {
            using var host = NewHostWithFakeTransport(new FakeTransportAdapter());
            Assert.Throws<InvalidOperationException>(() => { _ = host.Bus; });
            await Task.CompletedTask;
        }

        [Test]
        public async Task Bus_ReturnsInstanceAfterInitialize()
        {
            var transport = new FakeTransportAdapter();
            using var host = NewHostWithFakeTransport(transport);
            using var cts = new CancellationTokenSource(TestTimeout);
            await host.InitializeAsync(FastOptions(0), cts.Token);

            Assert.IsNotNull(host.Bus);
            Assert.IsNotNull(host.Bus.Diagnostics);
        }

        // ---------- PortInUse propagation (Req 2.6) ----------

        [Test]
        public async Task InitializeAsync_PortInUse_PropagatesAsTransportException()
        {
            int port = FindFreeTcpPort();
            var blocker = new TcpListener(IPAddress.Loopback, port);
            blocker.Server.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ExclusiveAddressUse,
                true);
            blocker.Start();

            try
            {
                var host = new CoreIpcRuntimeHost(
                    transportFactory: opts => new WebSocketTransportAdapter(
                        new SystemTextJsonCodec(opts),
                        new WebSocketServerOptions
                        {
                            HandshakeTimeout = TimeSpan.FromSeconds(2),
                            CloseTimeout = TimeSpan.FromSeconds(1),
                        },
                        new WebSocketClientOptions
                        {
                            CloseTimeout = TimeSpan.FromSeconds(1),
                        }),
                    installPlayerLoop: false,
                    registerAsCurrent: false);

                using var cts = new CancellationTokenSource(TestTimeout);

                CoreIpcTransportException? caught = null;
                try
                {
                    await host.InitializeAsync(FastOptions(port), cts.Token);
                }
                catch (CoreIpcTransportException ex)
                {
                    caught = ex;
                }

                Assert.IsNotNull(caught, "Expected CoreIpcTransportException for PortInUse to propagate.");
                Assert.IsInstanceOf<CoreIpcError.PortInUse>(caught!.IpcError);

                Assert.AreEqual(RuntimeState.NotInitialized, host.State,
                    "After PortInUse, runtime should reset to NotInitialized for retry.");

                // No singleton should have been registered when init failed.
                Assert.IsNull(CoreIpcRuntime.Current);

                host.Dispose();
            }
            finally
            {
                blocker.Stop();
            }
        }

        // ---------- CoreIpcRuntime singleton ----------

        [Test]
        public async Task SuccessfulInitialize_RegistersAsCoreIpcRuntimeCurrent()
        {
            var transport = new FakeTransportAdapter();
            var host = new CoreIpcRuntimeHost(
                transportFactory: _ => transport,
                installPlayerLoop: false,
                registerAsCurrent: true,
                clientReconnectDelay: (delay, ct) => Task.Delay(TimeSpan.FromMilliseconds(5), ct));

            using var cts = new CancellationTokenSource(TestTimeout);
            await host.InitializeAsync(FastOptions(0), cts.Token);

            Assert.AreSame(host, CoreIpcRuntime.Current);

            host.Dispose();
            Assert.IsNull(CoreIpcRuntime.Current,
                "Disposing the runtime that owns Current should clear it.");
        }

        [Test]
        public void OverrideForTesting_ReplacesCurrent()
        {
            var fake = new FakeRuntime();
            CoreIpcRuntime.OverrideForTesting(fake);
            Assert.AreSame(fake, CoreIpcRuntime.Current);

            CoreIpcRuntime.ResetForTesting();
            Assert.IsNull(CoreIpcRuntime.Current);
        }

        // ---------- Helpers ----------

        private sealed class FakeRuntime : ICoreIpcRuntime
        {
            public RuntimeState State => RuntimeState.Running;
            public ICoreIpcBus Bus =>
                throw new NotSupportedException("FakeRuntime has no bus.");
            public CoreIpcOptions Options { get; } = new();

            public Task InitializeAsync(CoreIpcOptions options, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public void Dispose() { }
        }

        private sealed class FakeTransportAdapter : ITransportAdapter
        {
            private readonly ConcurrentDictionary<FakeClientConnection, byte> _connections = new();
            private int _serverStarted;
            private int _disposed;

            public bool StartServerCalled => Volatile.Read(ref _serverStarted) != 0;
            public bool DisposeCalled => Volatile.Read(ref _disposed) != 0;
            public int DisposeCallCount;
            public int ConnectClientCallCount;

            public event Action<IClientConnection>? ClientConnected;
            public event Action<IClientConnection>? ClientDisconnected;

            public Task StartServerAsync(ServerBindOptions options, CancellationToken cancellationToken)
            {
                if (Interlocked.CompareExchange(ref _serverStarted, 1, 0) != 0)
                {
                    throw new InvalidOperationException("Server already started.");
                }
                return Task.CompletedTask;
            }

            public Task<IClientConnection> ConnectClientAsync(
                ClientBindOptions options, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref ConnectClientCallCount);
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return Task.FromException<IClientConnection>(
                        new ObjectDisposedException(nameof(FakeTransportAdapter)));
                }
                var connection = new FakeClientConnection();
                _connections.TryAdd(connection, 0);
                ClientConnected?.Invoke(connection);
                return Task.FromResult<IClientConnection>(connection);
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref DisposeCallCount);
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                {
                    return default;
                }

                foreach (var c in _connections.Keys)
                {
                    c.Complete();
                    ClientDisconnected?.Invoke(c);
                }
                _connections.Clear();
                return default;
            }
        }

        private sealed class FakeClientConnection : IClientConnection
        {
            private readonly object _lock = new();
            private readonly Queue<byte[]> _items = new();
            private TaskCompletionSource<bool>? _waiter;
            private bool _completed;
            private int _disposed;

            public string RemoteEndpoint { get; } = "fake://local";

            public ValueTask SendAsync(ReadOnlyMemory<byte> textFramePayload, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }

            public IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken)
            {
                return ReadAllAsync(cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _disposed, 1);
                Complete();
                return default;
            }

            internal void Complete()
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

            private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                while (true)
                {
                    Task waitTask;
                    lock (_lock)
                    {
                        if (_items.Count > 0)
                        {
                            yield return _items.Dequeue();
                            continue;
                        }
                        if (_completed) yield break;
                        _waiter ??= new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        waitTask = _waiter.Task;
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
