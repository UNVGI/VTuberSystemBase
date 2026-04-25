#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// UI-side classification of inbound messages observed by
    /// <see cref="IUiSubscriptionClient"/>. Mirrors the subset of
    /// <c>VTuberSystemBase.CoreIpc.Abstractions.MessageKind</c> that tab spec subscribers
    /// actually react to (Requirement 5.6, design.md §UiSubscriptionClient).
    /// </summary>
    public enum MessageKind
    {
        State,
        Event,
        Response,
    }
}
