#nullable enable

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public enum RuntimeState
    {
        NotInitialized = 0,
        Initializing = 1,
        Running = 2,
        ShuttingDown = 3,
        Disposed = 4,
    }
}
