#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Domain
{
    public enum FailureKind
    {
        OscDecodeFailed = 0,
        OscStartupFailed = 1,
        UnknownCameraIdOnOsc = 2,
        UnknownCameraIdOnIpc = 3,
        VolumeBindFailed = 4,
        ReflectionFailed = 5,
        IpcSendFailed = 6,
        InvalidCommand = 7,
    }

    /// <summary>
    /// Collects every adapter-side failure into per-kind counters and a bounded
    /// recent-history buffer. User-actionable failures are forwarded to the IPC
    /// bus as <see cref="CameraIpcTopics.CameraError"/> events; OSC decode
    /// failures are logged only (Requirement 2.4 / 12.2 — UI noise prevention).
    /// </summary>
    public sealed class FailureAggregator
    {
        public const int RecentHistoryLimit = 20;

        private readonly ICoreIpcBus? _bus;
        private readonly int[] _counts = new int[Enum.GetValues(typeof(FailureKind)).Length];
        private readonly Queue<FailureRecord> _recent = new Queue<FailureRecord>();

        public FailureAggregator(ICoreIpcBus? bus = null)
        {
            _bus = bus;
        }

        public IReadOnlyList<FailureRecord> RecentHistory => _recent.ToArray();

        public int CountOf(FailureKind kind) => _counts[(int)kind];

        public int CameraErrorPublishCount { get; private set; }

        public void RecordOscDecodeFailure(string cameraId, Exception? ex)
        {
            Increment(FailureKind.OscDecodeFailed, cameraId, ex?.Message ?? "");
            Debug.Log($"[CameraSwitcherOutputAdapter] OSC decode failed cameraId={cameraId} ex={ex?.GetType().Name}");
            // No camera/error event (audio-of-noise prevention, Req 2.4).
        }

        public void RecordOscStartupFailure(string detail)
        {
            Increment(FailureKind.OscStartupFailed, null, detail);
            PublishCameraError(new CameraErrorEventPayload
            {
                ClientRequestId = null,
                CameraId = null,
                Op = "osc-start",
                Reason = "OscStartupFailed",
                Detail = detail,
            });
        }

        public void RecordUnknownCameraIdOnOsc(string cameraId)
        {
            Increment(FailureKind.UnknownCameraIdOnOsc, cameraId, null);
            // Log only (Requirement 2.2). No camera/error event.
        }

        public void RecordUnknownCameraIdOnIpc(string op, string cameraId, string? clientRequestId = null)
        {
            Increment(FailureKind.UnknownCameraIdOnIpc, cameraId, op);
            PublishCameraError(new CameraErrorEventPayload
            {
                ClientRequestId = clientRequestId,
                CameraId = cameraId,
                Op = op,
                Reason = CameraErrorReasons.UnknownCameraId,
                Detail = null,
            });
        }

        public void RecordCameraOperationFailure(
            string op,
            string? cameraId,
            string reason,
            string? detail,
            string? clientRequestId = null)
        {
            Increment(FailureKind.InvalidCommand, cameraId, $"{op}: {reason}");
            PublishCameraError(new CameraErrorEventPayload
            {
                ClientRequestId = clientRequestId,
                CameraId = cameraId,
                Op = op,
                Reason = reason,
                Detail = detail,
            });
        }

        public void RecordVolumeBindFailed(
            string op,
            string cameraId,
            string overrideType,
            string reason,
            string? detail,
            string? clientRequestId = null)
        {
            Increment(FailureKind.VolumeBindFailed, cameraId, $"{overrideType}: {reason}");
            PublishCameraError(new CameraErrorEventPayload
            {
                ClientRequestId = clientRequestId,
                CameraId = cameraId,
                Op = op,
                Reason = "VolumeBindFailed",
                Detail = $"{overrideType}: {detail ?? reason}",
            });
        }

        public void RecordReflectionFailed(string context, Exception? ex)
        {
            Increment(FailureKind.ReflectionFailed, null, $"{context}: {ex?.Message ?? ""}");
            Debug.Log($"[CameraSwitcherOutputAdapter] Reflection failed at {context}: {ex?.Message}");
        }

        public void RecordIpcSendFailed(string topic, string? detail)
        {
            Increment(FailureKind.IpcSendFailed, null, $"{topic}: {detail}");
            Debug.Log($"[CameraSwitcherOutputAdapter] IPC publish failed topic={topic} detail={detail}");
        }

        public Snapshot GetSnapshot() => new Snapshot
        {
            OscDecodeFailedCount = CountOf(FailureKind.OscDecodeFailed),
            OscStartupFailedCount = CountOf(FailureKind.OscStartupFailed),
            UnknownCameraIdOnOscCount = CountOf(FailureKind.UnknownCameraIdOnOsc),
            UnknownCameraIdOnIpcCount = CountOf(FailureKind.UnknownCameraIdOnIpc),
            VolumeBindFailedCount = CountOf(FailureKind.VolumeBindFailed),
            ReflectionFailedCount = CountOf(FailureKind.ReflectionFailed),
            IpcSendFailedCount = CountOf(FailureKind.IpcSendFailed),
            InvalidCommandCount = CountOf(FailureKind.InvalidCommand),
            CameraErrorPublishCount = CameraErrorPublishCount,
            RecentHistory = RecentHistory,
        };

        private void Increment(FailureKind kind, string? cameraId, string? detail)
        {
            _counts[(int)kind]++;
            _recent.Enqueue(new FailureRecord(kind, cameraId, detail));
            while (_recent.Count > RecentHistoryLimit) _recent.Dequeue();
        }

        private void PublishCameraError(CameraErrorEventPayload payload)
        {
            CameraErrorPublishCount++;
            if (_bus == null) return;
            try
            {
                _bus.PublishEvent(CameraIpcTopics.CameraError, payload);
            }
            catch (Exception ex)
            {
                _counts[(int)FailureKind.IpcSendFailed]++;
                Debug.Log($"[CameraSwitcherOutputAdapter] camera/error publish threw: {ex.Message}");
            }
        }

        public readonly struct FailureRecord
        {
            public FailureRecord(FailureKind kind, string? cameraId, string? detail)
            {
                Kind = kind;
                CameraId = cameraId;
                Detail = detail;
            }

            public FailureKind Kind { get; }
            public string? CameraId { get; }
            public string? Detail { get; }
        }

        public readonly struct Snapshot
        {
            public int OscDecodeFailedCount { get; init; }
            public int OscStartupFailedCount { get; init; }
            public int UnknownCameraIdOnOscCount { get; init; }
            public int UnknownCameraIdOnIpcCount { get; init; }
            public int VolumeBindFailedCount { get; init; }
            public int ReflectionFailedCount { get; init; }
            public int IpcSendFailedCount { get; init; }
            public int InvalidCommandCount { get; init; }
            public int CameraErrorPublishCount { get; init; }
            public IReadOnlyList<FailureRecord> RecentHistory { get; init; }
        }
    }
}
