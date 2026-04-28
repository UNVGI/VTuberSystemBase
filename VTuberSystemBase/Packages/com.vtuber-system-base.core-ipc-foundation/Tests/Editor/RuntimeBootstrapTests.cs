#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Lifecycle;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class RuntimeBootstrapTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
            RuntimeBootstrap.ResetForTesting();
        }

        [Test]
        public void Bootstrap_NullOptionsLoader_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RuntimeBootstrap.Bootstrap(
                    optionsLoader: null!,
                    runtimeFactory: () => new CoreIpcRuntimeHost(
                        transportFactory: _ => new FakeTransportAdapter(),
                        installPlayerLoop: false,
                        registerAsCurrent: false)));
        }

        [Test]
        public void Bootstrap_NullRuntimeFactory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RuntimeBootstrap.Bootstrap(
                    optionsLoader: () => new CoreIpcOptions(),
                    runtimeFactory: null!));
        }

        [Test]
        public async Task Bootstrap_LoadsOptions_AndStartsRuntime()
        {
            var transport = new FakeTransportAdapter();
            int loaderCalls = 0;
            int factoryCalls = 0;

            var options = new CoreIpcOptions { Host = "127.0.0.1", Port = 0 };

            var (runtime, initTask) = RuntimeBootstrap.Bootstrap(
                optionsLoader: () =>
                {
                    Interlocked.Increment(ref loaderCalls);
                    return options;
                },
                runtimeFactory: () =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return new CoreIpcRuntimeHost(
                        transportFactory: _ => transport,
                        installPlayerLoop: false,
                        registerAsCurrent: false,
                        clientReconnectDelay: (delay, ct) => Task.Delay(TimeSpan.FromMilliseconds(5), ct));
                });

            try
            {
                await AwaitWithTimeout(initTask, TestTimeout);

                Assert.AreEqual(1, loaderCalls);
                Assert.AreEqual(1, factoryCalls);
                Assert.IsTrue(RuntimeBootstrap.IsBootstrapped);
                Assert.IsNotNull(runtime);
                Assert.AreEqual(RuntimeState.Running, runtime.State);
                Assert.IsTrue(transport.StartServerCalled);
                Assert.AreSame(initTask, RuntimeBootstrap.LastInitializationTask);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public async Task Bootstrap_InvokesQuitHandlerAttacher_WithRuntime()
        {
            var transport = new FakeTransportAdapter();
            ICoreIpcRuntime? attached = null;

            var (runtime, initTask) = RuntimeBootstrap.Bootstrap(
                optionsLoader: () => new CoreIpcOptions { Port = 0 },
                runtimeFactory: () => new CoreIpcRuntimeHost(
                    transportFactory: _ => transport,
                    installPlayerLoop: false,
                    registerAsCurrent: false,
                    clientReconnectDelay: (delay, ct) => Task.Delay(TimeSpan.FromMilliseconds(5), ct)),
                quitHandlerAttacher: r => attached = r);

            try
            {
                await AwaitWithTimeout(initTask, TestTimeout);

                Assert.AreSame(runtime, attached,
                    "Quit handler attacher must be invoked with the freshly created runtime.");
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public async Task Bootstrap_RegistersAsCoreIpcRuntimeCurrent_WhenFactoryEnablesIt()
        {
            var transport = new FakeTransportAdapter();

            var (runtime, initTask) = RuntimeBootstrap.Bootstrap(
                optionsLoader: () => new CoreIpcOptions { Port = 0 },
                runtimeFactory: () => new CoreIpcRuntimeHost(
                    transportFactory: _ => transport,
                    installPlayerLoop: false,
                    registerAsCurrent: true,
                    clientReconnectDelay: (delay, ct) => Task.Delay(TimeSpan.FromMilliseconds(5), ct)));

            try
            {
                await AwaitWithTimeout(initTask, TestTimeout);

                Assert.AreSame(runtime, CoreIpcRuntime.Current);
            }
            finally
            {
                runtime.Dispose();
            }

            Assert.IsNull(CoreIpcRuntime.Current,
                "Disposing the runtime that owns Current must clear it.");
        }

        [Test]
        public void Bootstrap_LogsAndRethrows_WhenInitializeAsyncThrowsSynchronously()
        {
            Exception? loggedFailure = null;
            int successCalls = 0;

            Assert.Throws<InvalidOperationException>(() =>
                RuntimeBootstrap.Bootstrap(
                    optionsLoader: () => new CoreIpcOptions(),
                    runtimeFactory: () => new ThrowingSynchronouslyRuntime(),
                    quitHandlerAttacher: null,
                    initFailureLogger: ex => loggedFailure = ex,
                    initSuccessLogger: () => Interlocked.Increment(ref successCalls)));

            Assert.IsNotNull(loggedFailure);
            Assert.IsInstanceOf<InvalidOperationException>(loggedFailure);
            Assert.AreEqual(0, successCalls);
        }

        [Test]
        public async Task Bootstrap_InvokesSuccessLogger_OnSuccessfulInitialization()
        {
            var transport = new FakeTransportAdapter();
            int successCalls = 0;
            Exception? loggedFailure = null;

            var (runtime, initTask) = RuntimeBootstrap.Bootstrap(
                optionsLoader: () => new CoreIpcOptions { Port = 0 },
                runtimeFactory: () => new CoreIpcRuntimeHost(
                    transportFactory: _ => transport,
                    installPlayerLoop: false,
                    registerAsCurrent: false,
                    clientReconnectDelay: (delay, ct) => Task.Delay(TimeSpan.FromMilliseconds(5), ct)),
                quitHandlerAttacher: null,
                initFailureLogger: ex => loggedFailure = ex,
                initSuccessLogger: () => Interlocked.Increment(ref successCalls));

            try
            {
                await AwaitWithTimeout(initTask, TestTimeout);

                // ContinueWith may complete after the init task; spin briefly until logged.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (Volatile.Read(ref successCalls) == 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Yield();
                }

                Assert.AreEqual(1, Volatile.Read(ref successCalls));
                Assert.IsNull(loggedFailure);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Test]
        public void ResetForTesting_ClearsState()
        {
            var transport = new FakeTransportAdapter();

            var (runtime, _) = RuntimeBootstrap.Bootstrap(
                optionsLoader: () => new CoreIpcOptions { Port = 0 },
                runtimeFactory: () => new CoreIpcRuntimeHost(
                    transportFactory: _ => transport,
                    installPlayerLoop: false,
                    registerAsCurrent: false,
                    clientReconnectDelay: (delay, ct) => Task.Delay(TimeSpan.FromMilliseconds(5), ct)));

            Assert.IsTrue(RuntimeBootstrap.IsBootstrapped);

            RuntimeBootstrap.ResetForTesting();

            Assert.IsFalse(RuntimeBootstrap.IsBootstrapped);
            Assert.IsNull(RuntimeBootstrap.LastInitializationTask);

            runtime.Dispose();
        }

        // ---------- Helpers ----------

        private static async Task AwaitWithTimeout(Task task, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource();
            var completed = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
            if (!ReferenceEquals(completed, task))
            {
                throw new TimeoutException($"Task did not complete within {timeout}.");
            }
            cts.Cancel();
            await task;
        }

        private sealed class ThrowingSynchronouslyRuntime : ICoreIpcRuntime
        {
            public RuntimeState State => RuntimeState.NotInitialized;
            public ICoreIpcBus Bus =>
                throw new NotSupportedException("ThrowingSynchronouslyRuntime has no bus.");
            public CoreIpcOptions Options { get; } = new();

            public Task InitializeAsync(CoreIpcOptions options, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("synchronous failure for tests");

            public void Dispose() { }
        }

        private sealed class FakeTransportAdapter : ITransportAdapter
        {
            private readonly ConcurrentDictionary<FakeClientConnection, byte> _connections = new();
            private int _serverStarted;
            private int _disposed;

            public bool StartServerCalled => Volatile.Read(ref _serverStarted) != 0;

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
