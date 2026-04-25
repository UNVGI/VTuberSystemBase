#nullable enable
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Codec;
using VTuberSystemBase.CoreIpc.Core.Connection;
using VTuberSystemBase.CoreIpc.Core.Correlation;
using VTuberSystemBase.CoreIpc.Core.Diagnostics;
using VTuberSystemBase.CoreIpc.Core.Dispatch;
using VTuberSystemBase.CoreIpc.Core.Subscription;
using VTuberSystemBase.CoreIpc.Core.Transport.WebSocket;

namespace VTuberSystemBase.CoreIpc.Core
{
    public sealed class CoreIpcRuntimeHost : ICoreIpcRuntime, IAsyncDisposable
    {
        private readonly object _sync = new();
        private readonly Func<CoreIpcOptions, ITransportAdapter> _transportFactory;
        private readonly bool _installPlayerLoop;
        private readonly bool _registerAsCurrent;
        private readonly Func<TimeSpan, CancellationToken, Task>? _clientReconnectDelay;
        private readonly Action<Action>? _mainThreadPost;
        private readonly Action<string>? _logInfo;
        private readonly Action<string>? _logWarning;
        private readonly Action<string, Exception>? _logError;

        private RuntimeState _state = RuntimeState.NotInitialized;
        private CoreIpcOptions _options = new();

        private SystemTextJsonCodec? _codec;
        private TopicSubscriptionRegistry? _subscriptions;
        private MainThreadDispatchQueue? _dispatchQueue;
        private RequestCorrelationRegistry? _correlation;
        private ConnectionStateMachine? _stateMachine;
        private ReconnectBackoff? _backoff;
        private CoreIpcDiagnostics? _diagnostics;
        private CoreIpcBus? _bus;
        private RuntimeOutboundChannel? _outbound;
        private ITransportAdapter? _transport;
        private ClientSessionManager? _clientSession;
        private IpcDispatchStep? _dispatchStep;

        public CoreIpcRuntimeHost(
            Func<CoreIpcOptions, ITransportAdapter>? transportFactory = null,
            bool installPlayerLoop = true,
            bool registerAsCurrent = true,
            Action<Action>? mainThreadPost = null,
            Func<TimeSpan, CancellationToken, Task>? clientReconnectDelay = null,
            Action<string>? logInfo = null,
            Action<string>? logWarning = null,
            Action<string, Exception>? logError = null)
        {
            _transportFactory = transportFactory ?? CreateDefaultTransport;
            _installPlayerLoop = installPlayerLoop;
            _registerAsCurrent = registerAsCurrent;
            _mainThreadPost = mainThreadPost;
            _clientReconnectDelay = clientReconnectDelay;
            _logInfo = logInfo;
            _logWarning = logWarning;
            _logError = logError;
        }

        public RuntimeState State
        {
            get { lock (_sync) return _state; }
        }

        public CoreIpcOptions Options
        {
            get { lock (_sync) return _options; }
        }

        public ICoreIpcBus Bus
        {
            get
            {
                var bus = Volatile.Read(ref _bus);
                if (bus is null)
                {
                    throw new InvalidOperationException(
                        "CoreIpcRuntime.Bus is unavailable; runtime has not been initialized.");
                }
                return bus;
            }
        }

