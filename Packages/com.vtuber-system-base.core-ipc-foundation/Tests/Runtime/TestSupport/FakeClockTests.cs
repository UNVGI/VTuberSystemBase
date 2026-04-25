#nullable enable
using System;
using NUnit.Framework;

namespace VTuberSystemBase.CoreIpc.Tests.TestSupport
{
    [TestFixture]
    public sealed class FakeClockTests
    {
        [Test]
        public void DefaultStart_IsDeterministic()
        {
            var clock = new FakeClock();
            var expected = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Assert.AreEqual(expected, clock.UtcNow);
        }

        [Test]
        public void Advance_MovesUtcNowForward()
        {
            var clock = new FakeClock();
            var start = clock.UtcNow;

            clock.Advance(TimeSpan.FromSeconds(7));

            Assert.AreEqual(start + TimeSpan.FromSeconds(7), clock.UtcNow);
        }

        [Test]
        public void Advance_FiresScheduledCallbacksAtOrBeforeTarget()
        {
            var clock = new FakeClock();
            int fired = 0;
            clock.ScheduleAfter(TimeSpan.FromSeconds(1), () => fired++);
            clock.ScheduleAfter(TimeSpan.FromSeconds(3), () => fired++);
            clock.ScheduleAfter(TimeSpan.FromSeconds(10), () => fired++);

            clock.Advance(TimeSpan.FromSeconds(5));

            Assert.AreEqual(2, fired);
        }

        [Test]
        public void Advance_FiresCallbacksInDueOrder()
        {
            var clock = new FakeClock();
            var observed = new System.Collections.Generic.List<int>();
            clock.ScheduleAfter(TimeSpan.FromSeconds(3), () => observed.Add(3));
            clock.ScheduleAfter(TimeSpan.FromSeconds(1), () => observed.Add(1));
            clock.ScheduleAfter(TimeSpan.FromSeconds(2), () => observed.Add(2));

            clock.Advance(TimeSpan.FromSeconds(5));

            Assert.AreEqual(new[] { 1, 2, 3 }, observed.ToArray());
        }

        [Test]
        public void ScheduleAfter_DisposeCancelsCallback()
        {
            var clock = new FakeClock();
            int fired = 0;
            var token = clock.ScheduleAfter(TimeSpan.FromSeconds(1), () => fired++);

            token.Dispose();
            clock.Advance(TimeSpan.FromSeconds(5));

            Assert.AreEqual(0, fired);
        }

        [Test]
        public void Advance_NegativeDelta_Throws()
        {
            var clock = new FakeClock();
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
        }

        [Test]
        public void ScheduleAfter_NegativeDelay_Throws()
        {
            var clock = new FakeClock();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.ScheduleAfter(TimeSpan.FromSeconds(-1), () => { }));
        }

        [Test]
        public void ScheduleAfter_NullCallback_Throws()
        {
            var clock = new FakeClock();
            Assert.Throws<ArgumentNullException>(
                () => clock.ScheduleAfter(TimeSpan.FromSeconds(1), null!));
        }
    }
}
