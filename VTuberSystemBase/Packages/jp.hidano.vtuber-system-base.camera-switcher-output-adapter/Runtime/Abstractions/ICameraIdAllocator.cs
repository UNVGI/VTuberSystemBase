#nullable enable
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Allocates the next cameraId on behalf of the main-output-side adapter (CSW-5,
    /// CSO-6). The default implementation produces <c>cam-{NNNN}</c> values that
    /// satisfy <c>OscAddressBuilder.IsValidCameraIdSegment</c>; deletion does not
    /// reuse numbers.
    /// </summary>
    /// <remarks>
    /// Implementations are not required to be thread-safe — the adapter operates on
    /// the Unity main thread per Requirement 10.
    /// </remarks>
    public interface ICameraIdAllocator
    {
        /// <summary>
        /// Returns the next allocated cameraId. The returned <see cref="CameraId"/> is
        /// guaranteed to be non-default (<c>HasValue == true</c>) and to use only the
        /// character class allowed by <c>OscAddressBuilder</c>.
        /// </summary>
        CameraId Allocate();
    }
}
