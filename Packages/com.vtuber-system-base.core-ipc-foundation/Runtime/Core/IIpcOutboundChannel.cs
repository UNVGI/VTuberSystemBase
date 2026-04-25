#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Core
{
    public interface IIpcOutboundChannel
    {
        bool IsConnected { get; }

        ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    }
}
