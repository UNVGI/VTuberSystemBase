#nullable enable
using System.Collections;
using System.Text.Json;
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
    /// IPC scenario coverage on top of the unit-level state tests in Task 3.5.
    /// Focuses on metadata propagation, volume override flow, and the volume
    /// metadata Request response (Requirement 3.x / 4.x / 5.x / 6.x / 7.x / 13.x).
    /// </summary>
    [TestFixture]
    public sealed class IpcHandlerIntegrationTests
    {
        private FakeOutputCommandDispatcher _dispatcher = null!;
        private FakeOutputSceneRoots _sceneRoots = null!;
        private FakeCameraIdAllocator _allocator = null!;
        private FakeOscReceiverHost _oscHost = null!;
        private FakeLocalVolumeBinder _volumeBinder = null!;
        private FakeVolumeOverrideSchemaResolver _schemaResolver = null!;
        private FakeCameraGameObjectFactory _factory = null!;
        private FakeCoreIpcBus _bus = null!;
        private FakeClock _clock = null!;
        private CameraSwitcherOutputAdapterConfig _config = null!;
        private CameraSwitcherOutputAdapter _adapter = null!;

        [SetUp]
        public void SetUp()
        {
            _dispatcher = new FakeOutputCommandDispatcher();
            _sceneRoots = new FakeOutputSceneRoots();
            _sceneRoots.BuildHierarchy();
            _allocator = new FakeCameraIdAllocator();
            _oscHost = new FakeOscReceiverHost();
            _volumeBinder = new FakeLocalVolumeBinder();
            _schemaResolver = FakeVolumeOverrideSchemaResolver.WithSchemas(new[]
            {
                new VolumeOverrideSchema { Type = "Bloom", DisplayName = "Bloom", Params = System.Array.Empty<VolumeParamSchema>() },
            });
            _factory = new FakeCameraGameObjectFactory();
            _bus = new FakeCoreIpcBus();
            _clock = new FakeClock();
            _config = ScriptableObject.CreateInstance<CameraSwitcherOutputAdapterConfig>();

            _adapter = new CameraSwitcherOutputAdapter(
                _dispatcher, _sceneRoots, _allocator, _oscHost, _volumeBinder, _schemaResolver,
                _factory, _bus, _clock, _config);
        }

        [TearDown]
        public void TearDown()
        {
            _adapter?.Dispose();
            _factory?.DestroyAllCreated();
            _volumeBinder?.DestroyAllCreated();
            _sceneRoots?.DestroyHierarchy();
            _dispatcher?.Dispose();
            if (_config != null) Object.Destroy(_config);
        }

        [UnityTest]
        public IEnumerator MetadataDisplayName_UpdatesEntryAndRepublishesList()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                PayloadFactory.AddCommand("g-1", CameraTypeNames.Perspective, "Old"));
            _bus.Reset();

            var metaPayload = new CameraMetadataStatePayload
            {
                Value = JsonDocument.Parse("\"NewName\"").RootElement,
            };
            _dispatcher.InvokeStateAt(CameraIpcTopics.CameraMetadata("cam-0001", CameraMetadataKeys.DisplayName), metaPayload);

            var entry = _adapter.Registry.Enumerate()[0];
            Assert.That(entry.DisplayName, Is.EqualTo("NewName"));
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.CamerasList), Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator MetadataType_TogglesOrthographic()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                PayloadFactory.AddCommand("g-1"));

            var entry = _adapter.Registry.Enumerate()[0];
            Assert.That(entry.CameraComponent!.orthographic, Is.False);

            var payload = new CameraMetadataStatePayload
            {
                Value = JsonDocument.Parse("\"Orthographic\"").RootElement,
            };
            _dispatcher.InvokeStateAt(CameraIpcTopics.CameraMetadata("cam-0001", CameraMetadataKeys.Type), payload);
            Assert.That(entry.CameraComponent.orthographic, Is.True);
        }

        [UnityTest]
        public IEnumerator VolumeCommandOverrideAdd_DelegatesToBinder()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));

            _dispatcher.InvokeEventAt(CameraIpcTopics.VolumeCommand("cam-0001"),
                new VolumeCommandPayload { Op = VolumeCommandOps.OverrideAdd, OverrideType = "Bloom" });

            var hadAddOverride = false;
            foreach (var call in _volumeBinder.Calls)
            {
                if (call.Kind == FakeLocalVolumeBinder.CallKind.AddOverride && call.OverrideType == "Bloom")
                    hadAddOverride = true;
            }
            Assert.That(hadAddOverride, Is.True);
        }

        [UnityTest]
        public IEnumerator VolumeMetadataRequest_ReturnsResolverResponse()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));

            var resp = _dispatcher.InvokeRequestAt<VolumeMetadataRequest, VolumeMetadataResponse>(
                CameraIpcTopics.VolumeOverridesMetadata("cam-0001"),
                new VolumeMetadataRequest { CameraId = "cam-0001" });

            Assert.That(resp.Overrides, Is.Not.Null);
            Assert.That(resp.Overrides.Count, Is.EqualTo(1));
            Assert.That(resp.Overrides[0].Type, Is.EqualTo("Bloom"));
            Assert.That(_schemaResolver.GetSchemaCallCount, Is.GreaterThanOrEqualTo(1));
        }
    }
}
