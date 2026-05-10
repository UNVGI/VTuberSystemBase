#nullable enable
using NUnit.Framework;
using UnityEngine.Rendering;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class VolumeParameterKindResolverTests
    {
        private enum SampleEnum { A, B }
        private sealed class SampleEnumParameter : VolumeParameter<SampleEnum> { }

        [Test]
        public void Resolves_PrimitiveParameters()
        {
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(BoolParameter)), Is.EqualTo(ParamKind.Bool));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(IntParameter)), Is.EqualTo(ParamKind.Int));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(NoInterpIntParameter)), Is.EqualTo(ParamKind.Int));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(MinIntParameter)), Is.EqualTo(ParamKind.Int));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(MaxIntParameter)), Is.EqualTo(ParamKind.Int));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(ClampedIntParameter)), Is.EqualTo(ParamKind.Int));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(FloatParameter)), Is.EqualTo(ParamKind.Float));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(NoInterpFloatParameter)), Is.EqualTo(ParamKind.Float));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(MinFloatParameter)), Is.EqualTo(ParamKind.Float));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(MaxFloatParameter)), Is.EqualTo(ParamKind.Float));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(ClampedFloatParameter)), Is.EqualTo(ParamKind.ClampedFloat));
        }

        [Test]
        public void Resolves_ColorAndVectors()
        {
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(ColorParameter)), Is.EqualTo(ParamKind.Color));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(Vector2Parameter)), Is.EqualTo(ParamKind.Vector2));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(Vector3Parameter)), Is.EqualTo(ParamKind.Vector3));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(Vector4Parameter)), Is.EqualTo(ParamKind.Vector4));
        }

        [Test]
        public void Resolves_EnumGenericArgument()
        {
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(SampleEnumParameter)), Is.EqualTo(ParamKind.Enum));
        }

        [Test]
        public void NullOrUnrelated_ReturnsUnknown()
        {
            Assert.That(VolumeParameterKindResolver.Resolve(null), Is.EqualTo(ParamKind.Unknown));
            Assert.That(VolumeParameterKindResolver.Resolve(typeof(string)), Is.EqualTo(ParamKind.Unknown));
        }
    }
}
