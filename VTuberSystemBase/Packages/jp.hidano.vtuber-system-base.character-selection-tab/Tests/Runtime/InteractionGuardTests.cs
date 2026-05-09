#nullable enable
using NUnit.Framework;
using System;
using System.Collections.Generic;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 2.2 acceptance tests: 199ms / 200ms / 201ms threshold behaviour and
    /// explicit-end vs. auto-end paths.
    /// </summary>
    [TestFixture]
    public sealed class InteractionGuardTests
    {
        [Test]
        public void Mark_ThenAdvanceBelowThreshold_KeepsInteracting()
        {
            var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var g = new InteractionGuard(clock, TimeSpan.FromMilliseconds(200));
            var events = new List<InteractingChangedEventArgs>();
            g.OnChanged += events.Add;

            g.MarkInteracting("s1", "smile");
            clock.Advance(TimeSpan.FromMilliseconds(199));

            Assert.IsTrue(g.IsInteracting("s1", "smile"));
            Assert.AreEqual(1, events.Count);
            Assert.IsTrue(events[0].IsInteracting);
        }

        [Test]
        public void Mark_ThenAdvanceAtOrAboveThreshold_FiresFalse()
        {
            var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var g = new InteractionGuard(clock, TimeSpan.FromMilliseconds(200));
            var events = new List<InteractingChangedEventArgs>();
            g.OnChanged += events.Add;

            g.MarkInteracting("s1", "smile");
            clock.Advance(TimeSpan.FromMilliseconds(200));

            Assert.IsFalse(g.IsInteracting("s1", "smile"));
            Assert.AreEqual(2, events.Count);
            Assert.IsFalse(events[1].IsInteracting);
        }

        [Test]
        public void Mark_ThenAdvancePastThreshold_FiresFalseExactlyOnce()
        {
            var clock = new ManualClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var g = new InteractionGuard(clock, TimeSpan.FromMilliseconds(200));
            int falseCount = 0;
            g.OnChanged += a => { if (!a.IsInteracting) falseCount++; };

            g.MarkInteracting("s1", "smile");
            clock.Advance(TimeSpan.FromMilliseconds(201));
            clock.Advance(TimeSpan.FromMilliseconds(50)); // additional ticks should not refire

            Assert.AreEqual(1, falseCount);
        }

        [Test]
        public void EndInteracting_FiresFalseImmediately()
        {
            var clock = new ManualClock();
            var g = new InteractionGuard(clock, TimeSpan.FromMilliseconds(200));
            int falseCount = 0;
            g.OnChanged += a => { if (!a.IsInteracting) falseCount++; };

            g.MarkInteracting("s1", "smile");
            g.EndInteracting("s1", "smile");

            Assert.IsFalse(g.IsInteracting("s1", "smile"));
            Assert.AreEqual(1, falseCount);
        }

        [Test]
        public void DifferentKeysAreIndependent()
        {
            var clock = new ManualClock();
            var g = new InteractionGuard(clock, TimeSpan.FromMilliseconds(200));

            g.MarkInteracting("s1", "smile");
            g.MarkInteracting("s2", "smile");

            Assert.IsTrue(g.IsInteracting("s1", "smile"));
            Assert.IsTrue(g.IsInteracting("s2", "smile"));
            g.EndInteracting("s1", "smile");
            Assert.IsFalse(g.IsInteracting("s1", "smile"));
            Assert.IsTrue(g.IsInteracting("s2", "smile"));
        }
    }
}
