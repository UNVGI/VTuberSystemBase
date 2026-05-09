#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Runtime
{
    [TestFixture]
    public sealed class IpcHandlerRegistrationTests
    {
        [Test]
        public void RegisterAll_AddsThreeStaticHandlers()
        {
            var dispatcher = new FakeOutputCommandDispatcher();
            var sceneRoots = new FakeOutputSceneRoots();
            sceneRoots.BuildHierarchy();
            try
            {
                var allocator = new FakeCameraIdAllocator();
                var oscHost = new FakeOscReceiverHost();
                var binder = new FakeLocalVolumeBinder();
                var schemaResolver = FakeVolumeOverrideSchemaResolver.WithEmpty();
                var factory = new FakeCameraGameObjectFactory();
                var bus = new FakeCoreIpcBus();
                var clock = new FakeClock();
                var config = ScriptableObject.CreateInstance<CameraSwitcherOutputAdapterConfig>();
                try
                {
                    var adapter = new CameraSwitcherOutputAdapter(
                        dispatcher, sceneRoots, allocator, oscHost, binder, schemaResolver,
                        factory, bus, clock, config);

                    var registration = new IpcHandlerRegistration();
                    registration.RegisterAll(dispatcher, adapter);

                    Assert.That(registration.RegisteredHandlerCount, Is.EqualTo(3));
                    Assert.That(dispatcher.EventHandlers.ContainsKey(CameraIpcTopics.CameraCommand), Is.True);
                    Assert.That(dispatcher.EventHandlers.ContainsKey(CameraIpcTopics.PreviewCommand), Is.True);
                    Assert.That(dispatcher.EventHandlers.ContainsKey(CameraIpcTopics.PresetCommand), Is.True);

                    registration.Dispose();
                    Assert.That(registration.RegisteredHandlerCount, Is.EqualTo(0));
                    Assert.That(dispatcher.EventHandlers.Count, Is.EqualTo(0));

                    adapter.Dispose();
                }
                finally
                {
                    Object.DestroyImmediate(config);
                }
            }
            finally
            {
                sceneRoots.DestroyHierarchy();
                dispatcher.Dispose();
            }
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var registration = new IpcHandlerRegistration();
            registration.Dispose();
            registration.Dispose();
            Assert.Pass();
        }
    }
}
