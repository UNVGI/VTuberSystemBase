#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Public Facade for tab spec code to subscribe to inbound state/event messages from the
    /// main-output side via the <c>core-ipc-foundation</c> abstraction. Callbacks fire on the
    /// Unity main thread (D-3 inheritance) and exceptions thrown by callbacks are caught and
    /// logged (LogCategory.Ipc) without disrupting other subscribers. See design.md
    /// §Commands §UiSubscriptionClient (Requirements 5.6, 5.7, 5.8, 11.5).
    /// </summary>
    public interface IUiSubscriptionClient
    {
        ISubscriptionToken Subscribe<TPayload>(
            string topic,
            MessageKind kind,
            Action<MessageEnvelope<TPayload>> callback);
    }
}
