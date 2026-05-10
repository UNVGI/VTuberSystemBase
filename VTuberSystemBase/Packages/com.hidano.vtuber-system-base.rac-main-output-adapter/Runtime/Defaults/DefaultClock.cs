using System;
using VTuberSystemBase.RacMainOutputAdapter.Bootstrapper;

namespace VTuberSystemBase.RacMainOutputAdapter.Defaults
{
    /// <summary>
    /// <see cref="IClock"/> の既定実装。<see cref="DateTimeOffset.UtcNow"/> をそのまま返す。
    /// </summary>
    public sealed class DefaultClock : IClock
    {
        /// <inheritdoc/>
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
