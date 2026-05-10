#nullable enable
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal
{
    /// <summary>
    /// Converters between Contracts wire DTO types and UnityEngine native types.
    /// All conversions are pure and side-effect free.
    /// </summary>
    internal static class DtoConverters
    {
        // ----- Color -----
        public static Color ToUnity(ColorDto dto) => new Color(dto.R, dto.G, dto.B, dto.A);
        public static ColorDto ToDto(Color color) => new ColorDto(color.r, color.g, color.b, color.a);

        // ----- Vector3 -----
        public static Vector3 ToUnity(Vector3Dto dto) => new Vector3(dto.X, dto.Y, dto.Z);
        public static Vector3Dto ToDto(Vector3 v) => new Vector3Dto(v.x, v.y, v.z);

        /// <summary>
        /// Treats the Vector3 DTO as Euler angles (degrees) and constructs a Quaternion.
        /// </summary>
        public static Quaternion ToQuaternion(Vector3Dto eulerDeg)
            => Quaternion.Euler(eulerDeg.X, eulerDeg.Y, eulerDeg.Z);

        // ----- Vector4 / Vector2 / Vector3 carried by Vector4Dto -----
        public static Vector4 ToUnity(Vector4Dto dto) => new Vector4(dto.X, dto.Y, dto.Z, dto.W);
        public static Vector4Dto ToDto(Vector4 v) => new Vector4Dto(v.x, v.y, v.z, v.w);

        public static Vector2 ToUnityVector2(Vector4Dto dto) => new Vector2(dto.X, dto.Y);
        public static Vector4Dto ToDtoVector4(Vector2 v) => new Vector4Dto(v.x, v.y, 0f, 0f);

        public static Vector3 ToUnityVector3(Vector4Dto dto) => new Vector3(dto.X, dto.Y, dto.Z);
        public static Vector4Dto ToDtoVector4(Vector3 v) => new Vector4Dto(v.x, v.y, v.z, 0f);

        // ----- LightType -----
        public static LightType ToUnity(LightTypeDto dto)
            => dto switch
            {
                LightTypeDto.Directional => LightType.Directional,
                LightTypeDto.Point => LightType.Point,
                LightTypeDto.Spot => LightType.Spot,
                LightTypeDto.Area => LightType.Rectangle,
                _ => LightType.Point,
            };

        public static LightTypeDto ToDto(LightType type)
            => type switch
            {
                LightType.Directional => LightTypeDto.Directional,
                LightType.Point => LightTypeDto.Point,
                LightType.Spot => LightTypeDto.Spot,
                LightType.Rectangle => LightTypeDto.Area,
                LightType.Disc => LightTypeDto.Area,
                _ => LightTypeDto.Point,
            };
    }
}
