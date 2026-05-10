#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class VolumeOverrideRegistryTests
    {
        [Test]
        public void Build_PopulatesBothDirections()
        {
            var r = new VolumeOverrideRegistry();
            r.Build(new[] { typeof(string), typeof(int) });
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r.Contains(typeof(string).FullName!), Is.True);
            Assert.That(r.GetTypeByFullName(typeof(int).FullName!, out var t), Is.True);
            Assert.That(t, Is.EqualTo(typeof(int)));
            Assert.That(r.GetFullNameByType(typeof(string), out var n), Is.True);
            Assert.That(n, Is.EqualTo(typeof(string).FullName));
        }

        [Test]
        public void Unknown_ReturnsFalse()
        {
            var r = new VolumeOverrideRegistry();
            r.Build(System.Array.Empty<System.Type>());
            Assert.That(r.GetTypeByFullName("Foo.Bar", out _), Is.False);
            Assert.That(r.Contains("Foo.Bar"), Is.False);
        }

        [Test]
        public void Build_RebuildsCleanly()
        {
            var r = new VolumeOverrideRegistry();
            r.Build(new[] { typeof(string) });
            r.Build(new[] { typeof(int) });
            Assert.That(r.Contains(typeof(string).FullName!), Is.False);
            Assert.That(r.Contains(typeof(int).FullName!), Is.True);
        }
    }
}
