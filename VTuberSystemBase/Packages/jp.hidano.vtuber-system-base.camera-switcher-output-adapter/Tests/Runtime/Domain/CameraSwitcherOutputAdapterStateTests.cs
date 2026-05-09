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

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class CameraSwitcherOutputAdapterStateTests
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
            _schemaResolver = FakeVolumeOverrideSchemaResolver.WithEmpty();
            _factory = new FakeCameraGameObjectFactory();
            _bus = new FakeCoreIpcBus();
            _clock = new FakeClock(1000);
            _config = ScriptableObject.CreateInstance<CameraSwitcherOutputAdapterConfig>();

            _adapter = new CameraSwitcherOutputAdapter(
                _dispatcher, _sceneRoots, _allocator, _oscHost, _volumeBinder,
                _schemaResolver, _factory, _bus, _clock, _config);
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
        public IEnumerator AddCommand_AllocatesAndPublishesCreatedAndList()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);

            // Initial publish: cameras/list (empty) + cameras/active (null).
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.CamerasList), Is.GreaterThanOrEqualTo(1));
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.CamerasActive), Is.GreaterThanOrEqualTo(1));

            _bus.Reset();
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                PayloadFactory.AddCommand("g-1", CameraTypeNames.Perspective, "Main"));

            Assert.That(_allocator.AllocateCallCount, Is.EqualTo(1));
            Assert.That(_factory.CreatedEntries.Count, Is.EqualTo(1));
            Assert.That(_adapter.CameraCount, Is.EqualTo(1));
            Assert.That(AssertEnvelope.CountEvents(_bus, CameraIpcTopics.CameraCreated), Is.EqualTo(1));
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.CamerasList), Is.EqualTo(1));

            var created = AssertEnvelope.SingleEventPayload<CameraCreatedEventPayload>(_bus, CameraIpcTopics.CameraCreated);
            Assert.That(created.ClientRequestId, Is.EqualTo("g-1"));
            Assert.That(created.CameraId, Is.EqualTo("cam-0001"));
        }

        [UnityTest]
        public IEnumerator DeleteCommand_RemovesFromRegistryAndPublishesList()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));
            var addedId = _factory.CreatedEntries[0].CameraId.Value;

            _bus.Reset();
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                PayloadFactory.DeleteCommand("g-2", addedId));

            Assert.That(_adapter.CameraCount, Is.EqualTo(0));
            Assert.That(_factory.DestroyedCameraIds.Count, Is.EqualTo(1));
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.CamerasList), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ActiveSetCommand_PublishesActiveAndPerCameraVolumeEnabled()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-2"));

            _bus.Reset();
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                PayloadFactory.ActiveSetCommand("g-3", "cam-0002"));

            // cameras/active publish.
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.CamerasActive), Is.EqualTo(1));
            var active = AssertEnvelope.SingleStatePayload<CamerasActiveStatePayload>(_bus, CameraIpcTopics.CamerasActive);
            Assert.That(active.ActiveCameraId, Is.EqualTo("cam-0002"));

            // Per-camera volume/enabled publishes.
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.VolumeEnabled("cam-0001")), Is.EqualTo(1));
            Assert.That(AssertEnvelope.CountStates(_bus, CameraIpcTopics.VolumeEnabled("cam-0002")), Is.EqualTo(1));

            // Camera + Volume enabled flips.
            Assert.That(_factory.CreatedEntries[0].CameraComponent!.enabled, Is.False);
            Assert.That(_factory.CreatedEntries[1].CameraComponent!.enabled, Is.True);
            Assert.That(_factory.CreatedEntries[0].LocalVolume!.enabled, Is.False);
            Assert.That(_factory.CreatedEntries[1].LocalVolume!.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator UnknownCameraIdOnDelete_PublishesCameraError()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);

            _bus.Reset();
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                PayloadFactory.DeleteCommand("g-1", "cam-9999"));

            Assert.That(AssertEnvelope.CountEvents(_bus, CameraIpcTopics.CameraError), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OscMessageReceived_AppliesToMatchingCamera()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));

            // Use the real UCAPI factory so the blob actually decodes.
            var blob = UcapiFlatRecordTestFactory.CreateBlob(new Vector3(2f, 3f, 4f), Vector3.zero, 60f);
            _oscHost.Emit("cam-0001", blob);

            var camera = _factory.CreatedEntries[0].CameraComponent;
            Assert.That(camera!.transform.position.x, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(camera.transform.position.z, Is.EqualTo(4f).Within(1e-3f));
            Assert.That(camera.focalLength, Is.EqualTo(60f).Within(1e-2f));
        }

        [UnityTest]
        public IEnumerator Dispose_RestoresDefaultCameraAndClearsRegistry()
        {
            yield return null;
            var initTask = _adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));
            Assert.That(_sceneRoots.DefaultCamera!.enabled, Is.False);

            _adapter.Dispose();
            yield return null;
            Assert.That(_sceneRoots.DefaultCamera!.enabled, Is.True);
            Assert.That(_adapter.Status, Is.EqualTo(AdapterStatus.Disposed));
        }
    }
}
