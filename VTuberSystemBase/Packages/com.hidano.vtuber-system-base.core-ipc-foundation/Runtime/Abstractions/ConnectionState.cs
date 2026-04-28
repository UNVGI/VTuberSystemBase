#nullable enable

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public enum ConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        PermanentlyDisconnected = 4,
    }
}
