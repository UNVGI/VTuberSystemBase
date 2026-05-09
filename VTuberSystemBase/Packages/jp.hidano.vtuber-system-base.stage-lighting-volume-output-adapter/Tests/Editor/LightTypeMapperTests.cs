#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class LightTypeMapperTests
    {
        [TestCase(LightTypeDto.Directional, LightType.Directional)]
        [TestCase(LightTypeDto.Point, LightType.Point)]
        [TestCase(LightTypeDto.Spot, LightType.Spot)]
        public void Roundtrip_PreservesPrimaryTypes(LightTypeDto dto, LightType unity)
        {
            Assert.That(LightTypeMapper.ToUnity(dto), Is.EqualTo(unity));
            Assert.That(LightTypeMapper.ToDto(unity), Is.EqualTo(dto));
        }

        [Test]
        public void Area_MapsToRectangle_ButRoundtripsToArea()
        {
            Assert.That(LightTypeMapper.ToUnity(LightTypeDto.Area), Is.EqualTo(LightType.Rectangle));
            Assert.That(LightTypeMapper.ToDto(LightType.Rectangle), Is.EqualTo(LightTypeDto.Area));
            Assert.That(LightTypeMapper.ToDto(LightType.Disc), Is.EqualTo(LightTypeDto.Area));
        }
    }
}
