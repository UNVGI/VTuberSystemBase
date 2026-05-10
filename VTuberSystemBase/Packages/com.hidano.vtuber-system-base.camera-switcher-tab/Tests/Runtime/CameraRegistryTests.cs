#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Task 2.1 acceptance tests for <see cref="CameraRegistry"/>: insert order
    /// preservation, in-place upsert, deletion gap closure across 1k cameras.
    /// </summary>
    [TestFixture]
    public sealed class CameraRegistryTests
    {
        [Test]
        public void Upsert_PreservesInsertionOrder()
        {
            var reg = new CameraRegistry();
            for (var i = 0; i < 1000; i++)
            {
                var id = new CameraId($"cam-{i:0000}");
                reg.Upsert(new CameraMetadata { Id = id, DisplayName = $"Cam {i}" });
            }
            var list = reg.Enumerate();
            Assert.AreEqual(1000, list.Count);
            for (var i = 0; i < 1000; i++)
            {
                Assert.AreEqual($"cam-{i:0000}", list[i].Id.Value);
            }
        }

        [Test]
        public void Upsert_OverwritesExistingInPlace()
        {
            var reg = new CameraRegistry();
            var id = new CameraId("cam-a");
            Assert.IsTrue(reg.Upsert(new CameraMetadata { Id = id, DisplayName = "v1" }));
            reg.Upsert(new CameraMetadata { Id = new CameraId("cam-b"), DisplayName = "B" });
            Assert.IsFalse(reg.Upsert(new CameraMetadata { Id = id, DisplayName = "v2" }));
            var list = reg.Enumerate();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("cam-a", list[0].Id.Value);
            Assert.AreEqual("v2", list[0].DisplayName);
            Assert.AreEqual("cam-b", list[1].Id.Value);
        }

        [Test]
        public void Remove_ShiftsLaterEntries()
        {
            var reg = new CameraRegistry();
            for (var i = 0; i < 5; i++)
            {
                reg.Upsert(new CameraMetadata { Id = new CameraId($"cam-{i}") });
            }
            Assert.IsTrue(reg.Remove(new CameraId("cam-2")));
            var list = reg.Enumerate();
            Assert.AreEqual(4, list.Count);
            CollectionAssert.AreEqual(
                new[] { "cam-0", "cam-1", "cam-3", "cam-4" },
                System.Linq.Enumerable.Select(list, m => m.Id.Value));
            Assert.IsTrue(reg.TryGet(new CameraId("cam-4"), out _));
        }

        [Test]
        public void Remove_NoGapIn1000UpsertsThen500Removes()
        {
            var reg = new CameraRegistry();
            for (var i = 0; i < 1000; i++)
                reg.Upsert(new CameraMetadata { Id = new CameraId($"cam-{i:0000}") });
            for (var i = 0; i < 1000; i += 2)
                reg.Remove(new CameraId($"cam-{i:0000}"));
            Assert.AreEqual(500, reg.Count);
            var list = reg.Enumerate();
            for (var k = 0; k < list.Count; k++)
            {
                var idx = k * 2 + 1;
                Assert.AreEqual($"cam-{idx:0000}", list[k].Id.Value);
            }
        }

        [Test]
        public void TryGet_ReturnsFalseForUnsetId()
        {
            var reg = new CameraRegistry();
            Assert.IsFalse(reg.TryGet(default, out var meta));
            Assert.IsNull(meta);
        }
    }

    [TestFixture]
    public sealed class ActiveCameraTrackerTests
    {
        [Test]
        public void SetActive_FiresOnlyOnActualTransition()
        {
            var tracker = new ActiveCameraTracker();
            int active = 0, editing = 0;
            tracker.OnActiveChanged += _ => active++;
            tracker.OnEditingChanged += _ => editing++;
            tracker.SetActive(new CameraId("cam-a"));
            tracker.SetActive(new CameraId("cam-a"));
            Assert.AreEqual(1, active);
            tracker.SetActive(new CameraId("cam-b"));
            Assert.AreEqual(2, active);
            Assert.AreEqual(0, editing);
        }

        [Test]
        public void EditingAndActive_AreIndependent()
        {
            var tracker = new ActiveCameraTracker();
            tracker.SetActive(new CameraId("cam-a"));
            tracker.SetEditing(new CameraId("cam-b"));
            Assert.AreEqual("cam-a", tracker.ActiveCameraId.Value);
            Assert.AreEqual("cam-b", tracker.EditingCameraId.Value);
            Assert.IsTrue(tracker.ActiveAndEditingDiverge);
        }

        [Test]
        public void TransitionToUnset_FiresOnce()
        {
            var tracker = new ActiveCameraTracker();
            int active = 0;
            tracker.OnActiveChanged += _ => active++;
            tracker.SetActive(new CameraId("cam-a"));
            tracker.SetActive(default);
            tracker.SetActive(default);
            Assert.AreEqual(2, active);
            Assert.IsFalse(tracker.ActiveCameraId.HasValue);
        }
    }
}
