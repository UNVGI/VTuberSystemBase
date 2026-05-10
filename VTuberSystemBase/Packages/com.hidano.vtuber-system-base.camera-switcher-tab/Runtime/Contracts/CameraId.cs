using System;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Strongly-typed identifier for a render-output camera. Wraps a non-empty string
    /// allocated by the main-output side (採番側). The UI never invents a CameraId on
    /// its own; it only echoes values it received from <c>cameras/list</c> /
    /// <c>camera/created</c> back to the main-output side.
    /// </summary>
    /// <remarks>
    /// The wrapped string is constrained to the same character class as
    /// <see cref="OscAddressBuilder"/> accepts (ASCII alphanumerics + <c>-</c> + <c>_</c>),
    /// so a CameraId may be embedded directly in OSC addresses
    /// (<c>/ucapi/camera/{cameraId}/flat</c>, design.md L1287) and IPC topics
    /// (<c>camera/{cameraId}/...</c>, design.md L1270-L1281) without further escaping.
    /// </remarks>
    public readonly record struct CameraId
    {
        /// <summary>
        /// The underlying string value. Guaranteed non-null and non-empty when constructed
        /// via <see cref="CameraId(string)"/>; the <c>default</c> instance carries an empty
        /// value and should be treated as "unset".
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Creates a new <see cref="CameraId"/>.
        /// </summary>
        /// <param name="value">The non-empty identifier string.</param>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> is empty or contains characters outside
        /// <c>[A-Za-z0-9_-]</c>.
        /// </exception>
        public CameraId(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length == 0) throw new ArgumentException("CameraId must not be empty.", nameof(value));
            for (var i = 0; i < value.Length; i++)
            {
                if (!IsAllowedChar(value[i]))
                {
                    throw new ArgumentException(
                        $"CameraId may contain only ASCII alphanumerics, '-', and '_'. Got: '{value}'",
                        nameof(value));
                }
            }
            Value = value;
        }

        /// <summary>
        /// Returns <c>true</c> if this instance was constructed with a real value
        /// (i.e. is not the <c>default</c> "unset" instance).
        /// </summary>
        public bool HasValue => !string.IsNullOrEmpty(Value);

        /// <inheritdoc />
        public override string ToString() => Value ?? string.Empty;

        /// <summary>
        /// Tries to create a <see cref="CameraId"/> from <paramref name="value"/>.
        /// Returns <c>false</c> instead of throwing if the input is invalid (useful when
        /// validating untrusted IPC payloads).
        /// </summary>
        public static bool TryCreate(string? value, out CameraId result)
        {
            if (string.IsNullOrEmpty(value))
            {
                result = default;
                return false;
            }
            for (var i = 0; i < value!.Length; i++)
            {
                if (!IsAllowedChar(value[i]))
                {
                    result = default;
                    return false;
                }
            }
            result = new CameraId(value);
            return true;
        }

        private static bool IsAllowedChar(char c)
        {
            return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_';
        }
    }
}
