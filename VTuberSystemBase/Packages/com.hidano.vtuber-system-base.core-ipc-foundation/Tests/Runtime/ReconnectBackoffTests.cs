#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Transport.Loopback;
using VTuberSystemBase.CoreIpc.Tests.TestSupport;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class ReconnectBackoffTests
    {
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan AssertTimeout = TimeSpan.FromSeconds(15);

        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
        }

        private static CoreIpcOptions FastOptions(int maxAttempts) => new()
        {
            Host = "loopback",
            Port = 0,
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(5),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(10),
            ReconnectMaxAttempts = maxAttempts,
            DefaultRequestTimeout = TimeSpan.FromSeconds(2),
        };

        private static Func<TimeSpan, CancellationToken, Task> InstantDelay() =>
            (_, ct) =>
            {
                if (ct.IsCancellationRequested)
                {
                    return Task.FromCanceled(ct);
                }
                return Task.Delay(TimeSpan.FromMilliseconds(5), ct);
            };

        [UnityTest]
        public IEnumerator ClientFirst_ServerArrivesLate_ConnectsViaBackoffRetries()
        {
            const int failuresBeforeServerReady = 4;

            var transport = new ScriptedLoopbackTransport(
                failuresBeforeServerReady: failuresBeforeServerReady);

            var host = new CoreIpcRuntimeHost(
                transportFactory: _ => transport,
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: InstantDelay());

            yield return LoopbackIntegrationHarness.InitializeAndAwaitConnected(
                host, FastOptions(maxAttempts: 10));

            Assert.GreaterOrEqual(
                transport.ConnectAttemptCount,
                failuresBeforeServerReady + 1,
                "Transport should have observed at least failuresBeforeServerReady+1 attempts " +
                "(scripted failures + final success); observed " + transport.ConnectAttemptCount);

            Assert.AreEqual(
                ConnectionState.Connected,
                host.Bus.Diagnostics.CurrentState,
                "Late-arriving server scenario must end in Connected.");

            host.Dispose();
            Assert.AreEqual(RuntimeState.Disposed, host.State);
        }

        [UnityTest]
        public IEnumerator ServerDrop_PermanentlyAbsent_TransitionsToPermanentlyDisconnected()
        {
            const int maxReconnectAttempts = 20;

            var transport = new ScriptedLoopbackTransport(failuresBeforeServerReady: 0);

            IClientConnection? capturedServerSide = null;
            transport.ClientConnected += conn => capturedServerSide = conn;

            var host = new CoreIpcRuntimeHost(
                transportFactory: _ => transport,
                installPlayerLoop: true,
                registerAsCurrent: false,
                clientReconnectDelay: InstantDelay());

            var initTask = host.InitializeAsync(FastOptions(maxAttempts: maxReconnectAttempts));
            yield return LoopbackIntegrationHarness.AwaitTask(initTask, StartupTimeout);

            var stateLog = new List<ConnectionState>();
            var stateLogSync = new object();
            host.Bus.Diagnostics.ConnectionStateChanged += (_, next) =>
            {
                lock (stateLogSync) stateLog.Add(next);
            };

            yield return LoopbackIntegrationHarness.WaitForConnected(host, StartupTimeout);

            Assert.IsNotNull(
                capturedServerSide,
                "Server-side connection must have been captured via ClientConnected event.");

            transport.PreventFurtherClientConnects();

            var disposeServerTask = capturedServerSide!.DisposeAsync().AsTask();
            yield return LoopbackIntegrationHarness.AwaitTask(
                disposeServerTask, AssertTimeout);

            yield return LoopbackIntegrationHarness.WaitFor(
                () => host.Bus.Diagnostics.CurrentState
                    == ConnectionState.PermanentlyDisconnected,
                AssertTimeout,
                "PermanentlyDisconnected must be reached after maxReconnectAttempts " +
                "consecutive failures (current state = " +
                host.Bus.Diagnostics.CurrentState + ", attempts seen = " +
                transport.ConnectAttemptCount + ").");

            Assert.AreEqual(
                ConnectionState.PermanentlyDisconnected,
                host.Bus.Diagnostics.CurrentState);

            lock (stateLogSync)
            {
                CollectionAssert.Contains(stateLog, ConnectionState.Reconnecting,
                    "Reconnecting transition is expected before PermanentlyDisconnected.");
                CollectionAssert.Contains(stateLog, ConnectionState.PermanentlyDisconnected,
                    "ConnectionStateChanged should publish PermanentlyDisconnected.");
            }

            Assert.GreaterOrEqual(
                host.Bus.Diagnostics.ReconnectAttemptCount,
                maxReconnectAttempts,
                "ReconnectAttemptCount should reach the configured maximum before " +
                "transitioning to PermanentlyDisconnected (was " +
                host.Bus.Diagnostics.ReconnectAttemptCount + ").");

            host.Dispose();
        }

        private sealed class ScriptedLoopbackTransport : ITransportAdapter
        {
            private readonly InMemoryLoopbackTransport _inner = new();
            private readonly object _sync = new();
            private readonly int _initialFailures;
            private int _attemptCount;
            private bool _preventFurtherConnects;

            public ScriptedLoopbackTransport(int failuresBeforeServerReady)
            {
                if (failuresBeforeServerReady < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(failuresBeforeServerReady));
                }
                _initialFailures = failuresBeforeServerReady;
            }

            public int ConnectAttemptCount
            {
                get { lock (_sync) return _attemptCount; }
            }

            public event Action<IClientConnection>? ClientConnected
            {
                add => _inner.ClientConnected += value;
                remove => _inner.ClientConnected -= value;
            }

            public event Action<IClientConnection>? ClientDisconnected
            {
                add => _inner.ClientDisconnected += value;
                remove => _inner.ClientDisconnected -= value;
            }

            public Task StartServerAsync(
                ServerBindOptions options, CancellationToken cancellationToken)
            {
                return _inner.StartServerAsync(options, cancellationToken);
            }

            public Task<IClientConnection> ConnectClientAsync(
                ClientBindOptions options, CancellationToken cancellationToken)
            {
                int attemptIndex;
                bool prevented;
                lock (_sync)
                {
                    _attemptCount++;
                    attemptIndex = _attemptCount;
                    prevented = _preventFurtherConnects;
                }

                if (prevented)
                {
                    return Task.FromException<IClientConnection>(
                        new IOException(
                            "ScriptedLoopbackTransport: simulated server outage on attempt #"
                            + attemptIndex + "."));
                }

                if (attemptIndex <= _initialFailures)
                {
                    return Task.FromException<IClientConnection>(
                        new IOException(
                            "ScriptedLoopbackTransport: server not yet ready on attempt #"
                            + attemptIndex + "."));
                }

                return _inner.ConnectClientAsync(options, cancellationToken);
            }

            public ValueTask DisposeAsync() => _inner.DisposeAsync();

            public void PreventFurtherClientConnects()
            {
                lock (_sync)
                {
                    _preventFurtherConnects = true;
                }
            }
        }
    }
}
