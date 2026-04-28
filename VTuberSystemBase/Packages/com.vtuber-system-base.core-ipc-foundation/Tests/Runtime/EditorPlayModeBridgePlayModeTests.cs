#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
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
    public sealed class EditorPlayModeBridgePlayModeTests
    {
        private const int RepeatedCycles = 5;
        private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);

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
        public IEnumerator RepeatedSimulatedPlayModeCycles_KeepPortBindable()
        {
            int port = FindFreeTcpPort();

            for (int iteration = 0; iteration < RepeatedCycles; iteration++)
            {
                var transport = new WebSocketTransportAdapter(
                    new SystemTextJsonCodec(),
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

                var host = new CoreIpcRuntimeHost(
                    transportFactory: _ => transport,
                    installPlayerLoop: true,
                    registerAsCurrent: true,
                    clientReconnectDelay: (delay, ct) =>
                        Task.Delay(TimeSpan.FromMilliseconds(20), ct));

                var initCts = new CancellationTokenSource(StartupTimeout);
                var initTask = host.InitializeAsync(
                    new CoreIpcOptions { Host = "127.0.0.1", Port = port },
                    initCts.Token);

                while (!initTask.IsCompleted)
                {
                    yield return null;
                }
                initCts.Dispose();

                if (initTask.IsFaulted)
                {
                    var ex = initTask.Exception?.GetBaseException() ?? initTask.Exception!;
                    throw new AssertionException(
                        $"Cycle {iteration + 1}/{RepeatedCycles}: InitializeAsync faulted: {ex.Message}", ex);
                }

                Assert.AreEqual(RuntimeState.Running, host.State,
                    $"Cycle {iteration + 1}: runtime must be Running after InitializeAsync.");
                Assert.AreSame(host, CoreIpcRuntime.Current,
                    $"Cycle {iteration + 1}: runtime must register itself as CoreIpcRuntime.Current.");
                Assert.IsTrue(PlayerLoopInstaller.IsInstalled,
                    $"Cycle {iteration + 1}: PlayerLoop dispatch step must be installed.");

                EditorPlayModeBridge.HandlePlayModeStateChange(
                    PlayModeStateChange.ExitingPlayMode,
                    currentRuntimeAccessor: () => CoreIpcRuntime.Current,
                    playerLoopUninstall: PlayerLoopInstaller.Uninstall);

                Assert.AreEqual(RuntimeState.Disposed, host.State,
                    $"Cycle {iteration + 1}: runtime must be Disposed after the bridge handles ExitingPlayMode.");
                Assert.IsNull(CoreIpcRuntime.Current,
                    $"Cycle {iteration + 1}: ExitingPlayMode must clear CoreIpcRuntime.Current.");
                Assert.IsFalse(PlayerLoopInstaller.IsInstalled,
                    $"Cycle {iteration + 1}: ExitingPlayMode must remove PlayerLoop dispatch step.");

                // Yield once to let any background tasks observe cancellation before the next cycle
                // attempts to bind the same port.
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
    }
}
#endif
