#nullable enable
using System;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Production clock: <see cref="DateTimeOffset.UtcNow"/>. Ticks must be raised
    /// externally by the bootstrapper from a UI Toolkit scheduled item; this class
    /// itself does not subscribe to Unity time so it stays test-friendly.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public event Action<DateTimeOffset>? OnTick;

        /// <summary>Raise <see cref="OnTick"/> with the current time.</summary>
        public void Tick()
        {
            OnTick?.Invoke(UtcNow);
        }
    }
}
