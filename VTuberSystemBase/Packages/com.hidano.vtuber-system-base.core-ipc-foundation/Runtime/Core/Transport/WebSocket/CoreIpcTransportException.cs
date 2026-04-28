#nullable enable
using System;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed class CoreIpcTransportException : Exception
    {
        public CoreIpcError IpcError { get; }

        public CoreIpcTransportException(CoreIpcError error)
            : base(error?.Message ?? "Transport error")
        {
            IpcError = error ?? throw new ArgumentNullException(nameof(error));
        }
    }
}
