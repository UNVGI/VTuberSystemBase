#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// Time abstraction so debounce / timeout logic is unit-testable. Production:
    /// <c>SystemClock</c>. Tests: <c>FakeClock</c> (manual time advance).
    /// See design.md §Services §DebounceFlusher (Requirements 4.7, 8.3, 12.8).
    /// </summary>
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }

        Task Delay(TimeSpan duration, CancellationToken ct);
    }
}
