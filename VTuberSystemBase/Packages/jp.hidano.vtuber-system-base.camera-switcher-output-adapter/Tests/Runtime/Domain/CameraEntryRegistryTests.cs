#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Domain
{
    [TestFixture]
    public sealed class CameraEntryRegistryTests
    {
        [Test]
        public void Upsert100AndRemoveHalf_RetainsAllocOrder()
        {
            var registry = new CameraEntryRegistry();
            for (var i = 1; i <= 100; i++)
            {
                registry.Upsert(MakeEntry(i));
            }

            // Remove every odd cameraId.
            for (var i = 1; i <= 100; i += 2)
            {
                Assert.That(registry.Remove(new CameraId($"cam-{i:D4}")), Is.True);
            }

            Assert.That(registry.Count, Is.EqualTo(50));
            var enumerated = registry.Enumerate();
            for (var i = 0; i < enumerated.Count - 1; i++)
            {
                Assert.That(enumerated[i].AllocOrder, Is.LessThan(enumerated[i + 1].AllocOrder));
            }
            Assert.That(enumerated[0].CameraId.Value, Is.EqualTo("cam-0002"));
            Assert.That(enumerated[^1].CameraId.Value, Is.EqualTo("cam-0100"));
        }

        [Test]
        public void RemoveUnknownCameraId_IsNoOp()
        {
            var registry = new CameraEntryRegistry();
            Assert.That(registry.Remove(new CameraId("cam-9999")), Is.False);
        }

        [Test]
        public void TryGet_ReturnsLatestEntry()
        {
            var registry = new CameraEntryRegistry();
            var first = MakeEntry(1, displayName: "first");
            registry.Upsert(first);
            Assert.That(registry.TryGet(new CameraId("cam-0001"), out var got), Is.True);
            Assert.That(got, Is.SameAs(first));

            var replaced = MakeEntry(1, displayName: "second");
            registry.Upsert(replaced);
            Assert.That(registry.TryGet(new CameraId("cam-0001"), out got), Is.True);
            Assert.That(got.DisplayName, Is.EqualTo("second"));
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        private static CameraEntry MakeEntry(int allocOrder, string? displayName = null) => new CameraEntry(
            cameraId: new CameraId($"cam-{allocOrder:D4}"),
            displayName: displayName ?? $"Cam{allocOrder}",
            type: CameraType.Perspective,
            defaultTransform: new CameraDefaultTransform
            {
                Position = new[] { 0f, 0f, 0f },
                Rotation = new[] { 0f, 0f, 0f, 1f },
                FocalLengthMm = 50f,
            },
            allocOrder: allocOrder,
            gameObject: null,
            cameraComponent: null,
            localVolume: null);
    }
}
