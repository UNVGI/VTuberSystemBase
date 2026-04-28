#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VTuberSystemBase.UiToolkitShell.Diagnostics
{
    /// <summary>
    /// Default <see cref="IDiagnosticsLogger"/> implementation. Routes accepted entries into the
    /// Unity Console (<see cref="Debug.Log"/> / <see cref="Debug.LogWarning"/> /
    /// <see cref="Debug.LogError"/>) and into an in-memory ring buffer that the UI-side
    /// diagnostics surface (notification bar / diagnostics panel) reads from. The class never
    /// references a <c>UIDocument</c> targeting Display 2+, so by construction no entry can
    /// reach the main output surface (Requirement 11.7).
    /// </summary>
    public sealed class DiagnosticsLogger : IDiagnosticsLogger
    {
        public const int DefaultRingBufferCapacity = 256;

        private readonly object _gate = new object();
        private readonly Queue<DiagnosticsLogEntry> _ringBuffer;
        private readonly int _ringBufferCapacity;

        public DiagnosticsLogger() : this(DefaultRingBufferCapacity) { }

        public DiagnosticsLogger(int ringBufferCapacity)
        {
            if (ringBufferCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(ringBufferCapacity));
            _ringBufferCapacity = ringBufferCapacity;
            _ringBuffer = new Queue<DiagnosticsLogEntry>(ringBufferCapacity);
            MinimumLevel = LogLevel.Info;
        }

        public LogLevel MinimumLevel { get; set; }

        public void Log(LogLevel level, LogCategory category, string message, object? context = null)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            if (level < MinimumLevel) return;

            var formatted = $"[{level}][{category}] {message}";

            lock (_gate)
            {
                if (_ringBuffer.Count >= _ringBufferCapacity)
                {
                    _ringBuffer.Dequeue();
                }
                _ringBuffer.Enqueue(new DiagnosticsLogEntry(level, category, message, context));
            }

            switch (level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(formatted);
                    break;
                case LogLevel.Error:
                    Debug.LogError(formatted);
                    break;
                default:
                    Debug.Log(formatted);
                    break;
            }
        }

        /// <summary>
        /// Returns a snapshot of the in-memory ring buffer in arrival order. Used by the UI-side
        /// diagnostics panel and by tests verifying that emitted entries are retained.
        /// </summary>
        public DiagnosticsLogEntry[] SnapshotRecentEntries()
        {
            lock (_gate)
            {
                return _ringBuffer.ToArray();
            }
        }
    }

    public readonly struct DiagnosticsLogEntry
    {
        public DiagnosticsLogEntry(LogLevel level, LogCategory category, string message, object? context)
        {
            Level = level;
            Category = category;
            Message = message;
            Context = context;
        }

        public LogLevel Level { get; }

        public LogCategory Category { get; }

        public string Message { get; }

        public object? Context { get; }
    }
}
