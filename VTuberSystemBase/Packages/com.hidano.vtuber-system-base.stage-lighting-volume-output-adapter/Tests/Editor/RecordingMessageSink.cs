#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    /// <summary>
    /// Records every <see cref="IAdapterMessageSink"/> publish call so tests can verify
    /// topic / payload / ordering.
    /// </summary>
    internal sealed class RecordingMessageSink : IAdapterMessageSink
    {
        public readonly List<(string Topic, object? Payload)> PublishedStates = new();
        public readonly List<(string Topic, object? Payload)> PublishedEvents = new();

        /// <summary>Optional override; when set, the corresponding Publish* call returns it.</summary>
        public bool NextStateResult { get; set; } = true;
        public bool NextEventResult { get; set; } = true;

        public bool PublishState<TPayload>(string topic, TPayload payload)
        {
            PublishedStates.Add((topic, payload));
            return NextStateResult;
        }

        public bool PublishEvent<TPayload>(string topic, TPayload payload)
        {
            PublishedEvents.Add((topic, payload));
            return NextEventResult;
        }

        public void Clear()
        {
            PublishedStates.Clear();
            PublishedEvents.Clear();
        }
    }
}
