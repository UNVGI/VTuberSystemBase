#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using uOSC;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Ucapi;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Osc
{
    /// <summary>
    /// Default <see cref="IOscReceiverHost"/> implementation. Owns a hidden
    /// <see cref="GameObject"/> with a <c>uOSC.uOscServer</c> component and converts
    /// incoming <c>uOSC.Message</c> values into <see cref="OscReceivedMessage"/>
    /// after parsing the cameraId via <see cref="FlatRecordAddressDecoder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lifecycle: <see cref="StartAsync"/> creates the host GameObject (with
    /// <c>autoStart=false</c>), assigns the port, calls <c>StartServer()</c>, and
    /// subscribes to <c>onDataReceived</c>. <see cref="StopAsync"/> calls
    /// <c>StopServer()</c> then destroys the GameObject. Re-Start after Stop /
    /// Failure is supported.
    /// </para>
    /// <para>
    /// Per CSO-3 the OSC dispatch is already on the Unity main thread (uOSC's
    /// <c>uOscServer.Update()</c> drains the parser queue), so no SynchronizationContext
    /// marshalling is added.
    /// </para>
    /// </remarks>
    public sealed class UoscReceiverHostAdapter : IOscReceiverHost
    {
        public const string DefaultPrefix = FlatRecordAddressDecoder.DefaultPrefix;

        private readonly string _addressPrefix;
        private readonly Action<UoscDecodeFailure>? _onDecodeFailure;
        private GameObject? _hostGameObject;
        private uOscServer? _server;
        private bool _disposed;
        private OscReceiverHostStatus _status = OscReceiverHostStatus.Stopped;

        public UoscReceiverHostAdapter(string? addressPrefix = null, Action<UoscDecodeFailure>? onDecodeFailure = null)
        {
            _addressPrefix = string.IsNullOrEmpty(addressPrefix) ? DefaultPrefix : addressPrefix!;
            _onDecodeFailure = onDecodeFailure;
        }

        public OscReceiverHostStatus Status => _status;

        public event Action<OscReceivedMessage>? MessageReceived;

        public Task<OscReceiverStartResult> StartAsync(string host, int port, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_disposed) return Task.FromResult(OscReceiverStartResult.Failure("adapter disposed"));
            if (_status == OscReceiverHostStatus.Running)
                return Task.FromResult(OscReceiverStartResult.Ok());
            if (port < 0 || port > 65535)
                return FailStart($"port out of range: {port}");
            if (string.IsNullOrEmpty(host))
                return FailStart("host is empty");

            _status = OscReceiverHostStatus.Starting;

            GameObject? go = null;
            try
            {
                go = new GameObject(CameraOscReceiverHost.DefaultGameObjectName)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                go.SetActive(false);
                go.AddComponent<CameraOscReceiverHost>();

                var server = go.AddComponent<uOscServer>();
                server.port = port;
                server.autoStart = false;
                server.onDataReceived.AddListener(OnDataReceived);

                go.SetActive(true);
                server.StartServer();

                if (!server.isRunning)
                {
                    UnityEngine.Object.Destroy(go);
                    _status = OscReceiverHostStatus.Failed;
                    return Task.FromResult(OscReceiverStartResult.Failure(
                        $"uOscServer failed to bind {host}:{port}"));
                }

                _hostGameObject = go;
                _server = server;
                _status = OscReceiverHostStatus.Running;
                return Task.FromResult(OscReceiverStartResult.Ok());
            }
            catch (Exception ex)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                _hostGameObject = null;
                _server = null;
                _status = OscReceiverHostStatus.Failed;
                return Task.FromResult(OscReceiverStartResult.Failure(ex.Message, ex));
            }
        }

        public Task StopAsync()
        {
            DestroyHost();
            _status = OscReceiverHostStatus.Stopped;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DestroyHost();
            _status = OscReceiverHostStatus.Stopped;
        }

        private Task<OscReceiverStartResult> FailStart(string detail)
        {
            _status = OscReceiverHostStatus.Failed;
            return Task.FromResult(OscReceiverStartResult.Failure(detail));
        }

        private void DestroyHost()
        {
            if (_server != null)
            {
                try { _server.StopServer(); } catch { /* ignore – defensive */ }
                _server = null;
            }
            if (_hostGameObject != null)
            {
                try { UnityEngine.Object.Destroy(_hostGameObject); } catch { /* ignore */ }
                _hostGameObject = null;
            }
        }

        private void OnDataReceived(Message msg)
        {
            // CSO-3: this fires on the Unity main thread via uOscServer.Update.
            var address = msg.address;
            if (string.IsNullOrEmpty(address)) return;

            var cameraId = FlatRecordAddressDecoder.TryDecodeCameraId(address, _addressPrefix);
            if (cameraId == null)
            {
                _onDecodeFailure?.Invoke(new UoscDecodeFailure(UoscDecodeFailureKind.AddressMismatch, address, null));
                return;
            }

            if (msg.values == null || msg.values.Length == 0)
            {
                _onDecodeFailure?.Invoke(new UoscDecodeFailure(UoscDecodeFailureKind.NoBlobArgument, address, cameraId));
                return;
            }

            if (msg.values[0] is not byte[] blob)
            {
                _onDecodeFailure?.Invoke(new UoscDecodeFailure(UoscDecodeFailureKind.NotABlob, address, cameraId));
                return;
            }

            var event_ = new OscReceivedMessage(cameraId, blob);
            try
            {
                MessageReceived?.Invoke(event_);
            }
            catch (Exception ex)
            {
                _onDecodeFailure?.Invoke(new UoscDecodeFailure(UoscDecodeFailureKind.HandlerException, address, cameraId, ex));
            }
        }
    }

    public enum UoscDecodeFailureKind
    {
        AddressMismatch,
        NoBlobArgument,
        NotABlob,
        HandlerException,
    }

    public readonly struct UoscDecodeFailure
    {
        public UoscDecodeFailure(UoscDecodeFailureKind kind, string address, string? cameraId, Exception? exception = null)
        {
            Kind = kind;
            Address = address;
            CameraId = cameraId;
            Exception = exception;
        }

        public UoscDecodeFailureKind Kind { get; }
        public string Address { get; }
        public string? CameraId { get; }
        public Exception? Exception { get; }
    }
}
