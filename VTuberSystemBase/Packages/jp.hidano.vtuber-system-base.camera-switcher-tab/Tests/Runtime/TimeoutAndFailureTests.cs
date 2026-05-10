#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Domain;
using VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    [TestFixture]
    public sealed class TimeoutTrackerTests
    {
        [Test]
        public void DefaultTimeout_FiresAfterFiveSeconds()
        {
            var time = new FakeTimeProvider();
            var tracker = new TimeoutTracker(time);
            string? fired = null;
            tracker.OnTimeout += id => fired = id;

            tracker.Arm("req-1");
            time.Advance(TimeSpan.FromSeconds(4));
            Assert.IsNull(fired);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.AreEqual("req-1", fired);
            Assert.AreEqual(0, tracker.ArmedCount);
        }

        [Test]
        public void Cancel_SuppressesPendingTimeout()
        {
            var time = new FakeTimeProvider();
            var tracker = new TimeoutTracker(time);
            int fires = 0;
            tracker.OnTimeout += _ => fires++;
            tracker.Arm("req-1");
            Assert.IsTrue(tracker.Cancel("req-1"));
            time.Advance(TimeSpan.FromSeconds(10));
            Assert.AreEqual(0, fires);
        }

        [Test]
        public void DuplicateArm_IsNoop()
        {
            var time = new FakeTimeProvider();
            var tracker = new TimeoutTracker(time);
            int fires = 0;
            tracker.OnTimeout += _ => fires++;
            tracker.Arm("req-1");
            tracker.Arm("req-1"); // ignored — existing timer survives
            time.Advance(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void Arm_AcceptsCustomTimeout()
        {
            var time = new FakeTimeProvider();
            var tracker = new TimeoutTracker(time);
            int fires = 0;
            tracker.OnTimeout += _ => fires++;
            tracker.Arm("req-1", TimeSpan.FromMilliseconds(200));
            time.Advance(TimeSpan.FromMilliseconds(199));
            Assert.AreEqual(0, fires);
            time.Advance(TimeSpan.FromMilliseconds(2));
            Assert.AreEqual(1, fires);
        }

        [Test]
        public void Arm_RejectsEmptyId()
        {
            var time = new FakeTimeProvider();
            var tracker = new TimeoutTracker(time);
            Assert.Throws<ArgumentException>(() => tracker.Arm(""));
        }
    }

    [TestFixture]
    public sealed class FailureAggregatorTests
    {
        [Test]
        public void Record_IncrementsKindAndTotal()
        {
            var agg = new FailureAggregator();
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            agg.Record(FailureKind.OscFailure, "udp", now);
            agg.Record(FailureKind.OscFailure, "udp", now);
            agg.Record(FailureKind.PresetIoFailure, "io", now);
            Assert.AreEqual(2, agg.CountOf(FailureKind.OscFailure));
            Assert.AreEqual(1, agg.CountOf(FailureKind.PresetIoFailure));
            Assert.AreEqual(0, agg.CountOf(FailureKind.IpcSendFailure));
            Assert.AreEqual(3, agg.TotalCount);
        }

        [Test]
        public void History_BoundedToConfiguredSize()
        {
            var agg = new FailureAggregator(historySize: 4);
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 10; i++)
            {
                agg.Record(FailureKind.OscFailure, $"m{i}", now);
            }
            var recent = agg.RecentRecords();
            Assert.AreEqual(4, recent.Count);
            Assert.AreEqual("m6", recent[0].Message);
            Assert.AreEqual("m9", recent[3].Message);
        }

        [Test]
        public void OnFailureRecorded_FiresWithRecord()
        {
            var agg = new FailureAggregator();
            FailureRecord? captured = null;
            agg.OnFailureRecorded += r => captured = r;
            agg.Record(FailureKind.CameraError, "boom", DateTimeOffset.UtcNow);
            Assert.IsNotNull(captured);
            Assert.AreEqual(FailureKind.CameraError, captured!.Value.Kind);
        }
    }
}
