#nullable enable
using System.Globalization;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Allocator
{
    /// <summary>
    /// Default <see cref="ICameraIdAllocator"/>: emits a monotonic <c>cam-{NNNN}</c>
    /// counter starting at 1 (CSO-6). The width is 4 digits while the counter fits
    /// (i.e. up to <c>cam-9999</c>); past that the formatter naturally widens to
    /// 5+ digits without re-using deleted IDs (no truncation, no re-numbering).
    /// </summary>
    /// <remarks>
    /// Thread-safety is not required (Requirement 10 — main thread only). The
    /// allocator never decrements the counter on delete, so OSC packets that race
    /// a recreate cannot land on a different camera by accident.
    /// </remarks>
    public sealed class SequentialCameraIdAllocator : ICameraIdAllocator
    {
        private readonly string _prefix;
        private int _next;

        public SequentialCameraIdAllocator(string prefix = "cam-", int seed = 1)
        {
            if (string.IsNullOrEmpty(prefix)) prefix = "cam-";
            if (seed < 1) seed = 1;
            _prefix = prefix!;
            _next = seed;
        }

        /// <summary>The next counter value that will be emitted on the next Allocate call.</summary>
        public int NextCounter => _next;

        public CameraId Allocate()
        {
            var index = _next++;
            // Always at least 4 digits ("cam-0001"); higher counters widen naturally.
            var formatted = index >= 10000
                ? index.ToString(CultureInfo.InvariantCulture)
                : index.ToString("D4", CultureInfo.InvariantCulture);
            return new CameraId(_prefix + formatted);
        }
    }
}
