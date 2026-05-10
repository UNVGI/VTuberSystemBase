namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Wire-format 3D vector used in light DTOs and preset configs. Avoids depending on
    /// <c>UnityEngine.Vector3</c> in Contracts to keep the assembly engine-agnostic where
    /// practical.
    /// </summary>
    public readonly record struct Vector3Dto(float X, float Y, float Z);
}
