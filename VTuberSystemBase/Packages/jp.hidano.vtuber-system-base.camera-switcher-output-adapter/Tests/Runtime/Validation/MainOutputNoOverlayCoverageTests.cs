#nullable enable
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Validation
{
    /// <summary>
    /// Verifies that none of the GameObjects authored by this spec attach UI
    /// rendering components or target a non-zero display (Requirement 12.1, OR-1,
    /// 5.6).
    /// </summary>
    [TestFixture]
    public sealed class MainOutputNoOverlayCoverageTests
    {
        [UnityTest]
        public IEnumerator FactoryCreatedHierarchies_HaveNoUiRenderingComponentsOrNonZeroTargetDisplay()
        {
            yield return null;
            var dispatcher = new FakeOutputCommandDispatcher();
            var sceneRoots = new FakeOutputSceneRoots();
            sceneRoots.BuildHierarchy();
            var binder = new GlobalEnabledLocalVolumeBinder();
            var factory = new CameraGameObjectFactory(binder, new Vector2(36f, 24f));
            var allocator = new FakeCameraIdAllocator();
            var oscHost = new FakeOscReceiverHost();
            var schemaResolver = FakeVolumeOverrideSchemaResolver.WithEmpty();
            var bus = new FakeCoreIpcBus();
            var clock = new FakeClock();
            var config = ScriptableObject.CreateInstance<CameraSwitcherOutputAdapterConfig>();
            CameraSwitcherOutputAdapter? adapter = null;
            try
            {
                adapter = new CameraSwitcherOutputAdapter(
                    dispatcher, sceneRoots, allocator, oscHost, binder, schemaResolver, factory, bus, clock, config);
                var initTask = adapter.InitializeAsync();
                yield return new WaitUntil(() => initTask.IsCompleted);
                dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1", CameraTypeNames.Perspective, "Main"));

                // Inspect the produced cameras subtree.
                var cameraEntries = adapter.Registry.Enumerate();
                Assert.That(cameraEntries.Count, Is.EqualTo(1));
                foreach (var entry in cameraEntries)
                {
                    var go = entry.GameObject!;
                    AssertNoUiRendering(go);
                    Assert.That(entry.CameraComponent!.targetDisplay, Is.EqualTo(0),
                        $"Camera {go.name} must default to display 0 (RDS controls non-zero displays).");
                }
            }
            finally
            {
                adapter?.Dispose();
                sceneRoots.DestroyHierarchy();
                dispatcher.Dispose();
                if (config != null) UnityEngine.Object.Destroy(config);
            }
        }

        private static void AssertNoUiRendering(GameObject root)
        {
            var allChildren = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in allChildren)
            {
                AssertNoOnGuiOrUi(t.gameObject);
            }
        }

        private static void AssertNoOnGuiOrUi(GameObject go)
        {
            // Rejects any IMGUI / UI Toolkit attachment.
            var uiDocs = go.GetComponents<UIDocument>();
            Assert.That(uiDocs.Length, Is.EqualTo(0), $"{go.name} attaches a UIDocument");
            var allBehaviours = go.GetComponents<MonoBehaviour>();
            foreach (var b in allBehaviours)
            {
                if (b == null) continue;
                var t = b.GetType();
                Assert.That(t.Namespace?.StartsWith("UnityEngine.UIElements") ?? false, Is.False,
                    $"{go.name} hosts UnityEngine.UIElements component {t.Name}");
            }
        }
    }
}
