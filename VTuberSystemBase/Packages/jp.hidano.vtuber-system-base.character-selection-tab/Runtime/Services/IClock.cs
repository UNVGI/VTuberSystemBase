#nullable enable
using System;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// Time abstraction used for debounce, timeout and idle detection. Production
    /// code uses <see cref="SystemClock"/>; tests inject <c>ManualClock</c>.
    /// (task 2.2, design.md §Services §IClock).
    /// </summary>
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }

        /// <summary>
        /// Raised after every advance operation (production clock raises this on a
        /// frame tick; ManualClock raises it from <c>Advance</c>). Subscribers must
        /// not throw; an exception is logged via diagnostics and swallowed by the
        /// publishing service so a faulty subscriber cannot stall timer flushes.
        /// </summary>
        event Action<DateTimeOffset> OnTick;
    }
}
