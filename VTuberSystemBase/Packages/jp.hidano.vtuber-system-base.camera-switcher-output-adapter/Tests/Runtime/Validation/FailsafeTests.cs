#nullable enable
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

using CameraSwitcherOutputAdapterCore = VTuberSystemBase.CameraSwitcherOutputAdapter.Domain.CameraSwitcherOutputAdapter;
namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Validation
{
    /// <summary>
    /// Failsafe scenarios that ensure the adapter survives partial failures
    /// (Requirement 1.4 / 2.2 / 2.4 / 3.4 / 3.7 / 6.9 / 6.10 / 7.8 / 12.x).
    /// </summary>
    [TestFixture]
    public sealed class FailsafeTests
    {
        private FakeOutputCommandDispatcher _dispatcher = null!;
        private FakeOutputSceneRoots _sceneRoots = null!;
        private FakeOscReceiverHost _oscHost = null!;
        private FakeLocalVolumeBinder _volumeBinder = null!;
        private FakeCameraGameObjectFactory _factory = null!;
        private FakeCoreIpcBus _bus = null!;
        private CameraSwitcherOutputAdapterConfig _config = null!;

        [SetUp]
        public void SetUp()
        {
            _dispatcher = new FakeOutputCommandDispatcher();
            _sceneRoots = new FakeOutputSceneRoots();
            _sceneRoots.BuildHierarchy();
            _oscHost = new FakeOscReceiverHost();
            _volumeBinder = new FakeLocalVolumeBinder();
            _factory = new FakeCameraGameObjectFactory();
            _bus = new FakeCoreIpcBus();
            _config = ScriptableObject.CreateInstance<CameraSwitcherOutputAdapterConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            _factory?.DestroyAllCreated();
            _volumeBinder?.DestroyAllCreated();
            _sceneRoots?.DestroyHierarchy();
            _dispatcher?.Dispose();
            if (_config != null) Object.Destroy(_config);
        }

        [UnityTest]
        public IEnumerator OscStartupFailure_PublishesCameraErrorButKeepsIpcAlive()
        {
            _oscHost.NextStartResult = OscReceiverStartResult.Failure("port in use");
            using var adapter = NewAdapter(FakeVolumeOverrideSchemaResolver.WithEmpty());
            var initTask = adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);

            // camera/error with OscStartupFailed should be in the bus.
            var found = false;
            foreach (var ev in _bus.PublishedEvents)
            {
                if (ev.Topic == CameraIpcTopics.CameraError && ev.Payload is CameraErrorEventPayload p && p.Reason == "OscStartupFailed")
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True);

            // IPC still works.
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));
            Assert.That(adapter.CameraCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UnknownCameraIdOnIpc_PublishesCameraError()
        {
            using var adapter = NewAdapter(FakeVolumeOverrideSchemaResolver.WithEmpty());
            var initTask = adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _bus.Reset();

            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand,
                PayloadFactory.ActiveSetCommand("g-9", "cam-9999"));

            Assert.That(_bus.PublishedEvents.Count, Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator UcapiDecodeFailure_DoesNotPublishCameraError()
        {
            using var adapter = NewAdapter(FakeVolumeOverrideSchemaResolver.WithEmpty());
            var initTask = adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));
            _bus.Reset();

            // Send a malformed blob to the registered cameraId.
            _oscHost.Emit("cam-0001", new byte[] { 0x00, 0x01, 0x02 });

            // No camera/error event for OSC decode failures (Req 2.4).
            var oscErrorCount = 0;
            foreach (var ev in _bus.PublishedEvents)
            {
                if (ev.Topic == CameraIpcTopics.CameraError) oscErrorCount++;
            }
            Assert.That(oscErrorCount, Is.EqualTo(0));
            Assert.That(adapter.Failures.CountOf(FailureKind.OscDecodeFailed), Is.GreaterThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator VolumeMetadataResolverThrowing_ReturnsEmptySchema()
        {
            using var adapter = NewAdapter(FakeVolumeOverrideSchemaResolver.Throwing(new InvalidOperationException("boom")));
            var initTask = adapter.InitializeAsync();
            yield return new WaitUntil(() => initTask.IsCompleted);
            _dispatcher.InvokeEventAt(CameraIpcTopics.CameraCommand, PayloadFactory.AddCommand("g-1"));

            var resp = _dispatcher.InvokeRequestAt<VolumeMetadataRequest, VolumeMetadataResponse>(
                CameraIpcTopics.VolumeOverridesMetadata("cam-0001"),
                new VolumeMetadataRequest { CameraId = "cam-0001" });
            Assert.That(resp.Overrides, Is.Not.Null);
            Assert.That(resp.Overrides.Count, Is.EqualTo(0));
            Assert.That(adapter.Failures.CountOf(FailureKind.ReflectionFailed), Is.GreaterThanOrEqualTo(1));
        }

        private CameraSwitcherOutputAdapterCore NewAdapter(IVolumeOverrideSchemaResolver schemaResolver) =>
            new CameraSwitcherOutputAdapterCore(
                _dispatcher, _sceneRoots, new FakeCameraIdAllocator(), _oscHost, _volumeBinder,
                schemaResolver, _factory, _bus, new FakeClock(), _config);
    }
}
