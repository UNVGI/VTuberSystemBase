#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Core.Connection;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class ReconnectBackoffTests
    {
        private const int DefaultMaxAttempts = 20;

        private static ReconnectBackoff NewDefault()
        {
            return new ReconnectBackoff(
                initialDelay: TimeSpan.FromMilliseconds(250),
                multiplier: 2.0,
                maxDelay: TimeSpan.FromSeconds(5),
                maxAttempts: DefaultMaxAttempts);
        }

        // ---- Initial state ----

        [Test]
        public void InitialState_AttemptCountIsZero_AndNotExceeded()
        {
            var backoff = NewDefault();

            Assert.AreEqual(0, backoff.AttemptCount);
            Assert.AreEqual(DefaultMaxAttempts, backoff.MaxAttempts);
            Assert.IsFalse(backoff.ExceededMaxAttempts);
        }

        // ---- Series ----

        [Test]
        public void NextDelay_FirstCall_ReturnsInitialDelay()
        {
            var backoff = NewDefault();

            Assert.AreEqual(TimeSpan.FromMilliseconds(250), backoff.NextDelay());
            Assert.AreEqual(1, backoff.AttemptCount);
        }

        [Test]
        public void NextDelay_FirstFiveCalls_ProduceExponentialSeries()
        {
            var backoff = NewDefault();

            Assert.AreEqual(TimeSpan.FromMilliseconds(250), backoff.NextDelay()); // 250 * 2^0
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), backoff.NextDelay()); // 250 * 2^1
            Assert.AreEqual(TimeSpan.FromSeconds(1), backoff.NextDelay());        // 250 * 2^2
            Assert.AreEqual(TimeSpan.FromSeconds(2), backoff.NextDelay());        // 250 * 2^3
            Assert.AreEqual(TimeSpan.FromSeconds(4), backoff.NextDelay());        // 250 * 2^4
        }

        [Test]
        public void NextDelay_SixthCall_AppliesMaxDelayCap()
        {
            var backoff = NewDefault();
            for (int i = 0; i < 5; i++)
            {
                backoff.NextDelay();
            }

            // 6th call: 250 * 2^5 = 8000ms → capped to 5s.
            Assert.AreEqual(TimeSpan.FromSeconds(5), backoff.NextDelay());
        }

        [Test]
        public void NextDelay_AfterCapApplied_StaysAtMaxDelay()
        {
            var backoff = NewDefault();
            for (int i = 0; i < 6; i++)
            {
                backoff.NextDelay();
            }

            for (int i = 0; i < 14; i++)
            {
                Assert.AreEqual(
                    TimeSpan.FromSeconds(5),
                    backoff.NextDelay(),
                    $"call #{7 + i} should remain at maxDelay");
            }
        }

        [Test]
        public void NextDelay_FullSeries_MatchesDesignedSequence()
        {
            var backoff = NewDefault();

            var expected = new[]
            {
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
            };

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], backoff.NextDelay(), $"call #{i + 1}");
            }
        }

        // ---- ExceededMaxAttempts ----

        [Test]
        public void ExceededMaxAttempts_IsFalse_BeforeReachingLimit()
        {
            var backoff = NewDefault();
            for (int i = 0; i < DefaultMaxAttempts - 1; i++)
            {
                backoff.NextDelay();
            }

            Assert.AreEqual(DefaultMaxAttempts - 1, backoff.AttemptCount);
            Assert.IsFalse(
                backoff.ExceededMaxAttempts,
                "after one fewer call than maxAttempts the limit must not yet be reached");
        }

        [Test]
        public void ExceededMaxAttempts_BecomesTrue_AfterMaxAttemptsCalls()
        {
            var backoff = NewDefault();
            for (int i = 0; i < DefaultMaxAttempts; i++)
            {
                backoff.NextDelay();
            }

            Assert.AreEqual(DefaultMaxAttempts, backoff.AttemptCount);
            Assert.IsTrue(backoff.ExceededMaxAttempts);
        }

        [Test]
        public void NextDelay_AfterExceededMaxAttempts_ThrowsInvalidOperationException()
        {
            var backoff = NewDefault();
            for (int i = 0; i < DefaultMaxAttempts; i++)
            {
                backoff.NextDelay();
            }

            Assert.IsTrue(backoff.ExceededMaxAttempts);
            Assert.Throws<InvalidOperationException>(() => backoff.NextDelay());
            Assert.AreEqual(
                DefaultMaxAttempts,
                backoff.AttemptCount,
                "throwing path must not increment AttemptCount");
        }

        // ---- Reset ----

        [Test]
        public void Reset_AfterPartialAttempts_RestartsSeriesFromInitial()
        {
            var backoff = NewDefault();
            backoff.NextDelay(); // 250ms
            backoff.NextDelay(); // 500ms
            backoff.NextDelay(); // 1s

            backoff.Reset();

            Assert.AreEqual(0, backoff.AttemptCount);
            Assert.IsFalse(backoff.ExceededMaxAttempts);
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), backoff.NextDelay());
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), backoff.NextDelay());
        }

        [Test]
        public void Reset_AfterExceedingMaxAttempts_AllowsNextDelayAgain()
        {
            var backoff = NewDefault();
            for (int i = 0; i < DefaultMaxAttempts; i++)
            {
                backoff.NextDelay();
            }
            Assert.IsTrue(backoff.ExceededMaxAttempts);

            backoff.Reset();

            Assert.AreEqual(0, backoff.AttemptCount);
            Assert.IsFalse(backoff.ExceededMaxAttempts);
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), backoff.NextDelay());
        }

        [Test]
        public void Reset_BeforeAnyCall_IsIdempotent()
        {
            var backoff = NewDefault();

            backoff.Reset();
            backoff.Reset();

            Assert.AreEqual(0, backoff.AttemptCount);
            Assert.IsFalse(backoff.ExceededMaxAttempts);
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), backoff.NextDelay());
        }

        [Test]
        public void Reset_DoubleReset_DoesNotChangeBehavior()
        {
            var backoff = NewDefault();
            backoff.NextDelay();
            backoff.NextDelay();

            backoff.Reset();
            backoff.Reset();

            Assert.AreEqual(0, backoff.AttemptCount);
            Assert.AreEqual(TimeSpan.FromMilliseconds(250), backoff.NextDelay());
        }

        // ---- Constructor validation ----

        [Test]
        public void Constructor_NegativeInitialDelay_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReconnectBackoff(
                    TimeSpan.FromMilliseconds(-1),
                    2.0,
                    TimeSpan.FromSeconds(5),
                    20));
        }

        [Test]
        public void Constructor_MultiplierBelowOne_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReconnectBackoff(
                    TimeSpan.FromMilliseconds(250),
                    0.5,
                    TimeSpan.FromSeconds(5),
                    20));
        }

        [Test]
        public void Constructor_MultiplierIsNaN_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReconnectBackoff(
                    TimeSpan.FromMilliseconds(250),
                    double.NaN,
                    TimeSpan.FromSeconds(5),
                    20));
        }

        [Test]
        public void Constructor_MaxDelayBelowInitialDelay_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReconnectBackoff(
                    TimeSpan.FromSeconds(10),
                    2.0,
                    TimeSpan.FromSeconds(1),
                    20));
        }

        [Test]
        public void Constructor_NonPositiveMaxAttempts_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReconnectBackoff(
                    TimeSpan.FromMilliseconds(250),
                    2.0,
                    TimeSpan.FromSeconds(5),
                    0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReconnectBackoff(
                    TimeSpan.FromMilliseconds(250),
                    2.0,
                    TimeSpan.FromSeconds(5),
                    -1));
        }

        // ---- Custom parameters ----

        [Test]
        public void NextDelay_HonorsCustomMultiplierAndCap()
        {
            var backoff = new ReconnectBackoff(
                initialDelay: TimeSpan.FromSeconds(1),
                multiplier: 3.0,
                maxDelay: TimeSpan.FromSeconds(10),
                maxAttempts: 5);

            Assert.AreEqual(TimeSpan.FromSeconds(1), backoff.NextDelay());  // 1 * 3^0
            Assert.AreEqual(TimeSpan.FromSeconds(3), backoff.NextDelay());  // 1 * 3^1
            Assert.AreEqual(TimeSpan.FromSeconds(9), backoff.NextDelay());  // 1 * 3^2
            Assert.AreEqual(TimeSpan.FromSeconds(10), backoff.NextDelay()); // 27 → cap to 10
            Assert.AreEqual(TimeSpan.FromSeconds(10), backoff.NextDelay()); // cap
            Assert.IsTrue(backoff.ExceededMaxAttempts);
        }

        [Test]
        public void NextDelay_MultiplierExactlyOne_AlwaysReturnsInitialDelay()
        {
            var backoff = new ReconnectBackoff(
                initialDelay: TimeSpan.FromMilliseconds(500),
                multiplier: 1.0,
                maxDelay: TimeSpan.FromSeconds(5),
                maxAttempts: 4);

            Assert.AreEqual(TimeSpan.FromMilliseconds(500), backoff.NextDelay());
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), backoff.NextDelay());
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), backoff.NextDelay());
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), backoff.NextDelay());
            Assert.IsTrue(backoff.ExceededMaxAttempts);
        }

        [Test]
        public void NextDelay_MaxAttemptsOne_ExceedsAfterSingleCall()
        {
            var backoff = new ReconnectBackoff(
                initialDelay: TimeSpan.FromMilliseconds(100),
                multiplier: 2.0,
                maxDelay: TimeSpan.FromSeconds(1),
                maxAttempts: 1);

            Assert.IsFalse(backoff.ExceededMaxAttempts);
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), backoff.NextDelay());
            Assert.IsTrue(backoff.ExceededMaxAttempts);
            Assert.Throws<InvalidOperationException>(() => backoff.NextDelay());
        }
    }
}
