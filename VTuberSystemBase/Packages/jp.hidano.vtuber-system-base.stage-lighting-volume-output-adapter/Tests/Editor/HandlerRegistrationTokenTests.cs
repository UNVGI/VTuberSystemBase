#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class HandlerRegistrationTokenTests
    {
        private sealed class RecordingDisposable : IDisposable
        {
            private readonly List<RecordingDisposable> _log;
            public bool Disposed { get; private set; }
            public int OrderIndex { get; private set; } = -1;
            public RecordingDisposable(List<RecordingDisposable> log) { _log = log; }
            public void Dispose()
            {
                if (Disposed) return;
                Disposed = true;
                OrderIndex = _log.Count;
                _log.Add(this);
            }
        }

        [Test]
        public void Dispose_DisposesChildrenInReverseOrder()
        {
            var log = new List<RecordingDisposable>();
            var a = new RecordingDisposable(log);
            var b = new RecordingDisposable(log);
            var c = new RecordingDisposable(log);
            var token = new HandlerRegistrationToken(a, b, c);

            token.Dispose();

            Assert.That(a.Disposed && b.Disposed && c.Disposed, Is.True);
            // LIFO: c first, b, a
            Assert.That(log[0], Is.SameAs(c));
            Assert.That(log[1], Is.SameAs(b));
            Assert.That(log[2], Is.SameAs(a));
        }

        [Test]
        public void Dispose_Twice_IsNoOp()
        {
            var log = new List<RecordingDisposable>();
            var a = new RecordingDisposable(log);
            var token = new HandlerRegistrationToken(a);
            token.Dispose();
            // calling again must not re-dispose a
            Assert.DoesNotThrow(() => token.Dispose());
            Assert.That(log.Count, Is.EqualTo(1));
            Assert.That(token.IsDisposed, Is.True);
        }

        [Test]
        public void Add_AfterDispose_DisposesChildImmediately()
        {
            var log = new List<RecordingDisposable>();
            var token = new HandlerRegistrationToken();
            token.Dispose();
            var late = new RecordingDisposable(log);
            token.Add(late);
            Assert.That(late.Disposed, Is.True);
        }

        [Test]
        public void Add_NullChild_IsIgnored()
        {
            var token = new HandlerRegistrationToken();
            Assert.DoesNotThrow(() => token.Add(null));
            Assert.That(token.Count, Is.EqualTo(0));
        }

        [Test]
        public void Dispose_FaultyChild_DoesNotPreventOthersAndRethrowsFirst()
        {
            var log = new List<RecordingDisposable>();
            var a = new RecordingDisposable(log);
            var faulty = new ThrowingDisposable("boom");
            var c = new RecordingDisposable(log);
            // disposal order: c, faulty, a
            var token = new HandlerRegistrationToken(a, faulty, c);
            var ex = Assert.Throws<InvalidOperationException>(() => token.Dispose());
            Assert.That(ex!.Message, Is.EqualTo("boom"));
            Assert.That(a.Disposed, Is.True);
            Assert.That(c.Disposed, Is.True);
        }

        private sealed class ThrowingDisposable : IDisposable
        {
            private readonly string _message;
            public ThrowingDisposable(string message) { _message = message; }
            public void Dispose() { throw new InvalidOperationException(_message); }
        }
    }
}
