#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// One entry in the adapter's Camera Registry. Holds the runtime references the
    /// adapter needs to apply OSC frames, propagate IPC metadata, and toggle active
    /// state and Local Volume in lockstep.
    /// </summary>
    /// <remarks>
    /// <see cref="GameObject"/> / <see cref="CameraComponent"/> / <see cref="LocalVolume"/>
    /// are <em>not</em> guaranteed non-null by the type system: tests substitute a
    /// <c>FakeOutputSceneRoots</c> that may omit them. Production code paths build
    /// entries through <c>CameraGameObjectFactory</c> which always populates them.
    /// </remarks>
    public sealed class CameraEntry
    {
        public CameraEntry(
            CameraId cameraId,
            string displayName,
            CameraType type,
            CameraDefaultTransform defaultTransform,
            int allocOrder,
            GameObject? gameObject,
            Camera? cameraComponent,
            Volume? localVolume)
        {
            CameraId = cameraId;
            DisplayName = displayName ?? string.Empty;
            Type = type;
            DefaultTransform = defaultTransform;
            AllocOrder = allocOrder;
            GameObject = gameObject;
            CameraComponent = cameraComponent;
            LocalVolume = localVolume;
        }

        public CameraId CameraId { get; }
        public string DisplayName { get; set; }
        public CameraType Type { get; set; }
        public CameraDefaultTransform DefaultTransform { get; set; }
        public int AllocOrder { get; }

        public GameObject? GameObject { get; }
        public Camera? CameraComponent { get; }
        public Volume? LocalVolume { get; }
    }
}
