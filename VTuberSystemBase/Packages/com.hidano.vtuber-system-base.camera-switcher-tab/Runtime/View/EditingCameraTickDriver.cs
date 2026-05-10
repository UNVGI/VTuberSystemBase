#nullable enable
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Adapters.Ucapi;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Domain;

using CameraType = VTuberSystemBase.CameraSwitcherTab.Contracts.CameraType;
namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// MonoBehaviour driver that pumps <see cref="ICameraSwitcherCoordinator.FrameTick"/>
    /// once per LateUpdate while the tab is active and PlayMode is running
    /// (Requirement 2.4 / 2.6 / 4.4 / 4.5).
    /// </summary>
    /// <remarks>
    /// Capture happens here (Camera → CameraSnapshot) and the snapshot is
    /// passed to the Coordinator which forwards it to
    /// <c>OscStreamController.FrameTick</c>. Use <see cref="Bind"/> to inject
    /// dependencies; the driver is dormant until that wiring completes.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class EditingCameraTickDriver : MonoBehaviour
    {
        private CameraSwitcherCoordinator? _coordinator;
        private System.Func<CameraId, Camera?>? _cameraResolver;
        private System.Func<bool>? _isTabActive;
        private uint _frameCounter;

        public void Bind(
            CameraSwitcherCoordinator coordinator,
            System.Func<CameraId, Camera?> cameraResolver,
            System.Func<bool> isTabActive)
        {
            _coordinator = coordinator;
            _cameraResolver = cameraResolver;
            _isTabActive = isTabActive;
        }

        public void Unbind()
        {
            _coordinator = null;
            _cameraResolver = null;
            _isTabActive = null;
        }

        private void LateUpdate()
        {
            if (_coordinator is null || _cameraResolver is null) return;
            if (!Application.isPlaying) return;
            if (_isTabActive is not null && !_isTabActive()) return;

            var editingId = _coordinator.EditingCameraId;
            CameraSnapshot? snapshot = null;
            if (editingId.HasValue)
            {
                var cam = _cameraResolver(editingId);
                if (cam != null)
                {
                    snapshot = UnityCameraSnapshotCapture.Capture(
                        cam, editingId, CameraType.Perspective, _frameCounter);
                }
            }
            unchecked { _frameCounter++; }
            _coordinator.FrameTick(snapshot);
        }
    }
}
