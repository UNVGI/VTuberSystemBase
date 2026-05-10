#nullable enable
using System;
using VTuberSystemBase.StageLightingVolumeTab.Preview;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IPreviewCameraAdapter"/> double. Counts ResetView calls and
    /// lets tests flip <see cref="IsAvailable"/> to drive
    /// <see cref="OnAvailabilityChanged"/>. (Task 1.2, Requirement 12.7)
    /// </summary>
    public sealed class FakePreviewCameraAdapter : IPreviewCameraAdapter
    {
        private bool _isAvailable;

        public int ResetCount { get; private set; }

        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (_isAvailable == value) return;
                _isAvailable = value;
                OnAvailabilityChanged?.Invoke();
            }
        }

        public event Action? OnAvailabilityChanged;

        public void ResetToDefaultView()
        {
            ResetCount++;
        }
    }
}
