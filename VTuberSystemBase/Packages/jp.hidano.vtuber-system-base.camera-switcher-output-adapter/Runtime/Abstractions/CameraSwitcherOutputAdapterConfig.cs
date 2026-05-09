#nullable enable
using UnityEngine;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Composition-Root-time configuration for the camera-switcher output adapter.
    /// Authored as a <see cref="ScriptableObject"/> so utilising projects can override
    /// the defaults (OSC host / port, default Camera transform, max camera cap)
    /// without code changes.
    /// </summary>
    /// <remarks>
    /// Default values follow CSO-2 (host=<c>127.0.0.1</c>, port=<c>9000</c>) and
    /// CSO-8 (focal length 50 mm, sensor 36×24, position <c>(0, 1.5, -3)</c>).
    /// </remarks>
    [CreateAssetMenu(
        fileName = "CameraSwitcherOutputAdapterConfig",
        menuName = "VTuberSystemBase/Camera Switcher Output Adapter Config",
        order = 1100)]
    public sealed class CameraSwitcherOutputAdapterConfig : ScriptableObject
    {
        public const string DefaultOscHost = "127.0.0.1";
        public const int DefaultOscPort = 9000;
        public const int DefaultMaxCameras = 32;
        public const float DefaultFocalLengthMm = 50f;

        [Header("OSC Receive")]
        [SerializeField] private string _oscHost = DefaultOscHost;
        [SerializeField] private int _oscPort = DefaultOscPort;

        [Header("Default Camera Transform")]
        [SerializeField] private Vector3 _defaultPosition = new Vector3(0f, 1.5f, -3f);
        [SerializeField] private Quaternion _defaultRotation = Quaternion.identity;
        [SerializeField] private float _defaultFocalLengthMm = DefaultFocalLengthMm;
        [SerializeField] private Vector2 _defaultSensorSize = new Vector2(36f, 24f);

        [Header("Limits")]
        [SerializeField] private int _maxCameras = DefaultMaxCameras;

        public string OscHost => string.IsNullOrEmpty(_oscHost) ? DefaultOscHost : _oscHost;
        public int OscPort => _oscPort > 0 && _oscPort <= 65535 ? _oscPort : DefaultOscPort;
        public Vector3 DefaultPosition => _defaultPosition;
        public Quaternion DefaultRotation => _defaultRotation;
        public float DefaultFocalLengthMm => _defaultFocalLengthMm > 0f ? _defaultFocalLengthMm : DefaultFocalLengthMm;
        public Vector2 DefaultSensorSize => _defaultSensorSize.x > 0f && _defaultSensorSize.y > 0f
            ? _defaultSensorSize
            : new Vector2(36f, 24f);
        public int MaxCameras => _maxCameras > 0 ? _maxCameras : DefaultMaxCameras;
    }
}
