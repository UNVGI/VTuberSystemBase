#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Core.Connection
{
    public sealed class ReconnectBackoff
    {
        private readonly TimeSpan _initialDelay;
        private readonly double _multiplier;
        private readonly TimeSpan _maxDelay;
        private readonly int _maxAttempts;
        private readonly object _sync = new();
        private int _attemptCount;

        public ReconnectBackoff(
            TimeSpan initialDelay,
            double multiplier,
            TimeSpan maxDelay,
            int maxAttempts)
        {
            if (initialDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialDelay), initialDelay,
                    "initialDelay must be greater than or equal to TimeSpan.Zero.");
            }

            if (double.IsNaN(multiplier) || multiplier < 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(multiplier), multiplier,
                    "multiplier must be a real number greater than or equal to 1.0 so the series never shrinks.");
            }

            if (maxDelay < initialDelay)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDelay), maxDelay,
                    "maxDelay must be greater than or equal to initialDelay.");
            }

            if (maxAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxAttempts), maxAttempts,
                    "maxAttempts must be greater than zero.");
            }

            _initialDelay = initialDelay;
            _multiplier = multiplier;
            _maxDelay = maxDelay;
            _maxAttempts = maxAttempts;
        }

        public int MaxAttempts => _maxAttempts;

        public int AttemptCount
        {
            get
            {
                lock (_sync)
                {
                    return _attemptCount;
                }
            }
        }

        public bool ExceededMaxAttempts
        {
            get
            {
                lock (_sync)
                {
                    return _attemptCount >= _maxAttempts;
                }
            }
        }

        public TimeSpan NextDelay()
        {
            lock (_sync)
            {
                if (_attemptCount >= _maxAttempts)
                {
                    throw new InvalidOperationException(
                        $"ReconnectBackoff: max attempts ({_maxAttempts}) reached; "
                        + "callers must check ExceededMaxAttempts and transition to PermanentlyDisconnected "
                        + "instead of calling NextDelay().");
                }

                double delayMs = _initialDelay.TotalMilliseconds
                    * Math.Pow(_multiplier, _attemptCount);

                if (double.IsNaN(delayMs)
                    || double.IsInfinity(delayMs)
                    || delayMs > _maxDelay.TotalMilliseconds)
                {
                    delayMs = _maxDelay.TotalMilliseconds;
                }

                _attemptCount++;
                return TimeSpan.FromMilliseconds(delayMs);
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _attemptCount = 0;
            }
        }
    }
}
