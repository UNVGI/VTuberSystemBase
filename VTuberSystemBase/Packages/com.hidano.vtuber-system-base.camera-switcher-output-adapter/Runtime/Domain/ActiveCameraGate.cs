#nullable enable
using System;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Domain
{
    /// <summary>
    /// State machine for the <c>cameras/active</c> selection. Walks the
    /// <see cref="CameraEntryRegistry"/> on each <see cref="SetActive"/> call and
    /// flips <c>Camera.enabled</c> + <c>Volume.enabled</c> per entry: the matching
    /// cameraId becomes <c>true</c>, every other entry becomes <c>false</c>
    /// (CSO-9, CSO-10, CSW-12).
    /// </summary>
    public sealed class ActiveCameraGate
    {
        private readonly CameraEntryRegistry _registry;
        private readonly Action<string>? _onUnknownCameraId;

        public ActiveCameraGate(
            CameraEntryRegistry registry,
            Action<string>? onUnknownCameraId = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _onUnknownCameraId = onUnknownCameraId;
        }

        public CameraId? Active { get; private set; }

        /// <summary>Sets the active camera. Pass <c>null</c> to clear.</summary>
        public void SetActive(CameraId? target)
        {
            if (!target.HasValue || !target.Value.HasValue)
            {
                DisableAll();
                Active = null;
                return;
            }

            if (!_registry.Contains(target.Value))
            {
                _onUnknownCameraId?.Invoke(target.Value.Value);
                return; // Active state unchanged.
            }

            foreach (var entry in _registry.Enumerate())
            {
                var match = entry.CameraId.Equals(target.Value);
                if (entry.CameraComponent != null) entry.CameraComponent.enabled = match;
                if (entry.LocalVolume != null) entry.LocalVolume.enabled = match;
            }
            Active = target.Value;
        }

        /// <summary>
        /// Notifies the gate that <paramref name="removed"/> has been deleted from
        /// the registry. Clears <see cref="Active"/> when the removed camera was
        /// the active one.
        /// </summary>
        public void OnCameraRemoved(CameraId removed)
        {
            if (Active.HasValue && Active.Value.Equals(removed))
            {
                Active = null;
            }
        }

        private void DisableAll()
        {
            foreach (var entry in _registry.Enumerate())
            {
                if (entry.CameraComponent != null) entry.CameraComponent.enabled = false;
                if (entry.LocalVolume != null) entry.LocalVolume.enabled = false;
            }
        }
    }
}
