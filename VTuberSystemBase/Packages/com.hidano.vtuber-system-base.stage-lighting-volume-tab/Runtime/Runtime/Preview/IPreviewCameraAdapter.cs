#nullable enable
using System;

namespace VTuberSystemBase.StageLightingVolumeTab.Preview
{
    /// <summary>
    /// Abstraction over the preview camera operations the UI needs to drive (e.g. the
    /// "reset view" button). Mouse orbit / pan / zoom is handled by
    /// <c>SceneViewStyleCameraController</c> attached to the preview camera in the
    /// main-output scene; this adapter only exposes UI-driven explicit commands.
    /// See design.md §Preview §IPreviewCameraAdapter (Requirements 2.4, 2.8, 2.10).
    /// </summary>
    public interface IPreviewCameraAdapter
    {
        /// <summary>
        /// Resets the preview camera back to the default view (matches the
        /// SceneViewStyleCameraController initial state). No-op if
        /// <see cref="IsAvailable"/> is false.
        /// </summary>
        void ResetToDefaultView();

        /// <summary>
        /// True once the underlying camera (and its controller) is reachable through
        /// <c>StagePreviewHostLocator.Current</c>. Flips back to false on host
        /// teardown.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Raised whenever <see cref="IsAvailable"/> changes. Subscribers MUST handle
        /// both directions (true→false and false→true) without leaking handlers.
        /// </summary>
        event Action? OnAvailabilityChanged;
    }
}
