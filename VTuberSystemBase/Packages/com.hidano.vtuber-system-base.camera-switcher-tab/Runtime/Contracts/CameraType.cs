namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Camera projection mode discriminator carried as a string on the wire
    /// (see <see cref="CameraTypeNames"/>) and parsed on read.
    /// </summary>
    /// <remarks>
    /// Exists as a typed value alongside the wire string so the UI can switch on a
    /// closed enum without scattering literal comparisons. Forward compatibility:
    /// receivers MUST treat unknown wire values as <see cref="Unknown"/> and skip + log
    /// instead of failing hard (consistent with the schema-versioning policy in
    /// design.md L1372-L1380).
    /// </remarks>
    public enum CameraType
    {
        Unknown = 0,
        Perspective = 1,
        Orthographic = 2
    }

    /// <summary>
    /// Wire-format string constants for <see cref="CameraType"/>. Matches the values
    /// carried in <c>camera/command.type</c> and the Camera list <c>type</c> field
    /// (design.md L1265, L1319).
    /// </summary>
    public static class CameraTypeNames
    {
        public const string Perspective = "Perspective";
        public const string Orthographic = "Orthographic";

        /// <summary>
        /// Parses a wire string to <see cref="CameraType"/>. Returns
        /// <see cref="CameraType.Unknown"/> for null / empty / unrecognised values.
        /// </summary>
        public static CameraType Parse(string? value)
        {
            return value switch
            {
                Perspective => CameraType.Perspective,
                Orthographic => CameraType.Orthographic,
                _ => CameraType.Unknown
            };
        }

        /// <summary>
        /// Returns the wire string for <paramref name="type"/>, or <c>null</c> for
        /// <see cref="CameraType.Unknown"/> (UNKNOWN is not a transmittable value).
        /// </summary>
        public static string? ToWire(CameraType type)
        {
            return type switch
            {
                CameraType.Perspective => Perspective,
                CameraType.Orthographic => Orthographic,
                _ => null
            };
        }
    }
}
