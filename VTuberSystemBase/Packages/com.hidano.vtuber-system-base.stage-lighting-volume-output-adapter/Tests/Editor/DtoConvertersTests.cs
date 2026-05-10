#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class DtoConvertersTests
    {
        [Test]
        public void Color_Roundtrip_PreservesRgba()
        {
            var dto = new ColorDto(0.1f, 0.2f, 0.3f, 0.4f);
            var unity = DtoConverters.ToUnity(dto);
            var back = DtoConverters.ToDto(unity);
            Assert.That(back.R, Is.EqualTo(dto.R).Within(1e-6f));
            Assert.That(back.G, Is.EqualTo(dto.G).Within(1e-6f));
            Assert.That(back.B, Is.EqualTo(dto.B).Within(1e-6f));
            Assert.That(back.A, Is.EqualTo(dto.A).Within(1e-6f));
        }

        [Test]
        public void Vector3_Roundtrip_PreservesXyz()
        {
            var dto = new Vector3Dto(1.5f, -2.5f, 3.5f);
            var unity = DtoConverters.ToUnity(dto);
            var back = DtoConverters.ToDto(unity);
            Assert.That(back, Is.EqualTo(dto));
        }

        [Test]
        public void ToQuaternion_FromEulerDegrees_MatchesUnityQuaternionEuler()
        {
            var dto = new Vector3Dto(45f, 90f, 0f);
            var q = DtoConverters.ToQuaternion(dto);
            var expected = Quaternion.Euler(dto.X, dto.Y, dto.Z);
            Assert.That(q.x, Is.EqualTo(expected.x).Within(1e-6f));
            Assert.That(q.y, Is.EqualTo(expected.y).Within(1e-6f));
            Assert.That(q.z, Is.EqualTo(expected.z).Within(1e-6f));
            Assert.That(q.w, Is.EqualTo(expected.w).Within(1e-6f));
        }

        [Test]
        public void Vector4_Roundtrip_PreservesXyzw()
        {
            var dto = new Vector4Dto(1f, 2f, 3f, 4f);
            var unity = DtoConverters.ToUnity(dto);
            var back = DtoConverters.ToDto(unity);
            Assert.That(back, Is.EqualTo(dto));
        }

        [Test]
        public void Vector4Dto_PackVector2_Roundtrip()
        {
            var dto = new Vector4Dto(0.25f, 0.75f, 0f, 0f);
            var v = DtoConverters.ToUnityVector2(dto);
            var back = DtoConverters.ToDtoVector4(v);
            Assert.That(back, Is.EqualTo(dto));
        }

        [Test]
        public void Vector4Dto_PackVector3_Roundtrip()
        {
            var dto = new Vector4Dto(1f, 2f, 3f, 0f);
            var v = DtoConverters.ToUnityVector3(dto);
            var back = DtoConverters.ToDtoVector4(v);
            Assert.That(back, Is.EqualTo(dto));
        }

        [TestCase(LightTypeDto.Directional, LightType.Directional)]
        [TestCase(LightTypeDto.Point, LightType.Point)]
        [TestCase(LightTypeDto.Spot, LightType.Spot)]
        public void LightType_KnownValues_Roundtrip(LightTypeDto dto, LightType unityType)
        {
            Assert.That(DtoConverters.ToUnity(dto), Is.EqualTo(unityType));
            Assert.That(DtoConverters.ToDto(unityType), Is.EqualTo(dto));
        }

        [Test]
        public void LightType_Area_MapsToRectangle()
        {
            // Unity has no single "Area" type; Rectangle/Disc are the modern equivalents.
            Assert.That(DtoConverters.ToUnity(LightTypeDto.Area), Is.EqualTo(LightType.Rectangle));
            Assert.That(DtoConverters.ToDto(LightType.Rectangle), Is.EqualTo(LightTypeDto.Area));
            Assert.That(DtoConverters.ToDto(LightType.Disc), Is.EqualTo(LightTypeDto.Area));
        }
    }
}
