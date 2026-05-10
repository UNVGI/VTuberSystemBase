#nullable enable
using SceneViewStyleCameraController;
using UnityEngine;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Thin wrapper around the third-party
    /// <see cref="SceneViewStyleCameraController.SceneViewStyleCameraController"/>
    /// component. Exposes <see cref="Enable"/> / <see cref="Disable"/> so the
    /// Coordinator can release mouse capture and rotate / pan / zoom input on
    /// tab deactivation (Requirement 2.7).
    /// </summary>
    public sealed class SceneViewStyleCameraControllerWrapper
    {
        private readonly SceneViewStyleCameraController.SceneViewStyleCameraController _controller;

        public SceneViewStyleCameraControllerWrapper(SceneViewStyleCameraController.SceneViewStyleCameraController controller)
        {
            _controller = controller != null ? controller : throw new System.ArgumentNullException(nameof(controller));
        }

        public bool IsEnabled => _controller.enabled;

        public void Enable()
        {
            if (_controller != null) _controller.enabled = true;
        }

        public void Disable()
        {
            if (_controller != null) _controller.enabled = false;
        }
    }
}
