#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// UI-side handle returned by
    /// <see cref="IUiSubscriptionClient.Subscribe{TPayload}(string, MessageKind, Action{MessageEnvelope{TPayload}})"/>.
    /// Subscribers <see cref="IDisposable.Dispose"/> the token to stop receiving messages;
    /// <see cref="IsActive"/> transitions monotonically <c>true → false</c> on Dispose
    /// (Requirement 5.7, design.md §UiSubscriptionClient Invariants).
    /// </summary>
    public interface ISubscriptionToken : IDisposable
    {
        string Topic { get; }
        bool IsActive { get; }
    }
}
