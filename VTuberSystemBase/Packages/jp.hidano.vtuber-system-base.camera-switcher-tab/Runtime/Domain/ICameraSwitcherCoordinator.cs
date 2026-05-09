#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// State-machine facade for the camera switcher tab. Owns the editing /
    /// active camera ids, dispatches every UI-side command, and surfaces an
    /// observable state for the view layer (Requirement 6.x / 7.x / 8.x).
    /// </summary>
    /// <remarks>
    /// All <c>Request*</c> / <c>Set*</c> methods accept input synchronously and
    /// dispatch the underlying IPC sends asynchronously; they MUST NOT throw.
    /// Failures are reported via <see cref="OnStateChanged"/> + the diagnostics
    /// aggregator.
    /// </remarks>
    public interface ICameraSwitcherCoordinator : IDisposable
    {
        TabStatus Status { get; }

        CameraId EditingCameraId { get; }

        CameraId ActiveCameraId { get; }

        IReadOnlyList<CameraMetadata> Cameras { get; }

        /// <summary>Raised on the Unity main thread after any observable state mutation.</summary>
        event Action OnStateChanged;

        // ---- Lifecycle ----

        void OnTabActivated();

        void OnTabDeactivated();

        /// <summary>Called once per LateUpdate while the tab is active.</summary>
        void FrameTick(in CameraSnapshot? editingCameraSnapshot);

        // ---- Camera CRUD ----

        void RequestAddCamera(CameraType type, string? displayName);

        void RequestDeleteCamera(CameraId cameraId);

        void ActivateCamera(CameraId cameraId);

        void SelectEditTarget(CameraId cameraId);

        void UpdateCameraMetadata(CameraId cameraId, string key, string value);

        // ---- Volume ----

        void AddVolumeOverride(CameraId cameraId, string overrideType);

        void RemoveVolumeOverride(CameraId cameraId, string overrideType);

        void SetVolumeOverrideEnabled(CameraId cameraId, string overrideType, bool enabled);

        void SetVolumeOverrideParam(CameraId cameraId, string overrideType, string param, System.Text.Json.JsonElement value);

        void SetVolumeEnabled(CameraId cameraId, bool enabled);

        // ---- Preset ----

        void CreatePreset(string name);

        void RenamePreset(string oldName, string newName);

        void DuplicatePreset(string sourceName, string newName);

        void DeletePreset(string name);

        void ActivatePreset(string name);
    }
}
