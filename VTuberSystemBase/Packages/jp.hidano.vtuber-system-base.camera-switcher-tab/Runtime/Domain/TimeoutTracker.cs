#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Tracks asynchronous correlations (e.g. <c>camera/command</c> →
    /// <c>camera/created</c>) keyed by clientRequestId. Each <see cref="Arm"/>
    /// schedules a timeout via <see cref="ITimeProvider"/>; a successful
    /// <see cref="Cancel"/> before the timeout fires suppresses the callback.
    /// </summary>
    /// <remarks>
    /// Designed to be Unity-free so it can be unit-tested with
    /// <c>FakeTimeProvider</c>. The default timeout is 5 s
    /// (Requirement 6.8 / D-8). Calling <see cref="Arm"/> twice with the same
    /// clientRequestId is a no-op (the existing timer survives).
    /// </remarks>
    public sealed class TimeoutTracker
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        private readonly ITimeProvider _time;
        private readonly Dictionary<string, IDebounceTimer> _timers = new Dictionary<string, IDebounceTimer>(StringComparer.Ordinal);

        public event Action<string>? OnTimeout;

        public TimeoutTracker(ITimeProvider time)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <summary>True when at least one Arm has been issued and not yet cancelled / fired.</summary>
        public int ArmedCount => _timers.Count;

        public bool IsArmed(string clientRequestId) => _timers.ContainsKey(clientRequestId);

        /// <summary>Arm a timeout for <paramref name="clientRequestId"/>. Idempotent.</summary>
        public void Arm(string clientRequestId, TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(clientRequestId))
                throw new ArgumentException("clientRequestId must not be empty.", nameof(clientRequestId));
            if (_timers.ContainsKey(clientRequestId)) return;
            var window = timeout ?? DefaultTimeout;
            var key = clientRequestId;
            // The captured 'this' fires OnTimeout — but only if the timer is still
            // present in the dictionary (i.e. it hasn't been Cancel'd). Capture
            // the timer reference so we can compare-by-instance to avoid late
            // fires being delivered after Cancel + reArm with the same id.
            IDebounceTimer? createdTimer = null;
            createdTimer = _time.CreateDebounce(window, () =>
            {
                if (createdTimer == null) return;
                if (_timers.TryGetValue(key, out var current) && ReferenceEquals(current, createdTimer))
                {
                    _timers.Remove(key);
                    OnTimeout?.Invoke(key);
                }
            });
            createdTimer.Bump();
            _timers[clientRequestId] = createdTimer;
        }

        /// <summary>Cancel a previously-armed timeout. Returns true if a pending timer was cleared.</summary>
        public bool Cancel(string clientRequestId)
        {
            if (string.IsNullOrEmpty(clientRequestId)) return false;
            if (!_timers.TryGetValue(clientRequestId, out var timer)) return false;
            timer.Dispose();
            _timers.Remove(clientRequestId);
            return true;
        }

        public void DisposeAll()
        {
            foreach (var timer in _timers.Values) timer.Dispose();
            _timers.Clear();
        }
    }
}
