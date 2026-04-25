#nullable enable
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core
{
    internal static class AbstractionsReferenceCheck
    {
        public static MessageEnvelope BuildSampleEnvelope()
        {
            return new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: MessageKind.State,
                Topic: "core-ipc/probe",
                CorrelationId: null,
                TimestampUnixMs: 0L,
                Payload: default(JsonElement));
        }

        public static ConnectionState DefaultConnectionState => ConnectionState.Disconnected;

        public static RuntimeState DefaultRuntimeState => RuntimeState.NotInitialized;

        public static Type[] AbstractionContractTypes => new[]
        {
            typeof(ICoreIpcBus),
            typeof(ICoreIpcRuntime),
            typeof(ITransportAdapter),
            typeof(IClientConnection),
            typeof(IMessageCodec),
            typeof(IConnectionDiagnostics),
            typeof(IAuthenticationHandler),
            typeof(ISubscriptionToken),
            typeof(RequestOptions),
            typeof(ServerBindOptions),
            typeof(ClientBindOptions),
            typeof(DiagnosticsSnapshot),
            typeof(AuthenticationContext),
        };

        public static RequestOptions BuildSampleRequestOptions(TimeSpan timeout) => new(timeout);

        public static ServerBindOptions BuildSampleServerBindOptions(string host, int port) => new(host, port);

        public static ClientBindOptions BuildSampleClientBindOptions(
            string host,
            int port,
            TimeSpan connectTimeout) => new(host, port, connectTimeout);

        public static DiagnosticsSnapshot BuildSampleDiagnosticsSnapshot()
        {
            return new DiagnosticsSnapshot(
                TakenAt: DateTimeOffset.UnixEpoch,
                ClientState: ConnectionState.Disconnected,
                ServerConnectedCount: 0,
                ReconnectAttemptCount: 0,
                PendingRequestCount: 0,
                StateSlotCount: 0,
                EventQueueCount: 0);
        }

        public static Task<IpcResult<TResponse>> CallRequestAsync<TRequest, TResponse>(
            ICoreIpcBus bus,
            string topic,
            TRequest payload,
            CancellationToken ct)
        {
            return bus.RequestAsync<TRequest, TResponse>(topic, payload, options: null, cancellationToken: ct);
        }
    }
}
