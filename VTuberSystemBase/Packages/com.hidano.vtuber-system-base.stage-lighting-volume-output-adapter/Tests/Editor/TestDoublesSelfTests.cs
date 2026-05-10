#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class TestDoublesSelfTests
    {
        [Test]
        public void RecordingMessageSink_RecordsStateAndEvent()
        {
            var sink = new RecordingMessageSink();
            Assert.That(sink.PublishState("a", 1), Is.True);
            Assert.That(sink.PublishEvent("b", "x"), Is.True);
            Assert.That(sink.PublishedStates.Count, Is.EqualTo(1));
            Assert.That(sink.PublishedStates[0].Topic, Is.EqualTo("a"));
            Assert.That(sink.PublishedStates[0].Payload, Is.EqualTo(1));
            Assert.That(sink.PublishedEvents.Count, Is.EqualTo(1));
            Assert.That(sink.PublishedEvents[0].Topic, Is.EqualTo("b"));
            Assert.That(sink.PublishedEvents[0].Payload, Is.EqualTo("x"));
        }

        [Test]
        public void FakeOutputCommandDispatcher_RegisterAndEmit_State()
        {
            var d = new FakeOutputCommandDispatcher();
            int hits = 0;
            string? last = null;
            using (d.RegisterStateHandler<string>("topic/a", cmd => { hits++; last = cmd.Payload; }))
            {
                Assert.That(d.RegisteredHandlerCount, Is.EqualTo(1));
                d.EmitState("topic/a", "hello");
                Assert.That(hits, Is.EqualTo(1));
                Assert.That(last, Is.EqualTo("hello"));
            }
            // After dispose, no longer registered.
            Assert.That(d.RegisteredHandlerCount, Is.EqualTo(0));
            d.EmitState("topic/a", "ignored");
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(d.DisposedRegistrations.Single(), Is.EqualTo(("topic/a", "state")));
        }

        [Test]
        public void FakeOutputCommandDispatcher_Event_FifoOrder()
        {
            var d = new FakeOutputCommandDispatcher();
            var seen = new System.Collections.Generic.List<int>();
            using var token = d.RegisterEventHandler<int>("evt", cmd => seen.Add(cmd.Payload));
            d.EmitEvent("evt", 1);
            d.EmitEvent("evt", 2);
            d.EmitEvent("evt", 3);
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void FakeOutputCommandDispatcher_Request_RoundTrip()
        {
            var d = new FakeOutputCommandDispatcher();
            using var token = d.RegisterRequestHandler<string, int>("req", cmd => (cmd.Payload ?? "").Length);
            var result = d.InvokeRequest<string, int>("req", "hello");
            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public void FakeOutputCommandDispatcher_NullArgs_Throw()
        {
            var d = new FakeOutputCommandDispatcher();
            Assert.Throws<System.ArgumentException>(() => d.RegisterStateHandler<int>("", cmd => { }));
            Assert.Throws<System.ArgumentNullException>(() => d.RegisterEventHandler<int>("t", null!));
        }

        [Test]
        public void FakeOutputSceneRoots_HasAllRoots()
        {
            using var roots = new FakeOutputSceneRoots();
            Assert.That(roots.Stage, Is.Not.Null);
            Assert.That(roots.Characters, Is.Not.Null);
            Assert.That(roots.Lights, Is.Not.Null);
            Assert.That(roots.Cameras, Is.Not.Null);
            Assert.That(roots.Volumes, Is.Not.Null);
            Assert.That(roots.GlobalVolumeProfile, Is.Not.Null);
            Assert.That(roots.DefaultCamera, Is.Not.Null);
            // DefaultCamera should be parented under Cameras.
            Assert.That(roots.DefaultCamera!.transform.parent, Is.EqualTo(roots.Cameras));
        }

        [Test]
        public void FakeInstantiationProvider_Configure_ReturnsConfigured()
        {
            var p = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeInstantiationProvider();
            p.Configure("Stages/Default");
            using var roots = new FakeOutputSceneRoots();
            var task = p.InstantiateAsync("Stages/Default", roots.Stage);
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result.Success, Is.True);
            Assert.That(task.Result.Instance, Is.Not.Null);
            Assert.That(p.InstantiatedKeys.Single(), Is.EqualTo("Stages/Default"));

            // Unknown key returns not_found.
            var task2 = p.InstantiateAsync("Unknown", roots.Stage);
            Assert.That(task2.Result.Success, Is.False);
            Assert.That(task2.Result.ErrorCode, Is.EqualTo("not_found"));
        }

        [Test]
        public void FakeInstantiationProvider_ReleaseInstance_Records()
        {
            var p = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeInstantiationProvider();
            p.Configure("k");
            using var roots = new FakeOutputSceneRoots();
            var go = p.InstantiateAsync("k", roots.Stage).Result.Instance!;
            p.ReleaseInstance(go);
            Assert.That(p.ReleasedInstances.Count, Is.EqualTo(1));
        }

        [Test]
        public void FakeInstantiationProvider_LoadResourceLocations_ReturnsConfigured()
        {
            var p = new VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor.FakeInstantiationProvider();
            p.ConfigureLabel("stage", new[] { "a", "b" });
            var locs = p.LoadResourceLocationsAsync("stage").Result;
            Assert.That(locs, Is.EqualTo(new[] { "a", "b" }));

            var empty = p.LoadResourceLocationsAsync("missing").Result;
            Assert.That(empty, Is.Empty);
        }
    }
}
