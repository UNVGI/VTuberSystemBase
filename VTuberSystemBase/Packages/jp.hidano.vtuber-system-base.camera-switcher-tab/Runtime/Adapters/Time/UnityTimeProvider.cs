#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Adapters.Time
{
    /// <summary>
    /// Production <see cref="ITimeProvider"/> that uses
    /// <see cref="DateTimeOffset.UtcNow"/> + <see cref="UnityEngine.Time.timeAsDouble"/>.
    /// Debounce timers are advanced from <see cref="Tick"/>, which the
    /// composition root drives once per LateUpdate or via a UI Toolkit
    /// scheduled item (mirrors <c>character-selection-tab.SystemClock</c>).
    /// </summary>
    public sealed class UnityTimeProvider : ITimeProvider
    {
        private readonly List<DebounceTimer> _timers = new List<DebounceTimer>();

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public double MonotonicSeconds => Application.isPlaying ? UnityEngine.Time.timeAsDouble : 0.0;

        public event Action<DateTimeOffset>? OnTick;

        public IDebounceTimer CreateDebounce(TimeSpan window, Action action)
        {
            if (window < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
            if (action == null) throw new ArgumentNullException(nameof(action));
            var t = new DebounceTimer(this, window, action);
            _timers.Add(t);
            return t;
        }

        /// <summary>Advance pending debounce timers. Call from LateUpdate or scheduler.</summary>
        public void Tick()
        {
            var now = UtcNow;
            OnTick?.Invoke(now);
            foreach (var t in _timers.ToArray())
            {
                t.MaybeFire(now);
            }
        }

        private sealed class DebounceTimer : IDebounceTimer
        {
            private readonly UnityTimeProvider _owner;
            private readonly TimeSpan _window;
            private readonly Action _action;
            private DateTimeOffset? _fireAt;
            private bool _disposed;

            public DebounceTimer(UnityTimeProvider owner, TimeSpan window, Action action)
            {
                _owner = owner;
                _window = window;
                _action = action;
            }

            public bool IsPending => !_disposed && _fireAt is not null;

            public void Bump()
            {
                if (_disposed) return;
                _fireAt = _owner.UtcNow + _window;
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
