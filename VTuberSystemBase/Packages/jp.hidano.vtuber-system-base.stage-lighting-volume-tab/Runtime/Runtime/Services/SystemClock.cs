#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// Production <see cref="IClock"/> implementation backed by <see cref="DateTimeOffset.UtcNow"/>
    /// and <see cref="Task.Delay(TimeSpan, CancellationToken)"/>. Tests substitute
    /// <c>FakeClock</c> in place of this.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task Delay(TimeSpan duration, CancellationToken ct) => Task.Delay(duration, ct);
    }
}
