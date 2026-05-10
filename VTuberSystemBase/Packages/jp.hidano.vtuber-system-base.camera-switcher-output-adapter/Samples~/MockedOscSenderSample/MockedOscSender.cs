#nullable enable
using UnityEngine;
using uOSC;
using UCAPI4Unity.Runtime.UnityCamera;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Samples.MockedOscSenderSample
{
    /// <summary>
    /// Sample MonoBehaviour that drives a same-process <see cref="uOscClient"/>
    /// to send UCAPI Flat Records at a configurable rate. Drop this on a
    /// GameObject alongside the <c>OutputSceneBootstrapper</c> + bootstrapper to
    /// validate the OSC receive pipeline without the UI side.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MockedOscSender : MonoBehaviour
    {
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 9000;
        [SerializeField] private string _cameraId = "cam-0001";
        [SerializeField] private float _frequencyHz = 60f;
        [SerializeField] private float _orbitRadius = 4f;
        [SerializeField] private float _focalLengthMm = 50f;

        private uOscClient? _client;
        private Camera? _emitterCamera;
        private float _phase;
        private float _accumulator;

        private void OnEnable()
        {
            var clientGo = new GameObject("[MockedOscSender.uOscClient]")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            clientGo.transform.SetParent(transform, worldPositionStays: false);
            clientGo.SetActive(false);
            _client = clientGo.AddComponent<uOscClient>();
            _client.address = _host;
            _client.port = _port;
            clientGo.SetActive(true);

            var emitterGo = new GameObject("[MockedOscSender.SourceCamera]")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            emitterGo.transform.SetParent(transform, worldPositionStays: false);
            _emitterCamera = emitterGo.AddComponent<Camera>();
            _emitterCamera.usePhysicalProperties = true;
            _emitterCamera.focalLength = _focalLengthMm;
            _emitterCamera.sensorSize = new Vector2(36f, 24f);
        }

        private void OnDisable()
        {
            if (_emitterCamera != null) Destroy(_emitterCamera.gameObject);
            _emitterCamera = null;
            if (_client != null) Destroy(_client.gameObject);
            _client = null;
        }

        private void Update()
        {
            if (_client == null || _emitterCamera == null) return;
            if (_frequencyHz <= 0f) return;

            var period = 1f / _frequencyHz;
            _accumulator += Time.deltaTime;
            while (_accumulator >= period)
            {
                _accumulator -= period;
                _phase += period * 2f * Mathf.PI / 4f; // 4-second orbit.
                var x = Mathf.Cos(_phase) * _orbitRadius;
                var z = Mathf.Sin(_phase) * _orbitRadius;
                _emitterCamera.transform.SetPositionAndRotation(
                    new Vector3(x, 1.5f, z),
                    Quaternion.LookRotation(-new Vector3(x, 0f, z), Vector3.up));
                var blob = UcApi4UnityCamera.SerializeFromCamera(_emitterCamera);
                _client.Send($"/ucapi/camera/{_cameraId}/flat", blob);
            }
        }
    }
}
