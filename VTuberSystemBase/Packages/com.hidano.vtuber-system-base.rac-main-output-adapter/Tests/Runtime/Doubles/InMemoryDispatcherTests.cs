using System;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    [TestFixture]
    public sealed class InMemoryDispatcherTests
    {
        public sealed record DemoPayload(string Value);

        [Test]
        public void RegisterAndEmitState_RoundTrips()
        {
            using var d = new InMemoryDispatcher();
            string captured = null;
            using var reg = d.RegisterStateHandler<DemoPayload>("topicA", cmd => captured = cmd.Payload?.Value);

            Assert.That(d.RegisteredHandlerCount, Is.EqualTo(1));
            var hit = d.EmitState("topicA", new DemoPayload("hello"));
            Assert.That(hit, Is.True);
            Assert.That(captured, Is.EqualTo("hello"));
        }

        [Test]
        public void DuplicateRegistration_Throws()
        {
            using var d = new InMemoryDispatcher();
            var r1 = d.RegisterStateHandler<DemoPayload>("topicA", _ => { });
            Assert.Throws<InvalidOperationException>(() =>
                d.RegisterStateHandler<DemoPayload>("topicA", _ => { }));
            r1.Dispose();
            // Dispose 後は再登録可能
            var r2 = d.RegisterStateHandler<DemoPayload>("topicA", _ => { });
            r2.Dispose();
        }

        [Test]
        public void DisposeRegistration_RemovesHandler()
        {
            using var d = new InMemoryDispatcher();
            var reg = d.RegisterStateHandler<DemoPayload>("topicA", _ => { });
            Assert.That(d.HasHandler("topicA", MessageKind.State), Is.True);
            reg.Dispose();
            Assert.That(d.HasHandler("topicA", MessageKind.State), Is.False);
            Assert.That(d.EmitState("topicA", new DemoPayload("x")), Is.False);
        }

        [Test]
        public void EventHandler_Routes()
        {
            using var d = new InMemoryDispatcher();
            int hits = 0;
            using var reg = d.RegisterEventHandler<DemoPayload>("topicE", _ => hits++);
            d.EmitEvent("topicE", new DemoPayload("a"));
            d.EmitEvent("topicE", new DemoPayload("b"));
            Assert.That(hits, Is.EqualTo(2));
        }

        [Test]
        public void RequestHandler_ReturnsResponse()
        {
            using var d = new InMemoryDispatcher();
            using var reg = d.RegisterRequestHandler<DemoPayload, DemoPayload>("topicR",
                cmd => new DemoPayload($"echo:{cmd.Payload?.Value}"));
            Assert.That(d.EmitRequest<DemoPayload, DemoPayload>("topicR", new DemoPayload("ping"), out var res), Is.True);
            Assert.That(res.Value, Is.EqualTo("echo:ping"));
        }

        [Test]
        public void RecordSent_AndGetSent_TracksHistory()
        {
            using var d = new InMemoryDispatcher();
            d.RecordSent("topicA", MessageKind.State, "payloadA");
            d.RecordSent("topicB", MessageKind.Event, "payloadB");
            d.RecordSent("topicA", MessageKind.State, "payloadC");
            Assert.That(d.GetSent("topicA"), Has.Count.EqualTo(2));
            Assert.That(d.GetSent("topicA", MessageKind.State), Has.Count.EqualTo(2));
            Assert.That(d.GetSent("topicB", MessageKind.Event), Has.Count.EqualTo(1));
        }
    }
}
