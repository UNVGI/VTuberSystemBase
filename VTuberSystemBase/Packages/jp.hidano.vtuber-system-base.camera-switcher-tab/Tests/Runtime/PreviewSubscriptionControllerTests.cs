#nullable enable
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    [TestFixture]
    public sealed class PreviewSubscriptionControllerTests
    {
        private FakeUiCommandClient _commands = null!;
        private FakePreviewHandleResolver _resolver = null!;
        private PreviewSubscriptionController _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _commands = new FakeUiCommandClient();
            _resolver = new FakePreviewHandleResolver();
            _sut = new PreviewSubscriptionController(_commands, _resolver);
        }

        [TearDown]
        public void TearDown() => _sut.Dispose();

        [Test]
        public void Attach_PublishesAttachCommand_WithIdsAndSize()
        {
            _sut.Attach(new[] { new CameraId("cam-1"), new CameraId("cam-2") }, 320, 180, 15);
            Assert.AreEqual(1, _commands.Sent.Count);
            var rec = _commands.Sent[0];
            Assert.AreEqual(CameraIpcTopics.PreviewCommand, rec.Topic);
            var p = (PreviewCommandPayload)rec.Payload!;
            Assert.AreEqual(PreviewCommandOps.Attach, p.Op);
            CollectionAssert.AreEquivalent(new[] { "cam-1", "cam-2" }, p.CameraIds);
            Assert.AreEqual(15, p.Fps);
            Assert.IsTrue(_sut.IsAttachActive);
        }

        [Test]
        public async Task OnHandleState_ResolvesAndCachesHandle()
        {
            var stub = new object();
            _resolver.Handles["tex-cam-1"] = stub;

            _sut.Attach(new[] { new CameraId("cam-1") }, 192, 108, 15);
            await _sut.OnHandleStateAsync(new CameraId("cam-1"), new PreviewHandleStatePayload
            {
                TextureKey = "tex-cam-1",
                Size = new[] { 192, 108 },
                Fps = 15,
            });

            Assert.IsTrue(_sut.Slots.TryGetValue("cam-1", out var slot));
            Assert.AreSame(stub, slot.Handle);
            Assert.IsFalse(slot.ResolveFailed);
            CollectionAssert.Contains(_resolver.Resolved, "tex-cam-1");
        }

        [Test]
        public async Task OnHandleState_MissingKey_MarksResolveFailed()
        {
            _sut.Attach(new[] { new CameraId("cam-1") }, 192, 108, 15);
            await _sut.OnHandleStateAsync(new CameraId("cam-1"), new PreviewHandleStatePayload
            {
                TextureKey = "missing-tex",
                Size = new[] { 192, 108 },
                Fps = 15,
            });

            Assert.IsTrue(_sut.Slots.TryGetValue("cam-1", out var slot));
            Assert.IsTrue(slot.ResolveFailed);
            Assert.IsNull(slot.Handle);
        }

        [Test]
        public async Task DetachOne_ReleasesHandleAndPublishesDetach()
        {
            _resolver.Handles["k1"] = new object();
            _sut.Attach(new[] { new CameraId("cam-1") }, 192, 108, 15);
            await _sut.OnHandleStateAsync(new CameraId("cam-1"), new PreviewHandleStatePayload { TextureKey = "k1", Size = new[] { 192, 108 }, Fps = 15 });

            _commands.Sent.Clear();
            _sut.DetachOne(new CameraId("cam-1"));

            Assert.AreEqual(1, _commands.Sent.Count);
            var p = (PreviewCommandPayload)_commands.Sent[0].Payload!;
            Assert.AreEqual(PreviewCommandOps.Detach, p.Op);
            CollectionAssert.AreEquivalent(new[] { "cam-1" }, p.CameraIds);
            CollectionAssert.Contains(_resolver.Released, "k1");
            Assert.AreEqual(0, _sut.Slots.Count);
            Assert.IsFalse(_sut.IsAttachActive);
        }

        [Test]
        public async Task DetachAll_ReleasesEveryHandle()
        {
            _resolver.Handles["k1"] = new object();
            _resolver.Handles["k2"] = new object();
            _sut.Attach(new[] { new CameraId("cam-1"), new CameraId("cam-2") }, 192, 108, 15);
            await _sut.OnHandleStateAsync(new CameraId("cam-1"), new PreviewHandleStatePayload { TextureKey = "k1", Size = new[] { 192, 108 }, Fps = 15 });
            await _sut.OnHandleStateAsync(new CameraId("cam-2"), new PreviewHandleStatePayload { TextureKey = "k2", Size = new[] { 192, 108 }, Fps = 15 });

            _commands.Sent.Clear();
            _sut.DetachAll();
            Assert.AreEqual(1, _commands.Sent.Count);
            var p = (PreviewCommandPayload)_commands.Sent[0].Payload!;
            Assert.AreEqual(PreviewCommandOps.Detach, p.Op);
            Assert.AreEqual(2, p.CameraIds.Count);
            Assert.AreEqual(2, _resolver.Released.Count(r => r == "k1" || r == "k2"));
            Assert.AreEqual(0, _sut.Slots.Count);
            Assert.IsFalse(_sut.IsAttachActive);
        }

        [Test]
        public void Attach_EmptyList_DoesNothing()
        {
            _sut.Attach(System.Array.Empty<CameraId>(), 192, 108, 15);
            Assert.AreEqual(0, _commands.Sent.Count);
            Assert.IsFalse(_sut.IsAttachActive);
        }
    }
}
