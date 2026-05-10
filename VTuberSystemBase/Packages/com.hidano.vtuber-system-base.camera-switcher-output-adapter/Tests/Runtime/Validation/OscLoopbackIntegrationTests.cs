#nullable enable
using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using uOSC;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Allocator;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Osc;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
using CameraSwitcherOutputAdapterCore = VTuberSystemBase.CameraSwitcherOutputAdapter.Domain.CameraSwitcherOutputAdapter;
namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Validation
{
    /// <summary>
    /// Integration test that drives a real <see cref="UoscReceiverHostAdapter"/>
    /// from a co-resident <see cref="uOscClient"/> with at least 1000 UCAPI Flat
    /// Records and verifies the adapter applies them to the matching Camera by
    /// cameraId (Requirement 1.1, 1.8, 2.x, 3.5, 13.2).
    /// </summary>
    [TestFixture]
    public sealed class OscLoopbackIntegrationTests
    {
        private const string Host = "127.0.0.1";
        private const int Port = 49210;

        [UnityTest]
        public IEnumerator OneCamera_PerCameraIdLoopback_AppliesTransform()
        {
            yield return null;
            var dispatcher = new FakeOutputCommandDispatcher();
            var sceneRoots = new FakeOutputSceneRoots();
            sceneRoots.BuildHierarchy();
            var allocator = new SequentialCameraIdAllocator();
            var oscHost = new UoscReceiverHostAdapter();
            var binder = new GlobalEnabledLocalVolumeBinder();
            var schemaResolver = FakeVolumeOverrideSchemaResolver.WithEmpty();
            var factory = new CameraGameObjectFactory(binder, new Vector2(36f, 24f));
            var bus = new FakeCoreIpcBus();
            var clock = new FakeClock();
            var config = ScriptableObject.CreateInstance<CameraSwitcherOutputAdapterConfig>();
            CameraSwitcherOutputAdapterCore? adapter = null;
            GameObject? clientGo = null;
            try
            {
                adapter = new CameraSwitcherOutputAdapterCore(
                    dispatcher, sceneRoots, allocator, oscHost, binder, schemaResolver, factory, bus, clock, config);
                // Replace OSC port via reflection of config? Instead start OSC manually with our test port.
                // We use a focused StartAsync directly.
                var startTask = oscHost.StartAsync(Host, Port);
                yield return new WaitUntil(() => startTask.IsCompleted);
                Assert.That(startTask.Result.Success, Is.True, startTask.Result.FailureDetail ?? "");

                var initTask = adapter.InitializeAsync();
                yield return new WaitUntil(() => initTask.IsCompleted);

                dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                    PayloadFactory.AddCommand("g-1", CameraTypeNames.Perspective, "Main"));
                Assert.That(adapter.CameraCount, Is.EqualTo(1));

                clientGo = new GameObject("[osc-client]") { hideFlags = HideFlags.HideAndDontSave };
                clientGo.SetActive(false);
                var client = clientGo.AddComponent<uOscClient>();
                client.address = Host;
                client.port = Port;
                clientGo.SetActive(true);
                yield return null;

                int sent = 0;
                const int total = 1200;
                Vector3 lastPos = Vector3.zero;
                for (var i = 0; i < total; i++)
                {
                    lastPos = new Vector3(i * 0.001f, 1f, -3f);
                    var blob = UcapiFlatRecordTestFactory.CreateBlob(lastPos, Vector3.zero, 50f);
                    client.Send("/ucapi/camera/cam-0001/flat", blob);
                    sent++;
                    if ((i & 0x3F) == 0) yield return null; // Periodically yield to let uOSC drain.
                }
                // Drain.
                for (var i = 0; i < 60; i++) yield return null;

                var camera = adapter.Registry.Enumerate()[0].CameraComponent!;
                Assert.That(sent, Is.GreaterThanOrEqualTo(1000));
                Assert.That(camera.transform.position.x, Is.EqualTo(lastPos.x).Within(1e-2f),
                    "last received message must be applied (last-write-wins)");
            }
            finally
            {
                if (clientGo != null) UnityEngine.Object.Destroy(clientGo);
                adapter?.Dispose();
                sceneRoots.DestroyHierarchy();
                dispatcher.Dispose();
                if (config != null) UnityEngine.Object.Destroy(config);
            }
        }
    }
}
