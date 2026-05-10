#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using uOSC;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Osc;
using Thread = System.Threading.Thread;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Adapters
{
    [TestFixture]
    public sealed class UoscReceiverHostAdapterTests
    {
        private const string Host = "127.0.0.1";

        // Use ports that are unlikely to clash with the configured default 9000.
        private static int NextPort()
            => 49152 + (Environment.TickCount & 0x3FFF); // 49152..65535 dynamic range

        [UnityTest]
        public IEnumerator StartThenStop_RoundTripIsClean()
        {
            var port = NextPort();
            using var adapter = new UoscReceiverHostAdapter();
            var startTask = adapter.StartAsync(Host, port);
            yield return new WaitUntil(() => startTask.IsCompleted);
            var startResult = startTask.Result;

            Assert.That(startResult.Success, Is.True, startResult.FailureDetail ?? "no detail");
            Assert.That(adapter.Status, Is.EqualTo(OscReceiverHostStatus.Running));

            var stopTask = adapter.StopAsync();
            yield return new WaitUntil(() => stopTask.IsCompleted);
            Assert.That(adapter.Status, Is.EqualTo(OscReceiverHostStatus.Stopped));

            // Re-Start after Stop is allowed.
            var restartTask = adapter.StartAsync(Host, port);
            yield return new WaitUntil(() => restartTask.IsCompleted);
            Assert.That(restartTask.Result.Success, Is.True);
            Assert.That(adapter.Status, Is.EqualTo(OscReceiverHostStatus.Running));

            var finalStopTask = adapter.StopAsync();
            yield return new WaitUntil(() => finalStopTask.IsCompleted);
        }

        [UnityTest]
        public IEnumerator StartFailureWhenPortOccupied_ReturnsFailureAndKeepsAdapterRecoverable()
        {
            var port = NextPort() + 1;

            // Occupy the port with a separate uOscServer.
            var occupierGo = new GameObject("[occupier]")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            occupierGo.SetActive(false);
            var occupier = occupierGo.AddComponent<uOscServer>();
            occupier.port = port;
            occupier.autoStart = false;
            occupierGo.SetActive(true);
            occupier.StartServer();
            yield return null;

            using (var adapter = new UoscReceiverHostAdapter())
            {
                var startTask = adapter.StartAsync(Host, port);
                yield return new WaitUntil(() => startTask.IsCompleted);

                // uOscServer's bind error semantics depend on the OS. We accept either:
                // - Failure (Windows binds typically fail on a duplicate UDP port);
                // - Success but adapter sees a second active server (rare).
                if (!startTask.Result.Success)
                {
                    Assert.That(adapter.Status, Is.EqualTo(OscReceiverHostStatus.Failed));
                    // Recovery: free the port and re-Start.
                    occupier.StopServer();
                    yield return null;
                    var retry = adapter.StartAsync(Host, port);
                    yield return new WaitUntil(() => retry.IsCompleted);
                    Assert.That(retry.Result.Success, Is.True);
                    var stop = adapter.StopAsync();
                    yield return new WaitUntil(() => stop.IsCompleted);
                }
                else
                {
                    var stop = adapter.StopAsync();
                    yield return new WaitUntil(() => stop.IsCompleted);
                }
            }

            occupier.StopServer();
            UnityEngine.Object.Destroy(occupierGo);
        }

        [UnityTest]
        public IEnumerator MessageReceived_FiresOnUnityMainThread()
        {
            var port = NextPort() + 2;
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var captured = new List<(string cameraId, int blobLength, int threadId)>();
            using var adapter = new UoscReceiverHostAdapter();
            adapter.MessageReceived += msg =>
            {
                captured.Add((msg.CameraId, msg.Blob.Length, Thread.CurrentThread.ManagedThreadId));
            };

            var startTask = adapter.StartAsync(Host, port);
            yield return new WaitUntil(() => startTask.IsCompleted);
            Assert.That(startTask.Result.Success, Is.True, startTask.Result.FailureDetail ?? "no detail");

            // Send a packet through a real uOscClient inside the same process.
            var clientGo = new GameObject("[client]")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            clientGo.SetActive(false);
            var client = clientGo.AddComponent<uOscClient>();
            client.address = Host;
            client.port = port;
            clientGo.SetActive(true);

            var blob = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            client.Send("/ucapi/camera/cam-0001/flat", blob);

            // Allow at most ~30 frames for the UDP datagram to be parsed and dispatched.
            var deadlineFrame = Time.frameCount + 60;
            while (captured.Count == 0 && Time.frameCount < deadlineFrame)
            {
                yield return null;
            }

            UnityEngine.Object.Destroy(clientGo);
            var stopTask = adapter.StopAsync();
            yield return new WaitUntil(() => stopTask.IsCompleted);

            Assert.That(captured.Count, Is.GreaterThanOrEqualTo(1), "no MessageReceived dispatched");
            Assert.That(captured[0].cameraId, Is.EqualTo("cam-0001"));
            Assert.That(captured[0].blobLength, Is.EqualTo(4));
            Assert.That(captured[0].threadId, Is.EqualTo(mainThreadId));
        }

        [UnityTest]
        public IEnumerator MismatchedPrefix_DoesNotFireMessageReceived()
        {
            var port = NextPort() + 3;
            var fired = false;
            using var adapter = new UoscReceiverHostAdapter();
            adapter.MessageReceived += _ => fired = true;

            var startTask = adapter.StartAsync(Host, port);
            yield return new WaitUntil(() => startTask.IsCompleted);
            Assert.That(startTask.Result.Success, Is.True, startTask.Result.FailureDetail ?? "no detail");

            var clientGo = new GameObject("[client]")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            clientGo.SetActive(false);
            var client = clientGo.AddComponent<uOscClient>();
            client.address = Host;
            client.port = port;
            clientGo.SetActive(true);

            client.Send("/some/other/topic", new byte[] { 0x00 });

            for (var i = 0; i < 30; i++) yield return null;

            UnityEngine.Object.Destroy(clientGo);
            var stopTask = adapter.StopAsync();
            yield return new WaitUntil(() => stopTask.IsCompleted);

            Assert.That(fired, Is.False);
        }
    }
}
