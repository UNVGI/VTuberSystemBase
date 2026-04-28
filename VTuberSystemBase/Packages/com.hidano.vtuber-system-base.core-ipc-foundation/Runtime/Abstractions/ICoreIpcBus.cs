#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public interface ICoreIpcBus
    {
        IpcResult PublishState<TPayload>(string topic, TPayload payload);

        IpcResult PublishEvent<TPayload>(string topic, TPayload payload);

        Task<IpcResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            RequestOptions? options = null,
            CancellationToken cancellationToken = default);

        ISubscriptionToken SubscribeState<TPayload>(
            string topic,
            Action<TPayload> handler);

        ISubscriptionToken SubscribeEvent<TPayload>(
            string topic,
            Action<TPayload> handler);

        ISubscriptionToken RegisterRequestHandler<TRequest, TResponse>(
            string topic,
            Func<TRequest, CancellationToken, Task<TResponse>> handler);

        IConnectionDiagnostics Diagnostics { get; }
    }

    public interface ISubscriptionToken : IDisposable
    {
    }
}