        public async Task InitializeAsync(CoreIpcOptions options, CancellationToken cancellationToken = default)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));

            lock (_sync)
            {
                if (_state == RuntimeState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(CoreIpcRuntimeHost));
                }
                if (_state != RuntimeState.NotInitialized)
                {
                    throw new InvalidOperationException(
                        $"CoreIpcRuntime cannot be initialized while in state {_state}; expected {RuntimeState.NotInitialized}.");
                }
                _state = RuntimeState.Initializing;
                _options = options;
            }

            SystemTextJsonCodec? codec = null;
            TopicSubscriptionRegistry? subscriptions = null;
            MainThreadDispatchQueue? dispatchQueue = null;
            RequestCorrelationRegistry? correlation = null;
            ConnectionStateMachine? stateMachine = null;
            ReconnectBackoff? backoff = null;
            CoreIpcDiagnostics? diagnostics = null;
            CoreIpcBus? bus = null;
            RuntimeOutboundChannel? outbound = null;
            ITransportAdapter? transport = null;
            ClientSessionManager? clientSession = null;
            IpcDispatchStep? dispatchStep = null;

            try
            {
                codec = new SystemTextJsonCodec(options);
                subscriptions = new TopicSubscriptionRegistry();
                dispatchQueue = new MainThreadDispatchQueue(options, _logWarning, _logError);
                dispatchQueue.SetHandlerLookup(subscriptions);

                correlation = new RequestCorrelationRegistry(options, _mainThreadPost, _logError);
                stateMachine = new ConnectionStateMachine(_logWarning);
                backoff = new ReconnectBackoff(
                    options.ReconnectInitialDelay,
                    options.ReconnectMultiplier,
                    options.ReconnectMaxDelay,
                    options.ReconnectMaxAttempts);

                outbound = new RuntimeOutboundChannel();

                var queueRef = dispatchQueue;
                var correlationRef = correlation;
                var stateMachineRef = stateMachine;
                var backoffRef = backoff;
                diagnostics = new CoreIpcDiagnostics(
                    stateMachine,
                    reconnectAttemptCountProvider: () => backoffRef.AttemptCount,
                    pendingRequestCountProvider: () => correlationRef.PendingRequestCount,
                    stateSlotCountProvider: () => queueRef.StateSlotCount,
                    eventQueueCountProvider: () => queueRef.EventQueueCount,
                    connectedClientCountProvider: () => GetConnectedClientCount());

                bus = new CoreIpcBus(
                    options,
                    codec,
                    outbound,
                    correlation,
                    subscriptions,
                    diagnostics,
                    logError: _logError);

                transport = _transportFactory(options);
                if (transport is null)
                {
                    throw new InvalidOperationException(
                        "CoreIpcRuntimeHost transport factory returned null.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                await transport.StartServerAsync(
                    new ServerBindOptions(options.Host, options.Port),
                    cancellationToken).ConfigureAwait(false);

                var clientBindOptions = new ClientBindOptions(
                    options.Host,
                    options.Port,
                    TimeSpan.FromSeconds(5));

                var codecRef = codec;
                var correlationCallbackRef = correlation;
                var subscriptionsRef = subscriptions;
                var dispatchQueueRef = dispatchQueue;

                clientSession = new ClientSessionManager(
                    transport,
                    clientBindOptions,
                    stateMachine,
                    backoff,
                    delay: _clientReconnectDelay,
                    onMessageReceived: bytes => RouteInboundBytes(
                        bytes,
                        codecRef,
                        correlationCallbackRef,
                        dispatchQueueRef),
                    logWarning: _logWarning,
                    logError: msg => _logError?.Invoke(msg, new InvalidOperationException(msg)));

                outbound.Bind(() => clientSession.CurrentConnection, () => stateMachineRef.CurrentState);

                if (_installPlayerLoop)
                {
                    dispatchStep = new IpcDispatchStep(dispatchQueue, _logError);
                    dispatchStep.Install(_logWarning);
                }

                await clientSession.StartAsync(cancellationToken).ConfigureAwait(false);

                lock (_sync)
                {
                    _codec = codec;
                    _subscriptions = subscriptions;
                    _dispatchQueue = dispatchQueue;
                    _correlation = correlation;
                    _stateMachine = stateMachine;
                    _backoff = backoff;
                    _diagnostics = diagnostics;
                    _bus = bus;
                    _outbound = outbound;
                    _transport = transport;
                    _clientSession = clientSession;
                    _dispatchStep = dispatchStep;
                    _state = RuntimeState.Running;
                }

                if (_registerAsCurrent)
                {
                    CoreIpcRuntime.SetCurrent(this);
                }

                _logInfo?.Invoke(
                    $"CoreIpcRuntime initialized (host={options.Host}, port={options.Port}).");
            }
            catch
            {
                await CleanupOnInitFailureAsync(
                    dispatchStep,
                    clientSession,
                    transport,
                    correlation,
                    diagnostics).ConfigureAwait(false);

                lock (_sync)
                {
                    _state = RuntimeState.NotInitialized;
                    _options = new CoreIpcOptions();
                }

                throw;
            }
        }

        public void Dispose()
        {
            DisposeInternal().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeInternal().ConfigureAwait(false);
        }

        private async Task DisposeInternal()
        {
            ClientSessionManager? clientSession;
            ITransportAdapter? transport;
            RequestCorrelationRegistry? correlation;
            CoreIpcDiagnostics? diagnostics;
            IpcDispatchStep? dispatchStep;
            RuntimeOutboundChannel? outbound;
            bool shouldClearSingleton;

            lock (_sync)
            {
                if (_state == RuntimeState.Disposed) return;
                if (_state == RuntimeState.ShuttingDown) return;

                _state = RuntimeState.ShuttingDown;
                clientSession = _clientSession;
                transport = _transport;
                correlation = _correlation;
                diagnostics = _diagnostics;
                dispatchStep = _dispatchStep;
                outbound = _outbound;
                shouldClearSingleton = true;
            }

            try
            {
                outbound?.Detach();

                if (dispatchStep is not null)
                {
                    try { dispatchStep.Uninstall(); }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke(
                            $"CoreIpcRuntime: dispatch step uninstall threw: {ex.Message}");
                    }
                }

                if (clientSession is not null)
                {
                    try
                    {
                        await clientSession.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke(
                            $"CoreIpcRuntime: client session dispose threw: {ex.Message}");
                    }
                }

                if (correlation is not null)
                {
                    try { correlation.Dispose(); }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke(
                            $"CoreIpcRuntime: correlation dispose threw: {ex.Message}");
                    }
                }

                if (transport is not null)
                {
                    try
                    {
                        await transport.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke(
                            $"CoreIpcRuntime: transport dispose threw: {ex.Message}");
                    }
                }

                if (diagnostics is not null)
                {
                    try { diagnostics.Dispose(); }
                    catch (Exception ex)
                    {
                        _logWarning?.Invoke(
                            $"CoreIpcRuntime: diagnostics dispose threw: {ex.Message}");
                    }
                }
            }
            finally
            {
                lock (_sync)
                {
                    _codec = null;
                    _subscriptions = null;
                    _dispatchQueue = null;
                    _correlation = null;
                    _stateMachine = null;
                    _backoff = null;
                    _diagnostics = null;
                    _bus = null;
                    _outbound = null;
                    _transport = null;
                    _clientSession = null;
                    _dispatchStep = null;
                    _state = RuntimeState.Disposed;
                }

                if (shouldClearSingleton && _registerAsCurrent)
                {
                    CoreIpcRuntime.ClearCurrent(this);
                }
            }
        }

        private async Task CleanupOnInitFailureAsync(
            IpcDispatchStep? dispatchStep,
            ClientSessionManager? clientSession,
            ITransportAdapter? transport,
            RequestCorrelationRegistry? correlation,
            CoreIpcDiagnostics? diagnostics)
        {
            if (dispatchStep is not null)
            {
                try { dispatchStep.Uninstall(); }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"CoreIpcRuntime: init-failure dispatch uninstall threw: {ex.Message}");
                }
            }

            if (clientSession is not null)
            {
                try
                {
                    await clientSession.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"CoreIpcRuntime: init-failure client session dispose threw: {ex.Message}");
                }
            }

            if (transport is not null)
            {
                try
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"CoreIpcRuntime: init-failure transport dispose threw: {ex.Message}");
                }
            }

            if (correlation is not null)
            {
                try { correlation.Dispose(); }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"CoreIpcRuntime: init-failure correlation dispose threw: {ex.Message}");
                }
            }

            if (diagnostics is not null)
            {
                try { diagnostics.Dispose(); }
                catch (Exception ex)
                {
                    _logWarning?.Invoke(
                        $"CoreIpcRuntime: init-failure diagnostics dispose threw: {ex.Message}");
                }
            }
        }

        private int GetConnectedClientCount()
        {
            var transport = _transport;
            if (transport is WebSocketTransportAdapter ws) return ws.ConnectedClientCount;
            return 0;
        }

        private void RouteInboundBytes(
            ReadOnlyMemory<byte> bytes,
            SystemTextJsonCodec codec,
            RequestCorrelationRegistry correlation,
            MainThreadDispatchQueue dispatchQueue)
        {
            var decoded = codec.Decode(bytes);
            if (!decoded.Success)
            {
                _logWarning?.Invoke(
                    $"CoreIpcRuntime: dropping inbound message that failed to decode: {decoded.Error?.Message}");
                return;
            }

            var envelope = decoded.Value;
            if (envelope.Kind == MessageKind.Response)
            {
                if (string.IsNullOrEmpty(envelope.CorrelationId))
                {
                    _logWarning?.Invoke(
                        $"CoreIpcRuntime: dropping Response with empty correlationId on topic '{envelope.Topic}'.");
                    return;
                }
                correlation.MatchResponse(envelope.CorrelationId!, envelope.Payload);
                return;
            }

            dispatchQueue.Enqueue(envelope);
        }

        private static ITransportAdapter CreateDefaultTransport(CoreIpcOptions options)
        {
            var codec = new SystemTextJsonCodec(options);
            return new WebSocketTransportAdapter(codec);
        }

        private sealed class RuntimeOutboundChannel : IIpcOutboundChannel
        {
            private Func<IClientConnection?>? _connectionAccessor;
            private Func<ConnectionState>? _stateAccessor;

            public bool IsConnected
            {
                get
                {
                    var accessor = _connectionAccessor;
                    if (accessor is null) return false;
                    var connection = accessor();
                    if (connection is null) return false;

                    var stateAccessor = _stateAccessor;
                    if (stateAccessor is null) return true;
                    return stateAccessor() == ConnectionState.Connected;
                }
            }

            public ValueTask SendAsync(
                ReadOnlyMemory<byte> bytes,
                CancellationToken cancellationToken)
            {
                var accessor = _connectionAccessor;
                var connection = accessor?.Invoke();
                if (connection is null)
                {
                    throw new InvalidOperationException(
                        "RuntimeOutboundChannel: no active client connection to send through.");
                }
                return connection.SendAsync(bytes, cancellationToken);
            }

            public void Bind(
                Func<IClientConnection?> connectionAccessor,
                Func<ConnectionState> stateAccessor)
            {
                _connectionAccessor = connectionAccessor;
                _stateAccessor = stateAccessor;
            }

            public void Detach()
            {
                _connectionAccessor = null;
                _stateAccessor = null;
            }
        }
    }
}
