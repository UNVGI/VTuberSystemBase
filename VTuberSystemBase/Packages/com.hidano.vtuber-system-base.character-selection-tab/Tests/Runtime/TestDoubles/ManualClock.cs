#nullable enable
using System;
using VTuberSystemBase.CharacterSelectionTab.Services;

namespace VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles
{
    /// <summary>
    /// Manual <see cref="IClock"/> for tests. <see cref="Advance"/> moves time
    /// forward and raises <see cref="OnTick"/> at the new instant.
    /// </summary>
    public sealed class ManualClock : IClock
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset start = default)
        {
            _now = start == default ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) : start;
        }

        public DateTimeOffset UtcNow => _now;

        public event Action<DateTimeOffset>? OnTick;

        /// <summary>Advance time by <paramref name="delta"/> and raise OnTick once.</summary>
        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta));
            _now += delta;
            OnTick?.Invoke(_now);
        }

        /// <summary>Set absolute time and raise OnTick (must move forward).</summary>
        public void SetUtcNow(DateTimeOffset to)
        {
            if (to < _now) throw new ArgumentOutOfRangeException(nameof(to), "Clock must be monotonic.");
            _now = to;
            OnTick?.Invoke(_now);
        }
    }
}
