#nullable enable

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Wall-clock millisecond timestamp source used to populate
    /// <c>CamerasListPayload.UpdatedAtUnixMs</c> and similar fields. Abstracted so
    /// tests can inject a deterministic clock.
    /// </summary>
    public interface ICameraSwitcherOutputAdapterClock
    {
        /// <summary>Wall-clock milliseconds since the Unix epoch (UTC).</summary>
        long UnixMillisecondsNow();
    }
}
