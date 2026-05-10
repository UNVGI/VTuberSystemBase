#nullable enable

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal
{
    /// <summary>
    /// Adapter-side outbound message sink. The adapter publishes state and event payloads
    /// to the IPC bus through this abstraction so that production code can route through
    /// <c>ICoreIpcBus</c> while tests can capture every emitted message via
    /// <c>RecordingMessageSink</c>.
    /// </summary>
    /// <remarks>
    /// Returns a coarse <see cref="bool"/> rather than the full <c>IpcResult</c> because the
    /// only behavior the adapter needs to vary is "publish vs swallow"; richer error
    /// information is logged through <c>AdapterLogger</c> and surfaced via
    /// <c>StageLightingVolumeOutputAdapterDiagnostics</c>.
    /// </remarks>
    internal interface IAdapterMessageSink
    {
        /// <summary>
        /// Publishes <paramref name="payload"/> as a state message under
        /// <paramref name="topic"/>. Returns <c>true</c> on success.
        /// </summary>
        bool PublishState<TPayload>(string topic, TPayload payload);

        /// <summary>
        /// Publishes <paramref name="payload"/> as an event message under
        /// <paramref name="topic"/>. Returns <c>true</c> on success.
        /// </summary>
        bool PublishEvent<TPayload>(string topic, TPayload payload);
    }
}
