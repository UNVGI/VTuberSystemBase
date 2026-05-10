#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Domain
{
    /// <summary>
    /// Publishes the <c>cameras/list</c> / <c>cameras/active</c> /
    /// <c>camera/created</c> / per-camera <c>camera/{id}/volume/enabled</c> state
    /// and event payloads on the IPC bus (Requirement 4 / 6.7 / 8.1〜8.5).
    /// </summary>
    public sealed class CamerasListPublisher
    {
        private readonly ICoreIpcBus _bus;
        private readonly ICameraSwitcherOutputAdapterClock _clock;

        public CamerasListPublisher(ICoreIpcBus bus, ICameraSwitcherOutputAdapterClock clock)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public void PublishCamerasList(IReadOnlyList<CameraEntry> entries)
        {
            var rows = new CameraListEntry[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                rows[i] = ToListEntry(entries[i]);
            }
            _bus.PublishState(CameraIpcTopics.CamerasList, new CamerasListPayload
            {
                Cameras = rows,
                UpdatedAtUnixMs = _clock.UnixMillisecondsNow(),
            });
        }

        public void PublishCamerasActive(CameraId? active)
        {
            _bus.PublishState(CameraIpcTopics.CamerasActive, new CamerasActiveStatePayload
            {
                ActiveCameraId = active.HasValue ? active.Value.Value : null,
                UpdatedAtUnixMs = _clock.UnixMillisecondsNow(),
            });
        }

        public void PublishCameraCreated(string clientRequestId, CameraEntry entry)
        {
            _bus.PublishEvent(CameraIpcTopics.CameraCreated, new CameraCreatedEventPayload
            {
                ClientRequestId = clientRequestId,
                CameraId = entry.CameraId.Value,
                Metadata = ToListEntry(entry),
            });
        }

        public void PublishVolumeEnabledForAll(IReadOnlyList<CameraEntry> entries, CameraId? active)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var enabled = active.HasValue && entry.CameraId.Equals(active.Value);
                _bus.PublishState(CameraIpcTopics.VolumeEnabled(entry.CameraId), new VolumeEnabledStatePayload
                {
                    Enabled = enabled,
                });
            }
        }

        private static CameraListEntry ToListEntry(CameraEntry entry) => new CameraListEntry
        {
            CameraId = entry.CameraId.Value,
            DisplayName = entry.DisplayName,
            Type = CameraTypeNames.ToWire(entry.Type) ?? CameraTypeNames.Perspective,
            DefaultTransform = entry.DefaultTransform,
        };
    }
}
