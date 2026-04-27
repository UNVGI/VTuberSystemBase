#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Dispatch;

namespace VTuberSystemBase.OutputRendererShell.EditModeTests
{
    /// <summary>
    /// Task 4.1: <see cref="HandlerRegistry"/> の登録／ルックアップ／解除／重複検出を検証する。
    /// </summary>
    [TestFixture]
    public class HandlerRegistryTests
    {
        [Test]
        public void Register_ThenLookup_ReturnsHandler()
        {
            var sut = new HandlerRegistry();
            Action<string> handler = _ => { };

            var token = sut.Register("topic.a", OutputCommandKind.State, handler);

            Assert.AreEqual(1, sut.Count);
            Assert.IsTrue(sut.TryGet("topic.a", OutputCommandKind.State, out var found));
            Assert.AreSame(handler, found);
            Assert.IsNotNull(token);
            Assert.IsFalse(token.IsDisposed);
        }

        [Test]
        public void Register_DuplicateTopicAndKind_ThrowsFailFast()
        {
            var sut = new HandlerRegistry();
            sut.Register("topic.a", OutputCommandKind.State, new Action<string>(_ => { }));

            Assert.Throws<InvalidOperationException>(() =>
                sut.Register("topic.a", OutputCommandKind.State, new Action<string>(_ => { })),
                "重複登録は InvalidOperationException で Fail-Fast すること（Req 4.5）");
        }

        [Test]
        public void Register_SameTopicDifferentKind_AllowsIndependentRegistration()
        {
            var sut = new HandlerRegistry();

            sut.Register("topic.a", OutputCommandKind.State, new Action<string>(_ => { }));
            sut.Register("topic.a", OutputCommandKind.Event, new Action<int>(_ => { }));

            Assert.AreEqual(2, sut.Count);
            Assert.IsTrue(sut.TryGet("topic.a", OutputCommandKind.State, out _));
            Assert.IsTrue(sut.TryGet("topic.a", OutputCommandKind.Event, out _));
        }

        [Test]
        public void TokenDispose_RemovesEntry_AndIsIdempotent()
        {
            var sut = new HandlerRegistry();
            var token = sut.Register("topic.a", OutputCommandKind.State, new Action<string>(_ => { }));
            Assert.AreEqual(1, sut.Count);

            token.Dispose();
            Assert.AreEqual(0, sut.Count, "Dispose で登録数が減少すること");
            Assert.IsFalse(sut.TryGet("topic.a", OutputCommandKind.State, out _));
            Assert.IsTrue(token.IsDisposed);

            Assert.DoesNotThrow(() => token.Dispose(), "多重 Dispose は安全に no-op");
        }

        [Test]
        public void TryGet_UnregisteredKey_ReturnsFalse()
        {
            var sut = new HandlerRegistry();
            Assert.IsFalse(sut.TryGet("topic.unknown", OutputCommandKind.Event, out _));
        }

        [Test]
        public void Register_NullOrEmptyTopic_ThrowsArgumentException()
        {
            var sut = new HandlerRegistry();
            Assert.Throws<ArgumentException>(() =>
                sut.Register("", OutputCommandKind.State, new Action<string>(_ => { })));
            Assert.Throws<ArgumentException>(() =>
                sut.Register(null!, OutputCommandKind.State, new Action<string>(_ => { })));
        }

        [Test]
        public void Register_NullHandler_ThrowsArgumentNullException()
        {
            var sut = new HandlerRegistry();
            Assert.Throws<ArgumentNullException>(() =>
                sut.Register("topic.a", OutputCommandKind.State, null!));
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var sut = new HandlerRegistry();
            sut.Register("topic.a", OutputCommandKind.State, new Action<string>(_ => { }));
            sut.Register("topic.b", OutputCommandKind.Event, new Action<int>(_ => { }));

            sut.Clear();

            Assert.AreEqual(0, sut.Count);
        }
    }
}
