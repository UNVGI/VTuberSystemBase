using System;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.Bootstrapper;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.Internal;

namespace VTuberSystemBase.RacMainOutputAdapter.Senders
{
    /// <summary>
    /// <c>slot/{slotId}/status</c> state を発行するヘルパ（Requirement 2.2 / 2.3 / 2.6 / 7.7）。
    /// </summary>
    internal sealed class SlotStatusPublisher
    {
        private readonly IAdapterMessageSink _sink;
        private readonly IClock _clock;
        private readonly IDiagnosticsLogger _logger;

        /// <summary>本 publisher を生成する。</summary>
        public SlotStatusPublisher(IAdapterMessageSink sink, IClock clock, IDiagnosticsLogger logger)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? new UnityConsoleDiagnosticsLogger();
        }

        /// <summary>
        /// <paramref name="slotId"/> 用の <c>slot/{id}/status</c> を <paramref name="status"/> + <paramref name="detail"/> で publish する。
        /// </summary>
        public void Publish(string slotId, string status, string detail = null)
        {
            try
            {
                var topic = CharacterTopics.SlotStatus(slotId);
                var payload = new SlotStatusPayload { Status = status, Detail = detail };
                _sink.PublishState(topic, payload);
                _logger.Log(AdapterLogLevel.Debug, AdapterLogCategories.Assignment,
                    $"status publish slot={slotId} status={status} detail={detail}");
            }
            catch (Exception ex)
            {
                _logger.Log(AdapterLogLevel.Warning, AdapterLogCategories.Assignment,
                    $"status publish failed slot={slotId} status={status}", ex);
            }
        }
    }
}
