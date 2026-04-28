#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public abstract record CoreIpcError(string Code, string Message)
    {
        public sealed record NotConnected()
            : CoreIpcError("NOT_CONNECTED", "The IPC connection is not established.");

        public sealed record SizeLimitExceeded(long ActualBytes, long LimitBytes)
            : CoreIpcError(
                "SIZE_LIMIT",
                $"Message size {ActualBytes} bytes exceeds the limit of {LimitBytes} bytes.");

        public sealed record InvalidTopic(string Topic)
            : CoreIpcError("INVALID_TOPIC", $"Topic '{Topic}' is null, empty, or otherwise invalid.");

        public sealed record InvalidEnvelope(string Reason)
            : CoreIpcError("INVALID_ENVELOPE", $"Envelope is invalid: {Reason}");

        public sealed record RequestTimeout(TimeSpan Elapsed)
            : CoreIpcError("TIMEOUT", $"Request timed out after {Elapsed.TotalMilliseconds:0} ms.");

        public sealed record PortInUse(int Port)
            : CoreIpcError("PORT_IN_USE", $"Port {Port} is already in use.");

        public sealed record ProtocolVersionMismatch(string Received, string Expected)
            : CoreIpcError(
                "VERSION_MISMATCH",
                $"Protocol version mismatch: received '{Received}', expected major-compatible with '{Expected}'.");

        public sealed record TransportFailure(string Details)
            : CoreIpcError("TRANSPORT", $"Transport failure: {Details}");

        public sealed record HandlerException(string Details)
            : CoreIpcError("HANDLER_EX", $"Handler raised an exception: {Details}");
    }
}
