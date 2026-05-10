#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using uOSC;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;

namespace VTuberSystemBase.CameraSwitcherTab.Adapters.Osc
{
    /// <summary>
    /// Default <see cref="IUcapiOscEmitter"/> implementation that wraps
    /// hecomi/uOSC's <see cref="uOscClient"/>. Brings up a hidden
    /// <see cref="GameObject"/> on <see cref="StartAsync"/>, sends every
    /// <see cref="UcapiFlatRecord"/> as a single <c>blob</c> argument to the
    /// configured address, and tears the GameObject down on <see cref="StopAsync"/>
    /// or <see cref="IDisposable.Dispose"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// hecomi/uOSC has no synchronous <c>onErrorInSend</c> callback. We surface
    /// failures synchronously by checking <see cref="uOscClient.isRunning"/>
    /// before each send and by catching exceptions thrown out of
    /// <see cref="uOscClient.Send(string, object[])"/>; asynchronous UDP errors
    /// (ICMP unreachable etc.) are not observable from the public API, so the
    /// caller MUST treat <see cref="OscEmitResult"/> as best-effort enqueue
    /// confirmation only — UDP loss is invisible by design.
    /// </para>
    /// <para>
    /// Lifecycle hardening for PlayMode iteration: the GameObject created on
    /// <see cref="StartAsync"/> is hidden via <c>HideFlags.HideAndDontSave</c>
    /// and explicitly destroyed on <see cref="StopAsync"/> / <see cref="IDisposable.Dispose"/>
    /// to prevent leaked sockets across PlayMode runs.
    /// </para>
    /// </remarks>
    public sealed class UoscFlatRecordEmitter : IUcapiOscEmitter
    {
        private readonly string _gameObjectName;
        private readonly object _stateLock = new object();
        private GameObject? _hostGameObject;
        private uOscClient? _client;
        private OscEmitterState _state = OscEmitterState.Stopped;

        public UoscFlatRecordEmitter(string gameObjectName = "[CameraSwitcher.UoscClient]")
        {
            _gameObjectName = gameObjectName ?? throw new ArgumentNullException(nameof(gameObjectName));
        }

        public OscEmitterState State => _state;

        public event Action<OscEmitFailure>? OnSendFailure;

        public Task<OscEmitResult> StartAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(host))
                return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.InitializationFailed, "host is empty")));
            if (port < 0 || port > 65535)
                return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.InitializationFailed, $"port out of range: {port}")));

            lock (_stateLock)
            {
                if (_state == OscEmitterState.Disposed)
                    return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed)));
                if (_state == OscEmitterState.Running)
                    return Task.FromResult(OscEmitResult.Ok());

                _state = OscEmitterState.Starting;
                try
                {
                    var go = new GameObject(_gameObjectName)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    // OnEnable on the AddComponent triggers uOscClient.StartClient,
                    // so set address/port BEFORE adding the component.
                    go.SetActive(false);
                    var client = go.AddComponent<uOscClient>();
                    client.address = host;
                    client.port = port;
                    go.SetActive(true);
                    if (!client.isRunning)
                    {
                        // Best-effort: surface init failure rather than letting it limp.
                        UnityEngine.Object.Destroy(go);
                        _state = OscEmitterState.Stopped;
                        return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(
                            OscFailureKind.InitializationFailed,
                            $"uOscClient failed to start at {host}:{port}")));
                    }
                    _hostGameObject = go;
                    _client = client;
                    _state = OscEmitterState.Running;
                    return Task.FromResult(OscEmitResult.Ok());
                }
                catch (Exception ex)
                {
                    DestroyHostInternal();
                    _state = OscEmitterState.Stopped;
                    return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(
                        OscFailureKind.InitializationFailed, ex.Message, ex)));
                }
            }
        }

        public Task<OscEmitResult> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                if (_state == OscEmitterState.Disposed)
                    return Task.FromResult(OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed)));
                if (_state == OscEmitterState.Stopped)
                    return Task.FromResult(OscEmitResult.Ok());
                _state = OscEmitterState.Stopping;
                DestroyHostInternal();
                _state = OscEmitterState.Stopped;
                return Task.FromResult(OscEmitResult.Ok());
            }
        }

        public OscEmitResult Send(string address, in UcapiFlatRecord record)
        {
            // Synchronous validation; never throw.
            if (_state == OscEmitterState.Disposed)
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.Disposed));
            if (_state != OscEmitterState.Running || _client == null)
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.NotStarted));
            if (string.IsNullOrEmpty(address))
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.InvalidAddress));
            if (!record.HasValue || record.Length == 0)
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.SerializeFailed, "record is empty"));
            if (!_client.isRunning)
                return OscEmitResult.Fail(new OscEmitFailure(OscFailureKind.SocketError, "uOscClient is not running"));

            try
            {
                _client.Send(address, record.AsBytes());
                return OscEmitResult.Ok();
            }
            catch (Exception ex)
            {
                var failure = new OscEmitFailure(OscFailureKind.SocketError, ex.Message, ex);
                // Surface to subscribers (Coordinator.FailureAggregator).
                try { OnSendFailure?.Invoke(failure); } catch { /* swallow per design */ }
                return OscEmitResult.Fail(failure);
            }
        }

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (_state == OscEmitterState.Disposed) return;
                DestroyHostInternal();
                _state = OscEmitterState.Disposed;
            }
        }

        /// <summary>
        /// Manually surface a failure to <see cref="OnSendFailure"/>. Wired up by
        /// <see cref="OscClientLifecycle"/> when it observes adapter-external
        /// failures (e.g. port-in-use detected during StartAsync).
        /// </summary>
        internal void RaiseSendFailure(OscEmitFailure failure)
        {
            try { OnSendFailure?.Invoke(failure); } catch { /* swallow */ }
        }

        private void DestroyHostInternal()
        {
            if (_hostGameObject != null)
            {
                try { UnityEngine.Object.Destroy(_hostGameObject); } catch { /* PlayMode shutdown */ }
                _hostGameObject = null;
            }
            _client = null;
        }
    }
}
