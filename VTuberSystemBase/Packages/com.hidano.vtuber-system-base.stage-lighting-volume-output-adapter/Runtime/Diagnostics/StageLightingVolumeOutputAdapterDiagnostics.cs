#nullable enable
using System.Threading;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics
{
    /// <summary>
    /// Read-only snapshot of adapter diagnostics. Plain immutable record-struct so it can be
    /// inspected from any thread without allocation.
    /// </summary>
    public readonly record struct DiagnosticsSnapshot(
        bool IsReady,
        int RegisteredHandlerCount,
        string? CurrentStageAddressableKey,
        int LightCount,
        int VolumeOverrideTypeCount,
        bool PreviewHostReady,
        string? LastErrorMessage,
        long LastErrorAtUnixMs);

    /// <summary>
    /// Mutable diagnostics state owned by the adapter Bootstrapper. Producers (handlers)
    /// update via internal setters; consumers call <see cref="Capture"/> for an atomic
    /// snapshot. Threading: primitives are read/written via volatile, reference values are
    /// guarded by an internal lock.
    /// </summary>
    public sealed class StageLightingVolumeOutputAdapterDiagnostics
    {
        private readonly object _lock = new object();
        private int _isReady;            // 0 / 1
        private int _registeredHandlerCount;
        private int _lightCount;
        private int _volumeOverrideTypeCount;
        private int _previewHostReady;   // 0 / 1
        private long _lastErrorAtUnixMs;
        private string? _currentStageAddressableKey;
        private string? _lastErrorMessage;

        public bool IsReady => Volatile.Read(ref _isReady) != 0;
        public int RegisteredHandlerCount => Volatile.Read(ref _registeredHandlerCount);
        public int LightCount => Volatile.Read(ref _lightCount);
        public int VolumeOverrideTypeCount => Volatile.Read(ref _volumeOverrideTypeCount);
        public bool PreviewHostReady => Volatile.Read(ref _previewHostReady) != 0;
        public long LastErrorAtUnixMs => Volatile.Read(ref _lastErrorAtUnixMs);

        public string? CurrentStageAddressableKey
        {
            get { lock (_lock) { return _currentStageAddressableKey; } }
        }

        public string? LastErrorMessage
        {
            get { lock (_lock) { return _lastErrorMessage; } }
        }

        internal void SetReady(bool ready) => Volatile.Write(ref _isReady, ready ? 1 : 0);
        internal void SetRegisteredHandlerCount(int count) => Volatile.Write(ref _registeredHandlerCount, count);
        internal void IncrementHandlerCount(int delta = 1) => Interlocked.Add(ref _registeredHandlerCount, delta);
        internal void SetLightCount(int count) => Volatile.Write(ref _lightCount, count);
        internal void SetVolumeOverrideTypeCount(int count) => Volatile.Write(ref _volumeOverrideTypeCount, count);
        internal void SetPreviewHostReady(bool ready) => Volatile.Write(ref _previewHostReady, ready ? 1 : 0);

        internal void SetCurrentStageAddressableKey(string? key)
        {
            lock (_lock) { _currentStageAddressableKey = key; }
        }

        internal void RecordError(string? message, long atUnixMs)
        {
            lock (_lock) { _lastErrorMessage = message; }
            Volatile.Write(ref _lastErrorAtUnixMs, atUnixMs);
        }

        public DiagnosticsSnapshot Capture()
        {
            string? stageKey;
            string? lastErr;
            lock (_lock)
            {
                stageKey = _currentStageAddressableKey;
                lastErr = _lastErrorMessage;
            }
            return new DiagnosticsSnapshot(
                IsReady: Volatile.Read(ref _isReady) != 0,
                RegisteredHandlerCount: Volatile.Read(ref _registeredHandlerCount),
                CurrentStageAddressableKey: stageKey,
                LightCount: Volatile.Read(ref _lightCount),
                VolumeOverrideTypeCount: Volatile.Read(ref _volumeOverrideTypeCount),
                PreviewHostReady: Volatile.Read(ref _previewHostReady) != 0,
                LastErrorMessage: lastErr,
                LastErrorAtUnixMs: Volatile.Read(ref _lastErrorAtUnixMs));
        }
    }
}
