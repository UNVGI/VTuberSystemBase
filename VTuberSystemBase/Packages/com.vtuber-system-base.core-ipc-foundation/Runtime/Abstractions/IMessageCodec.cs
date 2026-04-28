#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public interface IMessageCodec
    {
        IpcResult<ReadOnlyMemory<byte>> Encode(in MessageEnvelope envelope);

        IpcResult<MessageEnvelope> Decode(ReadOnlyMemory<byte> bytes);
    }
}
