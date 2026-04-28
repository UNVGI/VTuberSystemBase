#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public interface ICoreIpcRuntime : IDisposable
    {
        RuntimeState State { get; }

        ICoreIpcBus Bus { get; }

        CoreIpcOptions Options { get; }

        Task InitializeAsync(CoreIpcOptions options, CancellationToken cancellationToken = default);
    }
}
