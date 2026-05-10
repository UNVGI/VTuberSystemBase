#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Utilities
{
    /// <summary>
    /// Convenience builders for the camera-switcher Contracts payload structs used in
    /// adapter tests.
    /// </summary>
    public static class PayloadFactory
    {
        public static CameraDefaultTransform DefaultTransform(
            float[]? position = null,
            float[]? rotation = null,
            float focalLengthMm = 50f) => new CameraDefaultTransform
        {
            Position = position ?? new[] { 0f, 1.5f, -3f },
            Rotation = rotation ?? new[] { 0f, 0f, 0f, 1f },
            FocalLengthMm = focalLengthMm,
        };

        public static CameraCommandPayload AddCommand(string clientRequestId, string type = "Perspective", string? displayName = null)
            => new CameraCommandPayload
            {
                Op = CameraCommandOps.Add,
                ClientRequestId = clientRequestId,
                CameraId = null,
                Type = type,
                DisplayName = displayName,
            };

        public static CameraCommandPayload DeleteCommand(string clientRequestId, string cameraId)
            => new CameraCommandPayload
            {
                Op = CameraCommandOps.Delete,
                ClientRequestId = clientRequestId,
                CameraId = cameraId,
                Type = null,
                DisplayName = null,
            };

        public static CameraCommandPayload ActiveSetCommand(string clientRequestId, string? cameraId)
            => new CameraCommandPayload
            {
                Op = CameraCommandOps.ActiveSet,
                ClientRequestId = clientRequestId,
                CameraId = cameraId,
                Type = null,
                DisplayName = null,
            };

        public static VolumeCommandPayload OverrideAdd(string overrideType)
            => new VolumeCommandPayload { Op = VolumeCommandOps.OverrideAdd, OverrideType = overrideType };

        public static VolumeCommandPayload OverrideRemove(string overrideType)
            => new VolumeCommandPayload { Op = VolumeCommandOps.OverrideRemove, OverrideType = overrideType };

        public static PreviewCommandPayload PreviewAttach(IReadOnlyList<string> cameraIds, int width = 192, int height = 108, int fps = 30)
            => new PreviewCommandPayload
            {
                Op = PreviewCommandOps.Attach,
                CameraIds = cameraIds,
                Size = new[] { width, height },
                Fps = fps,
            };

        public static PreviewCommandPayload PreviewDetach(IReadOnlyList<string> cameraIds)
            => new PreviewCommandPayload
            {
                Op = PreviewCommandOps.Detach,
                CameraIds = cameraIds,
                Size = null,
                Fps = null,
            };

        public static CameraListEntry ListEntry(string cameraId, string displayName = "Cam", string type = "Perspective", CameraDefaultTransform? defaultTransform = null)
            => new CameraListEntry
            {
                CameraId = cameraId,
                DisplayName = displayName,
                Type = type,
                DefaultTransform = defaultTransform ?? DefaultTransform(),
            };
    }
}
