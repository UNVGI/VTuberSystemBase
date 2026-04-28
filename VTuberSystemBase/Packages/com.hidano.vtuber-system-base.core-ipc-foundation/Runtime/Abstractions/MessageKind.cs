#nullable enable

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public enum MessageKind
    {
        State = 0,
        Event = 1,
        Request = 2,
        Response = 3,
    }
}
