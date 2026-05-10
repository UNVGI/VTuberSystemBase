#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// Manual <see cref="ITimeProvider"/> for tests. <see cref="Advance"/> moves
    /// time forward, raises <see cref="OnTick"/>, and fires every debounce timer
    /// whose deadline has elapsed.
    /// </summary>
    public sealed class FakeTimeProvider : ITimeProvider
    {
        private DateTimeOffset _utcNow;
        private double _monotonicSeconds;
        private readonly List<DebounceTimer> _timers = new List<DebounceTimer>();

        public FakeTimeProvider(DateTimeOffset start = default, double monotonic = 0.0)
        {
            _utcNow = start == default ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) : start;
            _monotonicSeconds = monotonic;
        }

        public DateTimeOffset UtcNow => _utcNow;

        public double MonotonicSeconds => _monotonicSeconds;

        public event Action<DateTimeOffset>? OnTick;

        public IDebounceTimer CreateDebounce(TimeSpan window, Action action)
        {
            if (window < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
            if (action == null) throw new ArgumentNullException(nameof(action));
            var timer = new DebounceTimer(this, window, action);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta));
            _utcNow += delta;
            _monotonicSeconds += delta.TotalSeconds;
            OnTick?.Invoke(_utcNow);
            // Fire any debouncer whose deadline has passed.
            foreach (var t in _timers.ToArray())
            {
                t.MaybeFire(_utcNow);
            }
        }

        private sealed class DebounceTimer : IDebounceTimer
        {
            private readonly FakeTimeProvider _owner;
            private readonly TimeSpan _window;
            private readonly Action _action;
            private DateTimeOffset? _fireAt;
            private bool _disposed;

            public DebounceTimer(FakeTimeProvider owner, TimeSpan window, Action action)
            {
                _owner = owner;
                _window = window;
                _action = action;
            }

            public bool IsPending => !_disposed && _fireAt is not null;

            public void Bump()
            {
                if (_disposed) return;
                _fireAt = _owner._utcNow + _window;
            }

            public void Flush()
            {
                if (_disposed) return;
                if (_fireAt is null) return;
                _fireAt = null;
                _action();
            }

            public void MaybeFire(DateTimeOffset now)
            {
                if (_disposed) return;
                if (_fireAt is null) return;
                if (now < _fireAt.Value) return;
                _fireAt = null;
                _action();
            }

            public void Dispose()
            {
                _disposed = true;
                _fireAt = null;
                _owner._timers.Remove(this);
            }
        }
    }
}
