#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.StageLightingVolumeTab.Services;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// Manually-advanced <see cref="IClock"/> double. <see cref="Advance"/> bumps the
    /// virtual time and resolves any <see cref="Delay"/> tasks whose deadline has been
    /// reached. (Task 1.2, Requirement 12.8)
    /// </summary>
    public sealed class FakeClock : IClock
    {
        private readonly object _gate = new object();
        private readonly List<Pending> _pending = new List<Pending>();

        public FakeClock(DateTimeOffset? start = null)
        {
            UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        public DateTimeOffset UtcNow { get; private set; }

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            if (duration <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = new Pending(UtcNow + duration, tcs, ct);
            CancellationTokenRegistration reg = default;
            if (ct.CanBeCanceled)
            {
                reg = ct.Register(() =>
                {
                    lock (_gate)
                    {
                        _pending.Remove(entry);
                    }
                    tcs.TrySetCanceled(ct);
                });
            }
            entry.Registration = reg;
            lock (_gate)
            {
                _pending.Add(entry);
            }
            return tcs.Task;
        }

        /// <summary>Advances the virtual clock by <paramref name="delta"/> and resolves due delays.</summary>
        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta));
            List<Pending> due;
            lock (_gate)
            {
                UtcNow += delta;
                due = new List<Pending>();
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    if (_pending[i].DueAt <= UtcNow)
                    {
                        due.Add(_pending[i]);
                        _pending.RemoveAt(i);
                    }
                }
            }
            foreach (var p in due)
            {
                p.Registration.Dispose();
                p.Tcs.TrySetResult(true);
            }
        }

        private sealed class Pending
        {
            public DateTimeOffset DueAt;
            public TaskCompletionSource<bool> Tcs;
            public CancellationToken Token;
            public CancellationTokenRegistration Registration;

            public Pending(DateTimeOffset dueAt, TaskCompletionSource<bool> tcs, CancellationToken token)
            {
                DueAt = dueAt;
                Tcs = tcs;
                Token = token;
            }
        }
    }
}
