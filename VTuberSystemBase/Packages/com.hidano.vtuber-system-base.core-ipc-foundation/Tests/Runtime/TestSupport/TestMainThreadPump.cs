#nullable enable
using System;
using System.Collections.Generic;

namespace VTuberSystemBase.CoreIpc.Tests.TestSupport
{
    public sealed class TestMainThreadPump
    {
        private readonly List<Action> _callbacks = new();

        public int RegisteredCount => _callbacks.Count;

        public IDisposable Register(Action flush)
        {
            if (flush == null) throw new ArgumentNullException(nameof(flush));
            _callbacks.Add(flush);
            return new Registration(this, flush);
        }

        public void Pump()
        {
            for (int i = 0; i < _callbacks.Count; i++)
            {
                _callbacks[i].Invoke();
            }
        }

        public void Pump(int frames)
        {
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames), frames, "frames must be non-negative.");
            for (int i = 0; i < frames; i++)
            {
                Pump();
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly TestMainThreadPump _owner;
            private Action? _callback;

            public Registration(TestMainThreadPump owner, Action callback)
            {
                _owner = owner;
                _callback = callback;
            }

            public void Dispose()
            {
                if (_callback == null) return;
                _owner._callbacks.Remove(_callback);
                _callback = null;
            }
        }
    }
}
