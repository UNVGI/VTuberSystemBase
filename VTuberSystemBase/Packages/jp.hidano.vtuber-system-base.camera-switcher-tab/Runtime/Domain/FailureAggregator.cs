#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Coarse-grained classification used by <see cref="FailureAggregator"/>.
    /// Mirrors the failure surfaces produced by <c>OscStreamController</c>,
    /// <c>VolumeUiStateManager</c>, <c>PresetController</c> and the IPC layer.
    /// </summary>
    public enum FailureKind
    {
        OscFailure = 0,
        IpcSendFailure,
        VolumeMetadataFailure,
        PresetIoFailure,
        CameraError,
    }

    /// <summary>One recorded failure entry kept by <see cref="FailureAggregator"/>.</summary>
    public readonly struct FailureRecord
    {
        public FailureRecord(FailureKind kind, string message, DateTimeOffset at, object? context = null)
        {
            Kind = kind;
            Message = message;
            At = at;
            Context = context;
        }

        public FailureKind Kind { get; }
        public string Message { get; }
        public DateTimeOffset At { get; }
        public object? Context { get; }
    }

    /// <summary>
    /// Tab-wide failure registry. Caches per-kind counts and a bounded ring of
    /// the most recent records so the diagnostics surface (Requirement 14.5 /
    /// 14.9) can render without re-sweeping the IPC log.
    /// </summary>
    /// <remarks>
    /// Single-threaded by design; the Coordinator drives every report from the
    /// Unity main thread (D-3). Subscribers attached via
    /// <see cref="OnFailureRecorded"/> MUST NOT throw; callers swallow and log
    /// any exception via the diagnostics logger.
    /// </remarks>
    public sealed class FailureAggregator
    {
        private const int DefaultHistorySize = 64;

        private readonly Dictionary<FailureKind, int> _counts = new Dictionary<FailureKind, int>();
        private readonly Queue<FailureRecord> _history;
        private readonly int _historySize;

        public event Action<FailureRecord>? OnFailureRecorded;

        public FailureAggregator(int historySize = DefaultHistorySize)
        {
            if (historySize <= 0) throw new ArgumentOutOfRangeException(nameof(historySize));
            _historySize = historySize;
            _history = new Queue<FailureRecord>(historySize);
        }

        public void Record(FailureKind kind, string message, DateTimeOffset at, object? context = null)
        {
            _counts.TryGetValue(kind, out var n);
            _counts[kind] = n + 1;
            var rec = new FailureRecord(kind, message ?? string.Empty, at, context);
            if (_history.Count >= _historySize) _history.Dequeue();
            _history.Enqueue(rec);
            OnFailureRecorded?.Invoke(rec);
        }

        public int CountOf(FailureKind kind)
        {
            return _counts.TryGetValue(kind, out var n) ? n : 0;
        }

        public IReadOnlyDictionary<FailureKind, int> Snapshot()
        {
            var copy = new Dictionary<FailureKind, int>(_counts.Count);
            foreach (var kv in _counts) copy[kv.Key] = kv.Value;
            return copy;
        }

        public IReadOnlyList<FailureRecord> RecentRecords()
        {
            return _history.ToArray();
        }

        public int TotalCount => _counts.Values.Sum();

        public void Clear()
        {
            _counts.Clear();
            _history.Clear();
        }
    }
}
