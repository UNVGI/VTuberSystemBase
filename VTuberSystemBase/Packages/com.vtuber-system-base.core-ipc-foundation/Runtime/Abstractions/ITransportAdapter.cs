#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public interface ITransportAdapter : IAsyncDisposable
    {
        Task StartServerAsync(ServerBindOptions options, CancellationToken cancellationToken);

        Task<IClientConnection> ConnectClientAsync(ClientBindOptions options, CancellationToken cancellationToken);

        event Action<IClientConnection> ClientConnected;

        event Action<IClientConnection> ClientDisconnected;
    }

    public interface IClientConnection : IAsyncDisposable
    {
        string RemoteEndpoint { get; }

        ValueTask SendAsync(ReadOnlyMemory<byte> textFramePayload, CancellationToken cancellationToken);

        IAsyncEnumerable<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken cancellationToken);
    }

    public readonly record struct ServerBindOptions(string Host, int Port);

    public readonly record struct ClientBindOptions(string Host, int Port, TimeSpan ConnectTimeout);
}
