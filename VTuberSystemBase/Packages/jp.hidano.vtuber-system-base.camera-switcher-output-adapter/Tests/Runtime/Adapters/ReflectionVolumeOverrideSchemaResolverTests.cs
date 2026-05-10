#nullable enable
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Adapters
{
    [TestFixture]
    public sealed class ReflectionVolumeOverrideSchemaResolverTests
    {
        [UnityTest]
        public IEnumerator GetSchema_IncludesUrpStandardOverrides()
        {
            yield return null;
            var resolver = new ReflectionVolumeOverrideSchemaResolver();
            var schema = resolver.GetSchema();
            Assert.That(schema.Overrides, Is.Not.Null);
            Assert.That(schema.Overrides.Count, Is.GreaterThan(0));

            var bloom = schema.Overrides.FirstOrDefault(o => o.Type == "Bloom");
            Assert.That(bloom.Type, Is.EqualTo("Bloom"));
            Assert.That(bloom.Params, Is.Not.Null);
            Assert.That(bloom.Params.Count, Is.GreaterThan(0));
            Assert.That(bloom.Params.Any(p => p.Name == "intensity" && p.TypeTag == "float"), Is.True);
            Assert.That(bloom.Params.Any(p => p.Name == "tint" && p.TypeTag == "color"), Is.True);
        }

        [UnityTest]
        public IEnumerator GetSchema_CachesResultAcrossCalls()
        {
            yield return null;
            var resolver = new ReflectionVolumeOverrideSchemaResolver();
            var first = resolver.GetSchema();
            var second = resolver.GetSchema();
            // Cached: same instance reference for the IReadOnlyList<>.
            Assert.That(second.Overrides, Is.SameAs(first.Overrides));
        }

        [UnityTest]
        public IEnumerator EnumParameter_HasEnumValues()
        {
            yield return null;
            var resolver = new ReflectionVolumeOverrideSchemaResolver();
            var schema = resolver.GetSchema();
            var tonemapping = schema.Overrides.FirstOrDefault(o => o.Type == "Tonemapping");
            Assert.That(tonemapping.Type, Is.EqualTo("Tonemapping"));
            var modeParam = tonemapping.Params.FirstOrDefault(p => p.Name == "mode");
            Assert.That(modeParam.Name, Is.EqualTo("mode"));
            Assert.That(modeParam.TypeTag, Is.EqualTo("enum"));
            Assert.That(modeParam.EnumValues, Is.Not.Null);
            Assert.That(modeParam.EnumValues!.Count, Is.GreaterThan(0));
        }
    }
}
