#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    /// <summary>
    /// In-memory <see cref="IOutputSceneRoots"/> double. Allocates real GameObjects /
    /// Camera / VolumeProfile so tests exercise true Unity APIs (URP VolumeProfile, Camera
    /// culling mask) without requiring an OutputSceneBootstrapper.
    /// </summary>
    internal sealed class FakeOutputSceneRoots : IOutputSceneRoots, IDisposable
    {
        public Transform Stage { get; }
        public Transform Characters { get; }
        public Transform Lights { get; }
        public Transform Cameras { get; }
        public Transform Volumes { get; }
        public VolumeProfile? GlobalVolumeProfile { get; }
        public Camera? DefaultCamera { get; }

        private readonly GameObject _root;
        private readonly GameObject _camGo;
        private bool _disposed;

        public FakeOutputSceneRoots()
        {
            _root = new GameObject("FakeOutputSceneRoots");
            Stage = MakeChild("StageRoot");
            Characters = MakeChild("CharactersRoot");
            Lights = MakeChild("LightsRoot");
            Cameras = MakeChild("CamerasRoot");
            Volumes = MakeChild("VolumesRoot");

            _camGo = new GameObject("FakeDefaultCamera");
            _camGo.transform.SetParent(Cameras, worldPositionStays: false);
            DefaultCamera = _camGo.AddComponent<Camera>();

            GlobalVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            GlobalVolumeProfile.name = "FakeGlobalVolumeProfile";
        }

        private Transform MakeChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            return go.transform;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (GlobalVolumeProfile != null) Object.DestroyImmediate(GlobalVolumeProfile);
            if (_root != null) Object.DestroyImmediate(_root);
        }
    }
}
