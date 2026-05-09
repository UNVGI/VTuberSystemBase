#nullable enable
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Factory for the per-camera GameObject + LocalVolume hierarchy under
    /// <c>CamerasRoot</c>. The default implementation
    /// (<c>CameraGameObjectFactory</c>) is provided by the Runtime asmdef in Task
    /// 4.1; tests substitute a fake.
    /// </summary>
    public interface ICameraGameObjectFactory
    {
        /// <summary>
        /// Creates the camera GameObject + LocalVolume in the active scene under
        /// <paramref name="parent"/>. Implementations must respect CSO-7 / CSO-8
        /// (physical properties on, default transform applied, <c>enabled=false</c>
        /// until active-set).
        /// </summary>
        CameraEntry Create(
            Transform parent,
            CameraId cameraId,
            string displayName,
            CameraType type,
            CameraDefaultTransform defaultTransform,
            int allocOrder);

        /// <summary>Destroys the GameObject + LocalVolume produced by <see cref="Create"/>.</summary>
        void Destroy(CameraEntry entry);
    }
}
