#nullable enable

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Internal model for one camera's metadata mirrored from
    /// <see cref="CameraListEntry"/>. Held by the <c>CameraRegistry</c> so the UI
    /// can render the lineup without re-querying the wire payload.
    /// </summary>
    public sealed class CameraMetadata
    {
        public CameraId Id { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public CameraType Type { get; init; }
        public CameraDefaultTransform DefaultTransform { get; init; }

        public static CameraMetadata FromListEntry(in CameraListEntry entry)
        {
            return new CameraMetadata
            {
                Id = CameraId.TryCreate(entry.CameraId, out var id) ? id : default,
                DisplayName = entry.DisplayName ?? string.Empty,
                Type = CameraTypeNames.Parse(entry.Type),
                DefaultTransform = entry.DefaultTransform,
            };
        }
    }
}
