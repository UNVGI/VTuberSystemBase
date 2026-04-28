#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Public Facade exposed to tab spec code for sending Commands across the IPC boundary.
    /// Routes <c>state</c> / <c>event</c> / <c>request</c> calls into the
    /// <c>core-ipc-foundation</c> abstraction without exposing the concrete transport.
    /// All three send paths return Result-style values rather than throwing, so a malformed
    /// payload or a dropped connection cannot crash the UI (Requirement 5.9, 9.4).
    /// See design.md §Commands §UiCommandClient (Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.9, 5.10, 9.3, 9.4, 11.4).
    /// </summary>
    public interface IUiCommandClient
    {
        SendResult PublishState<TPayload>(string topic, TPayload payload);

        SendResult PublishEvent<TPayload>(string topic, TPayload payload);

        Task<RequestResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default);
    }
}
