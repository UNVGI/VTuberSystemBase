#nullable enable
using System;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Tracks the two independent camera-id concepts the tab exposes: the
    /// <see cref="ActiveCameraId"/> (server-authoritative, mirrors
    /// <c>cameras/active</c>) and the <see cref="EditingCameraId"/> (UI-local,
    /// the camera the operator is currently driving with mouse input).
    /// </summary>
    /// <remarks>
    /// active and editing are decoupled per Requirement 2.9 / 7.7: switching
    /// the editing target does NOT auto-broadcast a new active camera, and
    /// receiving a new active camera does NOT change which camera the operator
    /// is editing. <see cref="OnActiveChanged"/> / <see cref="OnEditingChanged"/>
    /// fire only on actual transitions (no spurious events for redundant sets).
    /// </remarks>
    public sealed class ActiveCameraTracker
    {
        private CameraId _active;
        private CameraId _editing;

        public CameraId ActiveCameraId => _active;
        public CameraId EditingCameraId => _editing;

        public event Action<CameraId>? OnActiveChanged;
        public event Action<CameraId>? OnEditingChanged;

        /// <summary>Apply a server-authoritative active update. <paramref name="next"/> may be unset (null transition).</summary>
        public void SetActive(CameraId next)
        {
            if (CameraIdEquals(_active, next)) return;
            _active = next;
            OnActiveChanged?.Invoke(_active);
        }

        /// <summary>Apply a UI-local editing target. <paramref name="next"/> may be unset.</summary>
        public void SetEditing(CameraId next)
        {
            if (CameraIdEquals(_editing, next)) return;
            _editing = next;
            OnEditingChanged?.Invoke(_editing);
        }

        /// <summary>True when active and editing point at different cameras (or one of them is unset).</summary>
        public bool ActiveAndEditingDiverge
        {
            get
            {
                return !CameraIdEquals(_active, _editing);
            }
        }

        private static bool CameraIdEquals(CameraId a, CameraId b)
        {
            if (a.HasValue && b.HasValue) return string.Equals(a.Value, b.Value, StringComparison.Ordinal);
            return a.HasValue == b.HasValue;
        }
    }
}
