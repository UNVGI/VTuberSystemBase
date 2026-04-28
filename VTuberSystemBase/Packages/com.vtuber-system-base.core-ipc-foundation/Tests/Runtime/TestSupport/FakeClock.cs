#nullable enable
using System;
using System.Collections.Generic;

namespace VTuberSystemBase.CoreIpc.Tests.TestSupport
{
    public sealed class FakeClock
    {
        private static readonly DateTimeOffset DefaultStart =
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly List<ScheduledCallback> _scheduled = new();
        private DateTimeOffset _now;

        public FakeClock() : this(DefaultStart) { }

        public FakeClock(DateTimeOffset start)
        {
            _now = start;
        }

        public DateTimeOffset UtcNow => _now;

        public IDisposable ScheduleAfter(TimeSpan delay, Action callback)
        {
            if (delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay), delay, "delay must be non-negative.");
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            var entry = new ScheduledCallback(_now + delay, callback);
            _scheduled.Add(entry);
            return new Subscription(this, entry);
        }

        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "delta must be non-negative.");

            var target = _now + delta;
            while (true)
            {
                ScheduledCallback? next = null;
                int nextIndex = -1;
                for (int i = 0; i < _scheduled.Count; i++)
                {
                    var c = _scheduled[i];
                    if (c.DueAt > target) continue;
                    if (next == null || c.DueAt < next.DueAt)
                    {
                        next = c;
                        nextIndex = i;
                    }
                }

                if (next == null) break;
                _scheduled.RemoveAt(nextIndex);
                _now = next.DueAt;
                next.Callback.Invoke();
            }

            _now = target;
        }

        private sealed class ScheduledCallback
        {
            public DateTimeOffset DueAt { get; }
            public Action Callback { get; }

            public ScheduledCallback(DateTimeOffset dueAt, Action callback)
            {
                DueAt = dueAt;
                Callback = callback;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly FakeClock _owner;
            private ScheduledCallback? _entry;

            public Subscription(FakeClock owner, ScheduledCallback entry)
            {
                _owner = owner;
                _entry = entry;
            }

            public void Dispose()
            {
                if (_entry == null) return;
                _owner._scheduled.Remove(_entry);
                _entry = null;
            }
        }
    }
}
