#nullable enable
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Deterministic <see cref="ICameraSwitcherOutputAdapterClock"/> double.
    /// </summary>
    public sealed class FakeClock : ICameraSwitcherOutputAdapterClock
    {
        public long CurrentUnixMs { get; set; }

        public FakeClock(long initialUnixMs = 0)
        {
            CurrentUnixMs = initialUnixMs;
        }

        public long UnixMillisecondsNow() => CurrentUnixMs;

        public void Advance(long milliseconds) => CurrentUnixMs += milliseconds;
    }
}
