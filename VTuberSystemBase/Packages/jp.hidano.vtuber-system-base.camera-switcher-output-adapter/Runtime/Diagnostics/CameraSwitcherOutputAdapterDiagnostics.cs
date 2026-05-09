#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Domain;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime.Diagnostics
{
    /// <summary>
    /// Aggregates diagnostic state from the adapter, registry, OSC host and
    /// failure aggregator into a single snapshot (Requirement 14.x).
    /// </summary>
    public sealed class CameraSwitcherOutputAdapterDiagnostics
    {
        private readonly CameraSwitcherOutputAdapter _adapter;
        private readonly IOscReceiverHost _oscHost;
        private readonly IpcHandlerRegistration _registration;

        public CameraSwitcherOutputAdapterDiagnostics(
            CameraSwitcherOutputAdapter adapter,
            IOscReceiverHost oscHost,
            IpcHandlerRegistration registration)
        {
            _adapter = adapter;
            _oscHost = oscHost;
            _registration = registration;
        }

        public Snapshot GetSnapshot()
        {
            var failureSnapshot = _adapter.Failures.GetSnapshot();
            var camerasIds = new List<string>();
            foreach (var entry in _adapter.Registry.Enumerate())
            {
                camerasIds.Add(entry.CameraId.Value);
            }
            return new Snapshot
            {
                AdapterStatus = _adapter.Status,
                CameraCount = _adapter.CameraCount,
                ActiveCameraId = _adapter.ActiveCameraId.HasValue ? _adapter.ActiveCameraId.Value.Value : null,
                Cameras = camerasIds,
                OscReceiverStatus = _oscHost.Status,
                IpcStaticHandlerCount = _registration.RegisteredHandlerCount,
                Failures = failureSnapshot,
            };
        }

        public readonly struct Snapshot
        {
            public AdapterStatus AdapterStatus { get; init; }
            public int CameraCount { get; init; }
            public string? ActiveCameraId { get; init; }
            public IReadOnlyList<string> Cameras { get; init; }
            public OscReceiverHostStatus OscReceiverStatus { get; init; }
            public int IpcStaticHandlerCount { get; init; }
            public FailureAggregator.Snapshot Failures { get; init; }
        }
    }
}
