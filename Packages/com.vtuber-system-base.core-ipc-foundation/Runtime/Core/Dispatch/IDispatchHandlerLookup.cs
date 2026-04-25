#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Dispatch
{
    public delegate void DispatchHandler(MessageEnvelope envelope);

    public interface IDispatchHandlerLookup
    {
        bool TryGetHandlers(
            string topic,
            MessageKind kind,
            out IReadOnlyList<DispatchHandler> handlers);
    }
}
