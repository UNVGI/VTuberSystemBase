using System;
using VTuberSystemBase.RacMainOutputAdapter.Bootstrapper;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// テスト用 <see cref="IClock"/>。<see cref="UtcNow"/> を内部状態として保持し、<see cref="Advance"/> で進める。
    /// </summary>
    public sealed class ManualClock : IClock
    {
        private DateTimeOffset _now;

        /// <summary>初期時刻を指定して生成する。</summary>
        public ManualClock(DateTimeOffset initialUtc) { _now = initialUtc; }

        /// <summary>1970-01-01 を初期時刻として生成する。</summary>
        public ManualClock() : this(DateTimeOffset.FromUnixTimeMilliseconds(0)) { }

        /// <inheritdoc/>
        public DateTimeOffset UtcNow => _now;

        /// <summary>時刻を <paramref name="delta"/> だけ進める。</summary>
        public void Advance(TimeSpan delta) { _now = _now + delta; }

        /// <summary>時刻を <paramref name="ms"/> だけ進める便利ヘルパ。</summary>
        public void AdvanceMs(int ms) { _now = _now + TimeSpan.FromMilliseconds(ms); }
    }
}
