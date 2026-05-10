#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Test double for <see cref="ICameraGameObjectFactory"/>. Spawns a real
    /// GameObject + Camera + Volume hierarchy under <c>parent</c> so the adapter
    /// can flip <c>enabled</c> state on real components, then tracks them for
    /// teardown via <see cref="DestroyAllCreated"/>.
    /// </summary>
    public sealed class FakeCameraGameObjectFactory : ICameraGameObjectFactory
    {
        private readonly List<GameObject> _spawned = new();

        public List<CameraEntry> CreatedEntries { get; } = new();
        public List<CameraId> DestroyedCameraIds { get; } = new();

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
            var camera = go.AddComponent<Camera>();
            camera.usePhysicalProperties = true;
            camera.focalLength = defaultTransform.FocalLengthMm > 0f ? defaultTransform.FocalLengthMm : 50f;
            camera.sensorSize = new Vector2(36f, 24f);
            camera.orthographic = type == CameraType.Orthographic;
            camera.enabled = false;

            var volumeGo = new GameObject($"LocalVolume-{cameraId.Value}");
            volumeGo.transform.SetParent(go.transform, worldPositionStays: false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.weight = 1f;
            volume.priority = allocOrder;
            volume.enabled = false;

            _spawned.Add(go);
            var entry = new CameraEntry(cameraId, displayName, type, defaultTransform, allocOrder, go, camera, volume);
            CreatedEntries.Add(entry);
            return entry;
        }

        public void Destroy(CameraEntry entry)
        {
            DestroyedCameraIds.Add(entry.CameraId);
            if (entry.GameObject != null)
            {
                _spawned.Remove(entry.GameObject);
                Object.Destroy(entry.GameObject);
            }
        }

        public void DestroyAllCreated()
        {
            foreach (var go in _spawned) if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }
    }
}
