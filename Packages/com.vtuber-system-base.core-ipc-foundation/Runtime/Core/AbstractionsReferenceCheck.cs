#nullable enable
using System.Text.Json;
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
    }
}
