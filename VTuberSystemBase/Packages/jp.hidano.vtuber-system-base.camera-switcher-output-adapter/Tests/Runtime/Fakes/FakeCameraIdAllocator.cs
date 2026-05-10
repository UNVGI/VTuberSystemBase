#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// Test double for <see cref="ICameraIdAllocator"/> with either a fixed value or
    /// a pre-seeded sequence (FIFO consumption).
    /// </summary>
    public sealed class FakeCameraIdAllocator : ICameraIdAllocator
    {
        private readonly Queue<CameraId> _sequence = new();
        private CameraId? _fallback;

        public int AllocateCallCount { get; private set; }

        public FakeCameraIdAllocator WithSequence(params string[] cameraIds)
        {
            foreach (var id in cameraIds) _sequence.Enqueue(new CameraId(id));
            return this;
        }

        public FakeCameraIdAllocator WithFallback(string cameraId)
        {
            _fallback = new CameraId(cameraId);
            return this;
        }

        public CameraId Allocate()
        {
            AllocateCallCount++;
            if (_sequence.Count > 0) return _sequence.Dequeue();
            if (_fallback.HasValue) return _fallback.Value;
            return new CameraId($"cam-{AllocateCallCount:D4}");
        }
    }
}
