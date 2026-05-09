using System;

namespace VTuberSystemBase.RacMainOutputAdapter.Bootstrapper
{
    /// <summary>
    /// 時刻取得抽象。テスト時には <c>ManualClock</c> を注入して時間進行を制御する（Requirement 8.7 / 11.6）。
    /// </summary>
    public interface IClock
    {
        /// <summary>UTC 現在時刻。</summary>
        DateTimeOffset UtcNow { get; }
    }
}
