#nullable enable
using System.Linq;
using NUnit.Framework;
using UnityEngine.Rendering.Universal;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class VolumeOverrideMetadataBuilderTests
    {
        [Test]
        public void Build_BloomTonemappingColorAdjustments_ReturnsExpectedTypes()
        {
            var b = new VolumeOverrideMetadataBuilder();
            var schema = b.Build(new[] { typeof(Bloom), typeof(Tonemapping), typeof(ColorAdjustments) });
            Assert.That(schema.SchemaVersion, Is.EqualTo(VolumeOverrideMetadataBuilder.SchemaVersion));
            Assert.That(schema.Types.Count, Is.EqualTo(3));

            var bloom = schema.Types.FirstOrDefault(t => t.TypeFullName == typeof(Bloom).FullName);
            Assert.That(bloom.TypeFullName, Is.EqualTo(typeof(Bloom).FullName));
            // Bloom.intensity is a MinFloatParameter -> Float kind.
            var intensity = bloom.Params.FirstOrDefault(p => p.ParamName == "intensity");
            Assert.That(intensity.ParamName, Is.EqualTo("intensity"));
            Assert.That(intensity.Kind, Is.EqualTo(ParamKind.Float));

            var tonemap = schema.Types.First(t => t.TypeFullName == typeof(Tonemapping).FullName);
            // Tonemapping.mode is an enum parameter.
            var mode = tonemap.Params.First(p => p.ParamName == "mode");
            Assert.That(mode.Kind, Is.EqualTo(ParamKind.Enum));
            Assert.That(mode.Range.HasValue, Is.True);
            Assert.That(mode.Range!.Value.EnumValues, Is.Not.Null);
            Assert.That(mode.Range!.Value.EnumValues!.Count, Is.GreaterThan(0));

            var colorAdj = schema.Types.First(t => t.TypeFullName == typeof(ColorAdjustments).FullName);
            // ColorAdjustments.colorFilter is a ColorParameter.
            var filter = colorAdj.Params.First(p => p.ParamName == "colorFilter");
            Assert.That(filter.Kind, Is.EqualTo(ParamKind.Color));
        }

        [Test]
        public void Build_NullList_ReturnsEmptySchema()
        {
            var b = new VolumeOverrideMetadataBuilder();
            var schema = b.Build(null!);
            Assert.That(schema.Types.Count, Is.EqualTo(0));
            Assert.That(schema.SchemaVersion, Is.EqualTo(VolumeOverrideMetadataBuilder.SchemaVersion));
        }
    }
}
