#nullable enable
using UnityEngine;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime
{
    /// <summary>
    /// Default <see cref="ICameraGameObjectFactory"/> that builds the per-camera
    /// hierarchy under <c>CamerasRoot</c> using physical-properties cameras and a
    /// child Local Volume produced by <see cref="ILocalVolumeBinder"/> (Requirement
    /// 3.2 / 5.4 / 6.1 / 6.8).
    /// </summary>
    public sealed class CameraGameObjectFactory : ICameraGameObjectFactory
    {
        private readonly ILocalVolumeBinder _volumeBinder;
        private readonly Vector2 _defaultSensorSize;

        public CameraGameObjectFactory(ILocalVolumeBinder volumeBinder, Vector2 defaultSensorSize)
        {
            _volumeBinder = volumeBinder ?? throw new System.ArgumentNullException(nameof(volumeBinder));
            _defaultSensorSize = defaultSensorSize.x > 0f && defaultSensorSize.y > 0f
                ? defaultSensorSize
                : new Vector2(36f, 24f);
        }

        public CameraEntry Create(
            Transform parent,
            CameraId cameraId,
            string displayName,
            CameraType type,
            CameraDefaultTransform defaultTransform,
            int allocOrder)
        {
            var go = new GameObject($"Camera-{cameraId.Value}-{displayName}");
            go.transform.SetParent(parent, worldPositionStays: false);

            ApplyTransform(go.transform, defaultTransform);

            var camera = go.AddComponent<Camera>();
            camera.usePhysicalProperties = true;
            camera.sensorSize = _defaultSensorSize;
            camera.focalLength = defaultTransform.FocalLengthMm > 0f ? defaultTransform.FocalLengthMm : 50f;
            camera.orthographic = type == CameraType.Orthographic;
            camera.enabled = false;

            var volume = _volumeBinder.CreateLocalVolume(go, cameraId, allocOrder);

            return new CameraEntry(cameraId, displayName, type, defaultTransform, allocOrder, go, camera, volume);
        }

        public void Destroy(CameraEntry entry)
        {
            if (entry.LocalVolume != null) _volumeBinder.DestroyLocalVolume(entry.LocalVolume);
            if (entry.GameObject != null) Object.Destroy(entry.GameObject);
        }

        private static void ApplyTransform(Transform t, CameraDefaultTransform defaults)
        {
            var p = defaults.Position;
            if (p != null && p.Length >= 3) t.position = new Vector3(p[0], p[1], p[2]);
            var r = defaults.Rotation;
            if (r != null && r.Length >= 4) t.rotation = new Quaternion(r[0], r[1], r[2], r[3]);
        }
    }
}
