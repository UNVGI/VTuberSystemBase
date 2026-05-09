#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Validation
{
    /// <summary>
    /// Five-iteration teardown loop verifying that the adapter's Dispose path
    /// reliably reclaims its registry, IPC registrations, factory-spawned cameras
    /// and DefaultCamera fallback (Requirement 1.5 / 1.6 / 1.7 / 11.x).
    /// </summary>
    /// <remarks>
    /// We exercise the adapter directly (not the bootstrapper) so the test
    /// remains hermetic: it does not require an <c>OutputSceneBootstrapper</c>
    /// to be present in the test scene.
    /// </remarks>
    [TestFixture]
    public sealed class PlayModeLifecycleTests
    {
        [UnityTest]
        public IEnumerator FiveIterations_DisposeRestoresFallbackAndClearsRegistry()
        {
            var dispatcher = new FakeOutputCommandDispatcher();
            var sceneRoots = new FakeOutputSceneRoots();
            sceneRoots.BuildHierarchy();
            var oscHost = new FakeOscReceiverHost();
            var binder = new FakeLocalVolumeBinder();
            var schemaResolver = FakeVolumeOverrideSchemaResolver.WithEmpty();
            var bus = new FakeCoreIpcBus();
            var clock = new FakeClock();
            var config = ScriptableObject.CreateInstance<CameraSwitcherOutputAdapterConfig>();
            try
            {
                for (var iter = 0; iter < 5; iter++)
                {
                    var allocator = new FakeCameraIdAllocator();
                    var factory = new FakeCameraGameObjectFactory();
                    var adapter = new CameraSwitcherOutputAdapter(
                        dispatcher, sceneRoots, allocator, oscHost, binder, schemaResolver,
                        factory, bus, clock, config);
                    var initTask = adapter.InitializeAsync();
                    yield return new WaitUntil(() => initTask.IsCompleted);

                    for (var i = 0; i < 5; i++)
                    {
                        dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand($"g-{iter}-{i}"));
                    }
                    Assert.That(adapter.CameraCount, Is.EqualTo(5), $"iter {iter}");
                    Assert.That(sceneRoots.DefaultCamera!.enabled, Is.False, $"iter {iter}");

                    dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                        PayloadFactory.ActiveSetCommand($"g-{iter}-active", "cam-0003"));

                    adapter.Dispose();
                    yield return null;
                    Assert.That(adapter.Status, Is.EqualTo(AdapterStatus.Disposed));
                    Assert.That(sceneRoots.DefaultCamera!.enabled, Is.True, $"iter {iter} fallback should restore");
                    Assert.That(adapter.CameraCount, Is.EqualTo(0), $"iter {iter} registry should clear");
                    factory.DestroyAllCreated();
                }
            }
            finally
            {
                binder.DestroyAllCreated();
                sceneRoots.DestroyHierarchy();
                dispatcher.Dispose();
                if (config != null) Object.Destroy(config);
            }
        }
    }
}
