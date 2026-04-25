#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Lifecycle;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Tests
{
    [TestFixture]
    public sealed class PlayModeLifecycleTests
    {
        private const int RepeatedCycles = 5;
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);

        [TearDown]
        public void TearDown()
        {
            CoreIpcRuntime.ResetForTesting();
            if (PlayerLoopInstaller.IsInstalled)
            {
                PlayerLoopInstaller.Uninstall();
            }
        }

        [UnityTest]
        public IEnumerator RepeatedSimulatedPlayModeCycles_ReachRunningThenDisposed_AndDoNotLeakDispatchSteps()
        {
            int port = FindFreeTcpPort();
            int initialPreUpdateChildCount = CountPreUpdateChildren(PlayerLoop.GetCurrentPlayerLoop());

            for (int iteration = 0; iteration < RepeatedCycles; iteration++)
            {
                var host = NewWebSocketHost();

                var initCts = new CancellationTokenSource(StartupTimeout);
                var initTask = host.InitializeAsync(FastOptions(port), initCts.Token);

                while (!initTask.IsCompleted)
                {
                    yield return null;
                }
                initCts.Dispose();

                if (initTask.IsFaulted)
                {
                    var ex = initTask.Exception?.GetBaseException() ?? initTask.Exception!;
                    throw new AssertionException(
                        $"Cycle {iteration + 1}/{RepeatedCycles}: InitializeAsync faulted: {ex.Message}",
                        ex);
                }

                Assert.AreEqual(RuntimeState.Running, host.State,
                    $"Cycle {iteration + 1}: runtime must reach Running after InitializeAsync.");
                Assert.AreSame(host, CoreIpcRuntime.Current,
                    $"Cycle {iteration + 1}: runtime must register itself as CoreIpcRuntime.Current.");
                Assert.IsTrue(PlayerLoopInstaller.IsInstalled,
                    $"Cycle {iteration + 1}: PlayerLoop dispatch step must be installed during Running.");
                Assert.AreEqual(
                    initialPreUpdateChildCount + 1,
                    CountPreUpdateChildren(PlayerLoop.GetCurrentPlayerLoop()),
                    $"Cycle {iteration + 1}: exactly one IpcDispatchStep must be present under PreUpdate.");

                EditorPlayModeBridge.HandlePlayModeStateChange(
                    PlayModeStateChange.ExitingPlayMode,
                    currentRuntimeAccessor: () => CoreIpcRuntime.Current,
                    playerLoopUninstall: PlayerLoopInstaller.Uninstall);

                Assert.AreEqual(RuntimeState.Disposed, host.State,
                    $"Cycle {iteration + 1}: runtime must transition to Disposed after ExitingPlayMode.");
                Assert.IsNull(CoreIpcRuntime.Current,
                    $"Cycle {iteration + 1}: ExitingPlayMode must clear CoreIpcRuntime.Current.");
                Assert.IsFalse(PlayerLoopInstaller.IsInstalled,
                    $"Cycle {iteration + 1}: ExitingPlayMode must uninstall the PlayerLoop dispatch step.");
                Assert.AreEqual(
                    initialPreUpdateChildCount,
                    CountPreUpdateChildren(PlayerLoop.GetCurrentPlayerLoop()),
                    $"Cycle {iteration + 1}: PreUpdate child count must return to baseline (no leaked steps).");

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator RepeatedSimulatedPlayModeCycles_FixedPortRemainsBindable()
        {
            int port = FindFreeTcpPort();

            for (int iteration = 0; iteration < RepeatedCycles; iteration++)
            {
                var host = NewWebSocketHost();

                var initCts = new CancellationTokenSource(StartupTimeout);
                var initTask = host.InitializeAsync(FastOptions(port), initCts.Token);

                while (!initTask.IsCompleted)
                {
                    yield return null;
                }
                initCts.Dispose();

                if (initTask.IsFaulted)
                {
                    var ex = initTask.Exception?.GetBaseException() ?? initTask.Exception!;
                    throw new AssertionException(
                        $"Cycle {iteration + 1}/{RepeatedCycles}: InitializeAsync faulted on port "
                        + $"{port}: {ex.Message}",
                        ex);
                }

                Assert.AreEqual(RuntimeState.Running, host.State,
                    $"Cycle {iteration + 1}: runtime must reach Running on port {port}.");

                EditorPlayModeBridge.HandlePlayModeStateChange(
                    PlayModeStateChange.ExitingPlayMode,
                    currentRuntimeAccessor: () => CoreIpcRuntime.Current,
                    playerLoopUninstall: PlayerLoopInstaller.Uninstall);

                Assert.AreEqual(RuntimeState.Disposed, host.State,
                    $"Cycle {iteration + 1}: runtime must be Disposed after ExitingPlayMode "
                    + "(otherwise the port would still be bound by the previous cycle).");

                yield return null;
            }

            Assert.IsTrue(IsPortBindable(port),
                $"After {RepeatedCycles} simulated PlayMode cycles, port {port} must still be "
                + "bindable on 127.0.0.1; the previous lifecycle leaked the listener.");
        }

        [UnityTest]
        public IEnumerator PlayModeStop_ShutdownDoesNotTriggerReconnect()
        {
            int port = FindFreeTcpPort();
            var host = NewWebSocketHost();

            var initCts = new CancellationTokenSource(StartupTimeout);
            var initTask = host.InitializeAsync(FastOptions(port), initCts.Token);

            while (!initTask.IsCompleted)
            {
                yield return null;
            }
            initCts.Dispose();

            if (initTask.IsFaulted)
            {
                var ex = initTask.Exception?.GetBaseException() ?? initTask.Exception!;
                throw new AssertionException(
                    $"InitializeAsync faulted: {ex.Message}", ex);
            }

            Assert.AreEqual(RuntimeState.Running, host.State);

            // Capture the diagnostics reference up front: after Dispose, host.Bus throws.
            var diagnostics = host.Bus.Diagnostics;

            yield return WaitFor(
                () => diagnostics.CurrentState == ConnectionState.Connected,
                StartupTimeout,
                "Runtime never reached Connected within the timeout (current = "
                + diagnostics.CurrentState + ").");

            var transitionLog = new List<ConnectionState>();
            var transitionLogSync = new object();
            diagnostics.ConnectionStateChanged += (_, next) =>
            {
                lock (transitionLogSync) transitionLog.Add(next);
            };

            // Simulate a normal PlayMode stop via the editor bridge.
            EditorPlayModeBridge.HandlePlayModeStateChange(
                PlayModeStateChange.ExitingPlayMode,
                currentRuntimeAccessor: () => CoreIpcRuntime.Current,
                playerLoopUninstall: PlayerLoopInstaller.Uninstall);

            Assert.AreEqual(RuntimeState.Disposed, host.State,
                "ExitingPlayMode must place the runtime into Disposed.");

            // Give any straggling cancellation/dispose paths a chance to push
            // additional ConnectionStateChanged events.
            var observationDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
            while (DateTime.UtcNow < observationDeadline)
            {
                yield return null;
            }

            ConnectionState[] snapshot;
            lock (transitionLogSync)
            {
                snapshot = transitionLog.ToArray();
            }

            CollectionAssert.DoesNotContain(snapshot, ConnectionState.Reconnecting,
                "PlayMode stop must be treated as a normal shutdown (Req 5.8); the connection "
                + "state machine must not transition through Reconnecting after shutdown was "
                + "requested. Observed transitions after Connected: ["
                + string.Join(", ", snapshot) + "].");
            CollectionAssert.DoesNotContain(snapshot, ConnectionState.PermanentlyDisconnected,
                "PlayMode stop must not escalate to PermanentlyDisconnected. Observed "
                + "transitions after Connected: [" + string.Join(", ", snapshot) + "].");

            // Diagnostics is disposed during runtime shutdown but its CurrentState getter
            // still returns the last known value (the underlying state machine isn't cleared),
            // which lets us assert that we landed on Disconnected.
            Assert.AreEqual(
                ConnectionState.Disconnected,
                diagnostics.CurrentState,
                "After a clean PlayMode stop the connection should land on Disconnected, not "
                + "Reconnecting or PermanentlyDisconnected.");
        }

        // ---------- Helpers ----------

        private static CoreIpcRuntimeHost NewWebSocketHost()
        {
            ITransportAdapter Factory(CoreIpcOptions opts) =>
                new WebSocketTransportAdapter(
                    new SystemTextJsonCodec(opts),
                    new WebSocketServerOptions
                    {
                        MaxConcurrentClients = 4,
                        PingInterval = TimeSpan.Zero,
                        PongTimeout = TimeSpan.FromSeconds(60),
                        CloseTimeout = TimeSpan.FromSeconds(2),
                        HandshakeTimeout = TimeSpan.FromSeconds(5),
                    },
                    new WebSocketClientOptions
                    {
                        CloseTimeout = TimeSpan.FromSeconds(2),
                    });

            return new CoreIpcRuntimeHost(
                transportFactory: Factory,
                installPlayerLoop: true,
                registerAsCurrent: true,
                clientReconnectDelay: (delay, ct) =>
                    Task.Delay(TimeSpan.FromMilliseconds(20), ct));
        }

        private static CoreIpcOptions FastOptions(int port) => new()
        {
            Host = "127.0.0.1",
            Port = port,
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(20),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(40),
            ReconnectMaxAttempts = 3,
            DefaultRequestTimeout = TimeSpan.FromSeconds(5),
        };

        private static IEnumerator WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new AssertionException(failureMessage);
                }
                yield return null;
            }
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

        private static bool IsPortBindable(int port)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                listener?.Stop();
            }
        }

        private static int CountPreUpdateChildren(PlayerLoopSystem loop)
        {
            if (loop.subSystemList is null) return 0;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
                return loop.subSystemList[i].subSystemList?.Length ?? 0;
            }
            return 0;
        }
    }
}
#endif
