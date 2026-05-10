using System;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.RacMainOutputAdapter.Internal
{
    /// <summary>
    /// <see cref="IAdapterMessageSink"/> の本番実装。
    /// <see cref="ICoreIpcBus.PublishState{TPayload}"/> / <see cref="ICoreIpcBus.PublishEvent{TPayload}"/>
    /// に直接転送する。
    /// </summary>
    public sealed class CoreIpcBusMessageSink : IAdapterMessageSink
    {
        private readonly ICoreIpcBus _bus;

        /// <summary><paramref name="bus"/> を委譲先として保持する。</summary>
        public CoreIpcBusMessageSink(ICoreIpcBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        /// <inheritdoc/>
        public void PublishState<TPayload>(string topic, TPayload payload)
        {
            _bus.PublishState(topic, payload);
        }

        /// <inheritdoc/>
        public void PublishEvent<TPayload>(string topic, TPayload payload)
        {
            _bus.PublishEvent(topic, payload);
        }
    }
}
