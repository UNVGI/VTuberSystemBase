#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Services;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DebounceFlusher"/> (Task 3.2, Requirements 4.7, 8.3,
    /// 12.8). Drives the clock manually via <see cref="FakeClock"/> so timing is
    /// deterministic.
    /// </summary>
    [TestFixture]
    public sealed class DebounceFlusherTests
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

        [Test]
        public async Task Schedule_DoesNotFlushBeforeInterval()
        {
            var clock = new FakeClock();
            using var flusher = new DebounceFlusher(Interval, clock);
            int fired = 0;
            flusher.Schedule(() => { fired++; return Task.CompletedTask; });

            clock.Advance(TimeSpan.FromMilliseconds(499));
            await Task.Yield();

            Assert.That(fired, Is.EqualTo(0));
        }

        [Test]
        public async Task Schedule_FlushesAfterIntervalElapses()
        {
            var clock = new FakeClock();
            using var flusher = new DebounceFlusher(Interval, clock);
            int fired = 0;
            flusher.Schedule(() => { fired++; return Task.CompletedTask; });

            clock.Advance(Interval);
            await Task.Delay(10);  // give async continuation a chance to run

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public async Task Schedule_OnlyTheLatestRegistration_RunsAfterInterval()
        {
            var clock = new FakeClock();
            using var flusher = new DebounceFlusher(Interval, clock);
            int firstRan = 0;
            int secondRan = 0;

            flusher.Schedule(() => { firstRan++; return Task.CompletedTask; });
            clock.Advance(TimeSpan.FromMilliseconds(200));
            flusher.Schedule(() => { secondRan++; return Task.CompletedTask; });

            // Advance ENOUGH to elapse the latest 500 ms wait from the second Schedule.
            // FakeClock's UtcNow has advanced to 200 ms; the second Schedule's deadline
            // is at 200 + 500 = 700 ms, so we advance another 500 ms.
            clock.Advance(Interval);
            await Task.Delay(10);

            Assert.That(firstRan, Is.EqualTo(0), "first action must have been superseded by the second Schedule");
            Assert.That(secondRan, Is.EqualTo(1), "second action must run exactly once after its own 500 ms window");
        }

        [Test]
        public async Task FlushImmediateAsync_RunsPendingActionRightAway()
        {
            var clock = new FakeClock();
            using var flusher = new DebounceFlusher(Interval, clock);
            int fired = 0;
            flusher.Schedule(() => { fired++; return Task.CompletedTask; });

            await flusher.FlushImmediateAsync();

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public async Task FlushImmediateAsync_OnEmptyQueue_IsNoOp()
        {
            var clock = new FakeClock();
            using var flusher = new DebounceFlusher(Interval, clock);

            await flusher.FlushImmediateAsync();
            // No exception, no fire.
            Assert.Pass();
        }

        [Test]
        public async Task Dispose_DropsPendingAction_WithoutFiring()
        {
            var clock = new FakeClock();
            var flusher = new DebounceFlusher(Interval, clock);
            int fired = 0;
            flusher.Schedule(() => { fired++; return Task.CompletedTask; });

            flusher.Dispose();
            clock.Advance(Interval);
            await Task.Delay(10);

            Assert.That(fired, Is.EqualTo(0));
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var clock = new FakeClock();
            var flusher = new DebounceFlusher(Interval, clock);
            flusher.Dispose();
            Assert.DoesNotThrow(() => flusher.Dispose());
        }

        [Test]
        public void Schedule_OnDisposed_IsNoOp()
        {
            var clock = new FakeClock();
            var flusher = new DebounceFlusher(Interval, clock);
            flusher.Dispose();
            Assert.DoesNotThrow(() => flusher.Schedule(() => Task.CompletedTask));
        }

        [Test]
        public void Constructor_RejectsNonPositiveInterval()
        {
            var clock = new FakeClock();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DebounceFlusher(TimeSpan.Zero, clock));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DebounceFlusher(TimeSpan.FromMilliseconds(-1), clock));
        }
    }
}
