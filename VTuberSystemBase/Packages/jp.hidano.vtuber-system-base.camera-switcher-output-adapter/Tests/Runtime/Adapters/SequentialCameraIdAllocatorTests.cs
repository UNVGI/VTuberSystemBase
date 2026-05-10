#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Allocator;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Adapters
{
    [TestFixture]
    public sealed class SequentialCameraIdAllocatorTests
    {
        [Test]
        public void FirstThreeAllocations_AreCam0001ThroughCam0003()
        {
            var allocator = new SequentialCameraIdAllocator();
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-0001"));
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-0002"));
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-0003"));
        }

        [Test]
        public void AllocationsBeyond9999_WidenWithoutPadding()
        {
            var allocator = new SequentialCameraIdAllocator(seed: 9999);
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-9999"));
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-10000"));
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-10001"));
        }

        [Test]
        public void AllocatedCameraId_IsAlwaysOscAddressSafe()
        {
            var allocator = new SequentialCameraIdAllocator();
            for (var i = 0; i < 10010; i++)
            {
                var id = allocator.Allocate();
                Assert.That(OscAddressBuilder.IsValidCameraIdSegment(id.Value), Is.True,
                    $"id was {id.Value}");
            }
        }

        [Test]
        public void CustomPrefix_IsHonoured()
        {
            var allocator = new SequentialCameraIdAllocator(prefix: "out-cam_");
            Assert.That(allocator.Allocate().Value, Is.EqualTo("out-cam_0001"));
        }

        [Test]
        public void Counter_DoesNotReuseAfterAllocation()
        {
            var allocator = new SequentialCameraIdAllocator();
            allocator.Allocate();
            allocator.Allocate();
            // No "release" API. Next is always +1.
            Assert.That(allocator.NextCounter, Is.EqualTo(3));
            Assert.That(allocator.Allocate().Value, Is.EqualTo("cam-0003"));
        }
    }
}
