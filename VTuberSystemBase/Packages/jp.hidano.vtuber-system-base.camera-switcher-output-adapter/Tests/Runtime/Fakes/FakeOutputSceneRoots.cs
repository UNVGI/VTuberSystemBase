#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Test double for <see cref="IOutputSceneRoots"/> that builds a minimal
    /// transient hierarchy under a single test-owned root.
    /// </summary>
    /// <remarks>
    /// PlayMode tests build a real <see cref="GameObject"/> hierarchy (CamerasRoot
    /// + DefaultCamera) so the adapter can mutate <c>Camera.enabled</c> and parent
    /// new cameras under <see cref="Cameras"/>. Edit-mode tests may construct an
    /// instance with all roots set to <c>null</c>; the adapter is required to
    /// tolerate null <c>DefaultCamera</c> per <see cref="IOutputSceneRoots"/> doc.
    /// </remarks>
    public sealed class FakeOutputSceneRoots : IOutputSceneRoots
    {
        private GameObject? _ownerRoot;

        public Transform? Stage { get; private set; }
        public Transform? Characters { get; private set; }
        public Transform? Lights { get; private set; }
        public Transform? Cameras { get; private set; }
        public Transform? Volumes { get; private set; }
        public VolumeProfile? GlobalVolumeProfile { get; private set; }
        public Camera? DefaultCamera { get; private set; }

        Transform IOutputSceneRoots.Stage => Stage!;
        Transform IOutputSceneRoots.Characters => Characters!;
        Transform IOutputSceneRoots.Lights => Lights!;
        Transform IOutputSceneRoots.Cameras => Cameras!;
        Transform IOutputSceneRoots.Volumes => Volumes!;
        VolumeProfile? IOutputSceneRoots.GlobalVolumeProfile => GlobalVolumeProfile;
        Camera? IOutputSceneRoots.DefaultCamera => DefaultCamera;

        /// <summary>
        /// Builds a real, transient hierarchy in the active scene. Call from PlayMode
        /// tests; pair with <see cref="DestroyHierarchy"/> in <c>[TearDown]</c>.
        /// </summary>
        public void BuildHierarchy(string ownerName = "FakeOutputSceneRoots")
        {
            _ownerRoot = new GameObject(ownerName);
            Stage = NewChild("StageRoot");
            Characters = NewChild("CharactersRoot");
            Lights = NewChild("LightsRoot");
            Cameras = NewChild("CamerasRoot");
            Volumes = NewChild("VolumeRoot");

            var defaultCameraGo = new GameObject("DefaultCamera");
            defaultCameraGo.transform.SetParent(Cameras, worldPositionStays: false);
            DefaultCamera = defaultCameraGo.AddComponent<Camera>();
            DefaultCamera.enabled = true;

            GlobalVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        public void DestroyHierarchy()
        {
            if (GlobalVolumeProfile != null)
            {
                Object.Destroy(GlobalVolumeProfile);
                GlobalVolumeProfile = null;
            }
            if (_ownerRoot != null)
            {
                Object.Destroy(_ownerRoot);
                _ownerRoot = null;
            }
            Stage = Characters = Lights = Cameras = Volumes = null;
            DefaultCamera = null;
        }

        private Transform NewChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_ownerRoot!.transform, worldPositionStays: false);
            return go.transform;
        }
    }
}
