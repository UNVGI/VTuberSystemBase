#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Connection;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class ClientSessionManagerTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

        private static ClientBindOptions DefaultBindOptions() =>
            new("127.0.0.1", 61874, TimeSpan.FromSeconds(1));

        private static ReconnectBackoff QuickBackoff(int maxAttempts = 5) =>
            new(
                initialDelay: TimeSpan.FromMilliseconds(1),
                multiplier: 2.0,
                maxDelay: TimeSpan.FromMilliseconds(10),
                maxAttempts: maxAttempts);

        private static Func<TimeSpan, CancellationToken, Task> InstantDelay() =>
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            };

        private static Func<TimeSpan, CancellationToken, Task> NeverReturningDelay() =>
            (_, ct) =>
            {
                var tcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            };

        private static async Task WaitForAsync(
            Func<bool> predicate,
            TimeSpan timeout,
            string description)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!predicate())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail($"Timed out after {timeout.TotalMilliseconds:0} ms waiting for {description}.");
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private static List<ConnectionState> AttachStateLog(
            ConnectionStateMachine sm,
            object syncRoot)
        {
            var log = new List<ConnectionState>();
            sm.StateChanged += (_, next) =>
            {
                lock (syncRoot) log.Add(next);
            };
            return log;
        }

        [Test]
        public async Task Scenario1_ConnectSucceeds_TransitionsToConnected()
        {
            var transport = new FakeTransport();
            var connection = new FakeClientConnection();
            transport.EnqueueSuccess(connection);

            var sm = new ConnectionStateMachine();
            var backoff = QuickBackoff();
            var sync = new object();
            var stateLog = AttachStateLog(sm, sync);

            await using var manager = new ClientSessionManager(
                transport,
                DefaultBindOptions(),
                sm,
                backoff,
                delay: InstantDelay());

            await manager.StartAsync();

            await WaitForAsync(
                () => sm.CurrentState == ConnectionState.Connected,
                TestTimeout,
                "initial Connected");

            Assert.AreEqual(ConnectionState.Connected, sm.CurrentState);
            Assert.AreEqual(1, transport.ConnectAttemptCount);
            Assert.AreEqual(0, backoff.AttemptCount,
                "successful connect must reset backoff.");

            lock (sync)
            {
                CollectionAssert.AreEqual(
                    new[] { ConnectionState.Connecting, ConnectionState.Connected },
                    stateLog);
            }
        }

        [Test]
        public async Task Scenario2_DropAfterConnected_RecoversThroughReconnecting()
        {
            var transport = new FakeTransport();
            var firstConnection = new FakeClientConnection();
            var secondConnection = new FakeClientConnection();
            transport.EnqueueSuccess(firstConnection);
            transport.EnqueueSuccess(secondConnection);

            var sm = new ConnectionStateMachine();
            var backoff = QuickBackoff();
            var sync = new object();
            var stateLog = AttachStateLog(sm, sync);

            await using var manager = new ClientSessionManager(
                transport,
                DefaultBindOptions(),
                sm,
                backoff,
                delay: InstantDelay());

            await manager.StartAsync();

            await WaitForAsync(
                () => sm.CurrentState == ConnectionState.Connected,
                TestTimeout,
                "initial Connected");

            firstConnection.SimulateDrop();

            await WaitForAsync(
                () =>
                {
                    lock (sync) return stateLog.Count >= 5;
                },
                TestTimeout,
                "five state transitions (Connecting, Connected, Reconnecting, Connecting, Connected)");

            Assert.AreEqual(2, transport.ConnectAttemptCount);
            Assert.AreEqual(0, backoff.AttemptCount,
                "successful reconnect must reset backoff.");
            Assert.IsTrue(firstConnection.WasDisposed,
                "dropped connection must be disposed.");
            Assert.AreEqual(ConnectionState.Connected, sm.CurrentState);

            lock (sync)
            {
                CollectionAssert.AreEqual(
                    new[]
                    {
                        ConnectionState.Connecting,
                        ConnectionState.Connected,
                        ConnectionState.Reconnecting,
                        ConnectionState.Connecting,
                        ConnectionState.Connected,
                    },
                    stateLog);
            }
        }

        [Test]
        public async Task Scenario3_AttemptsExceedMax_TransitionsToPermanentlyDisconnected()
        {
            const int maxAttempts = 3;

            var transport = new FakeTransport();
            var firstConnection = new FakeClientConnection();
            transport.EnqueueSuccess(firstConnection);
            for (int i = 0; i < maxAttempts; i++)
            {
                transport.EnqueueFailure(
                    new InvalidOperationException($"reconnect attempt #{i + 1} fails"));
            }

            var sm = new ConnectionStateMachine();
            var backoff = QuickBackoff(maxAttempts);
            var sync = new object();
            var stateLog = AttachStateLog(sm, sync);

            await using var manager = new ClientSessionManager(
                transport,
                DefaultBindOptions(),
                sm,
                backoff,
                delay: InstantDelay());

            await manager.StartAsync();

            await WaitForAsync(
                () => sm.CurrentState == ConnectionState.Connected,
                TestTimeout,
                "initial Connected");

            firstConnection.SimulateDrop();

            await WaitForAsync(
                () => sm.CurrentState == ConnectionState.PermanentlyDisconnected,
                TestTimeout,
                "PermanentlyDisconnected after max retries");

            Assert.AreEqual(ConnectionState.PermanentlyDisconnected, sm.CurrentState);
            Assert.AreEqual(
                1 + maxAttempts,
                transport.ConnectAttemptCount,
                "should attempt initial + each retry up to max.");
            Assert.IsTrue(backoff.ExceededMaxAttempts);

            lock (sync)
            {
                CollectionAssert.AreEqual(
                    new[]
                    {
                        ConnectionState.Connecting,
                        ConnectionState.Connected,
                        ConnectionState.Reconnecting,
                        ConnectionState.PermanentlyDisconnected,
                    },
                    stateLog);
            }
        }

        [Test]
        public async Task Scenario4_ShutdownDuringReconnect_StopsRetriesAndCleanlyDisconnects()
        {
            var transport = new FakeTransport();
            var firstConnection = new FakeClientConnection();
            transport.EnqueueSuccess(firstConnection);
            for (int i = 0; i < 100; i++)
            {
                transport.EnqueueFailure(
                    new InvalidOperationException("server is offline"));
            }

            var sm = new ConnectionStateMachine();
            var backoff = QuickBackoff(maxAttempts: 100);

            await using var manager = new ClientSessionManager(
                transport,
                DefaultBindOptions(),
                sm,
                backoff,
                delay: NeverReturningDelay());

            await manager.StartAsync();
            await WaitForAsync(
                () => sm.CurrentState == ConnectionState.Connected,
                TestTimeout,
                "initial Connected");

            firstConnection.SimulateDrop();
            await WaitForAsync(
                () => sm.CurrentState == ConnectionState.Reconnecting,
                TestTimeout,
                "Reconnecting after drop");

            int attemptsBeforeShutdown = transport.ConnectAttemptCount;

            await manager.ShutdownAsync();

            Assert.AreEqual(ConnectionState.Disconnected, sm.CurrentState);
            Assert.IsTrue(sm.ShutdownRequested);
            Assert.AreEqual(
                attemptsBeforeShutdown,
                transport.ConnectAttemptCount,
                "shutdown must suppress further connect attempts.");
        }

        [Test]
        public async Task ReceivedFrames_AreForwardedToCallback()
        {
            var transport = new FakeTransport();
            var connection = new FakeClientConnection();
            transport.EnqueueSuccess(connection);

            var sm = new ConnectionStateMachine();
            var backoff = QuickBackoff();

            var received = new List<byte[]>();
            var receivedLock = new object();

            await using var manager = new ClientSessionManager(
                transport,
                DefaultBindOptions(),
                sm,
                backoff,
                delay: InstantDelay(),
                onMessageReceived: bytes =>
                {
                    lock (receivedLock) received.Add(bytes.ToArray());
                });

            await manager.StartAsync();
            await WaitForAsync(
                () => sm.CurrentState == ConnectionState.Connected,
                TestTimeout,
                "initial Connected");

            connection.PushFrame(new byte[] { 1, 2, 3 });
            connection.PushFrame(new byte[] { 4, 5 });

            await WaitForAsync(
                () =>
                {
                    lock (receivedLock) return received.Count >= 2;
                },
                TestTimeout,
                "two frames forwarded to callback");

            lock (receivedLock)
            {
                Assert.AreEqual(2, received.Count);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, received[0]);
                CollectionAssert.AreEqual(new byte[] { 4, 5 }, received[1]);
            }
        }

        [Test]
        public void Constructor_NullTransport_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ClientSessionManager(
                    transport: null!,
                    DefaultBindOptions(),
                    new ConnectionStateMachine(),
                    QuickBackoff()));
        }

        [Test]
        public void Constructor_NullStateMachine_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ClientSessionManager(
                    new FakeTransport(),
                    DefaultBindOptions(),
                    stateMachine: null!,
                    QuickBackoff()));
        }

        [Test]
        public void Constructor_NullBackoff_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new ClientSessionManager(
                    new FakeTransport(),
                    DefaultBindOptions(),
                    new ConnectionStateMachine(),
                    backoff: null!));
        }

        [Test]
        public async Task StartAsync_CalledTwice_Throws()
        {
            var transport = new FakeTransport();
            transport.EnqueueSuccess(new FakeClientConnection());

            var manager = new ClientSessionManager(
                transport,
                DefaultBindOptions(),
                new ConnectionStateMachine(),
                QuickBackoff(),
                delay: InstantDelay());

            await manager.StartAsync();
            Assert.Throws<InvalidOperationException>(
                () => manager.StartAsync().GetAwaiter().GetResult());

            await manager.DisposeAsync();
        }

        // ---------- Test fakes ----------

        private sealed class FakeTransport : ITransportAdapter
        {
            private readonly object _sync = new();
            private readonly Queue<Func<Task<IClientConnection>>> _responses = new();
            private int _connectAttemptCount;

            public int ConnectAttemptCount
            {
                get { lock (_sync) return _connectAttemptCount; }
            }

#pragma warning disable CS0067
            public event Action<IClientConnection>? ClientConnected;
            public event Action<IClientConnection>? ClientDisconnected;
#pragma warning restore CS0067

            public void EnqueueSuccess(IClientConnection connection)
            {
                lock (_sync)
                {
                    _responses.Enqueue(() => Task.FromResult(connection));
                }
            }

            public void EnqueueFailure(Exception exception)
            {
                lock (_sync)
                {
                    _responses.Enqueue(() => Task.FromException<IClientConnection>(exception));
                }
            }

            public Task StartServerAsync(
                ServerBindOptions options,
                CancellationToken cancellationToken)
            {
                return Task.FromException(new NotSupportedException(
                    "FakeTransport: server start not supported."));
            }

            public Task<IClientConnection> ConnectClientAsync(
                ClientBindOptions options,
                CancellationToken cancellationToken)
            {
                Func<Task<IClientConnection>>? next;
                lock (_sync)
                {
                    _connectAttemptCount++;
                    next = _responses.Count > 0 ? _responses.Dequeue() : null;
                }

                if (next == null)
                {
                    return Task.FromException<IClientConnection>(
                        new InvalidOperationException(
                            "FakeTransport: no queued connect response."));
                }

                cancellationToken.ThrowIfCancellationRequested();
                return next();
            }

            public ValueTask DisposeAsync() => default;
        }

        private sealed class FakeClientConnection : IClientConnection
        {
            private readonly object _sync = new();
            private readonly Queue<byte[]> _pendingFrames = new();
            private TaskCompletionSource<bool>? _frameWaiter;
            private bool _dropped;
            private Exception? _dropError;
            private bool _disposed;

            public string RemoteEndpoint => "ws://fake/test";

            public bool WasDisposed
            {
                get { lock (_sync) return _disposed; }
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> textFramePayload,
                CancellationToken cancellationToken)
            {
                return default;
            }

            public void PushFrame(byte[] payload)
            {
                TaskCompletionSource<bool>? waiter;
                lock (_sync)
                {
                    if (_dropped || _disposed)
                    {
                        throw new InvalidOperationException(
                            "FakeClientConnection: cannot push after drop/dispose.");
                    }

                    _pendingFrames.Enqueue(payload);
                    waiter = _frameWaiter;
                    _frameWaiter = null;
                }

                waiter?.TrySetResult(true);
            }

            public void SimulateDrop()
            {
                TaskCompletionSource<bool>? waiter;
                lock (_sync)
                {
                    if (_dropped) return;
                    _dropped = true;
                    waiter = _frameWaiter;
                    _frameWaiter = null;
                }

                waiter?.TrySetResult(true);
            }

            public void SimulateError(Exception error)
            {
                TaskCompletionSource<bool>? waiter;
                lock (_sync)
                {
                    if (_dropped) return;
                    _dropped = true;
                    _dropError = error;
                    waiter = _frameWaiter;
                    _frameWaiter = null;
                }

                waiter?.TrySetResult(true);
            }

            public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    byte[]? frame = null;
                    bool dropped;
                    Exception? dropError;
                    Task waiterTask;

                    lock (_sync)
                    {
                        if (_pendingFrames.Count > 0)
                        {
                            frame = _pendingFrames.Dequeue();
                        }

                        dropped = _dropped;
                        dropError = _dropError;

                        if (frame == null && !dropped)
                        {
                            _frameWaiter ??= new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously);
                            waiterTask = _frameWaiter.Task;
                        }
                        else
                        {
                            waiterTask = Task.CompletedTask;
                        }
                    }

                    if (frame != null)
                    {
                        yield return frame;
                        continue;
                    }

                    if (dropped)
                    {
                        if (dropError != null) throw dropError;
                        yield break;
                    }

                    var cancelTcs = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    using (cancellationToken.Register(
                        () => cancelTcs.TrySetResult(true)))
                    {
                        await Task.WhenAny(waiterTask, cancelTcs.Task).ConfigureAwait(false);
                    }
                }
            }

            public ValueTask DisposeAsync()
            {
                TaskCompletionSource<bool>? waiter;
                lock (_sync)
                {
                    _disposed = true;
                    if (!_dropped)
                    {
                        _dropped = true;
                    }
                    waiter = _frameWaiter;
                    _frameWaiter = null;
                }

                waiter?.TrySetResult(true);
                return default;
            }
        }
    }
}
