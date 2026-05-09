#nullable enable
using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Time abstraction used by the timeout tracker, preset debouncer and any
    /// other code that needs deterministic time control under tests.
    /// </summary>
    /// <remarks>
    /// Implementations MUST publish <see cref="OnTick"/> on the Unity main
    /// thread. Production wraps <c>Time.timeAsDouble</c> + a
    /// <c>PlayerLoop</c> hook; tests use a manual clock that advances time
    /// synchronously and raises <see cref="OnTick"/> from <c>Advance</c>.
    /// </remarks>
    public interface ITimeProvider
    {
        DateTimeOffset UtcNow { get; }

        /// <summary>Monotonic seconds (typically <c>Time.timeAsDouble</c>).</summary>
        double MonotonicSeconds { get; }

        /// <summary>Raised once per advance / frame tick on the Unity main thread.</summary>
        event Action<DateTimeOffset> OnTick;

        /// <summary>
        /// Create a debounced action that fires <paramref name="action"/> at most
        /// once per <paramref name="window"/> since the last <c>Bump</c>. Disposing
        /// the returned handle cancels any pending fire and detaches the timer.
        /// </summary>
        IDebounceTimer CreateDebounce(TimeSpan window, Action action);
    }

    /// <summary>Debounced timer handle returned by <see cref="ITimeProvider.CreateDebounce"/>.</summary>
    public interface IDebounceTimer : IDisposable
    {
        /// <summary>Reset the debounce window — the action fires <c>window</c> after the latest <c>Bump</c>.</summary>
        void Bump();

        /// <summary>Fire the action immediately if a bump is pending; otherwise no-op.</summary>
        void Flush();

        /// <summary>True while at least one <c>Bump</c> is pending and the action has not yet fired.</summary>
        bool IsPending { get; }
    }
}
