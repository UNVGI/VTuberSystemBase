#nullable enable
using System;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal
{
    /// <summary>
    /// User-provided locator that returns the application's <see cref="ICoreIpcBus"/>.
    /// Lives outside the adapter package because <c>OutputSceneBootstrapper</c> currently
    /// keeps the bus private (Fact 2). Application code attaches a MonoBehaviour or
    /// service that implements this interface; the adapter Bootstrapper looks it up via
    /// <c>FindObjectOfType</c>.
    /// </summary>
    public interface ICoreIpcBusProvider
    {
        ICoreIpcBus? Bus { get; }
    }

    /// <summary>
    /// Default <see cref="IAdapterMessageSink"/> implementation that publishes through an
    /// <see cref="ICoreIpcBus"/>. Each <c>Publish*</c> call forwards the topic / payload
    /// and reports success based on <see cref="IpcResult.Success"/>.
    /// </summary>
    internal sealed class CoreIpcBusMessageSink : IAdapterMessageSink
    {
        private readonly ICoreIpcBus _bus;

        public CoreIpcBusMessageSink(ICoreIpcBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        public bool PublishState<TPayload>(string topic, TPayload payload)
            => _bus.PublishState(topic, payload).Success;

        public bool PublishEvent<TPayload>(string topic, TPayload payload)
            => _bus.PublishEvent(topic, payload).Success;
    }
}
