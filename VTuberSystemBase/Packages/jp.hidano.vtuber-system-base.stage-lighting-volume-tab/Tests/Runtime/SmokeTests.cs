#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Commands;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Smoke tests verifying the foundation layer (Task 1.2): the test asmdef compiles,
    /// NUnit is wired up, and the in-memory test doubles can be constructed and
    /// exercised end-to-end.
    /// </summary>
    [TestFixture]
    public sealed class SmokeTests
    {
        [Test]
        public void FakeIpcClient_PublishStateRecordsTopicAndPayload()
        {
            var ipc = new FakeIpcClient();

            var result = ipc.PublishState("light/abc/intensity", 1.5f);

            Assert.That(result.Success, Is.True);
            Assert.That(ipc.Sent, Has.Count.EqualTo(1));
            Assert.That(ipc.Sent[0].Topic, Is.EqualTo("light/abc/intensity"));
            Assert.That(ipc.Sent[0].Kind, Is.EqualTo(MessageKind.State));
            Assert.That(ipc.Sent[0].Payload, Is.EqualTo(1.5f));
        }

        [Test]
        public void FakeIpcClient_EmitDeliversOnlyToMatchingSubscribers()
        {
            var ipc = new FakeIpcClient();
            int received = 0;
            using var token = ipc.Subscribe<int>("topic-a", MessageKind.State, env =>
            {
                received = env.Payload;
            });

            int delivered = ipc.Emit("topic-a", 42);
            int dropped = ipc.Emit("topic-b", 99);

            Assert.That(delivered, Is.EqualTo(1));
            Assert.That(dropped, Is.EqualTo(0));
            Assert.That(received, Is.EqualTo(42));
        }
    }
}
