#nullable enable
using System;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics
{
    /// <summary>
    /// Centralizes error event publication so handlers do not have to wire DTO construction,
    /// logging, and diagnostics updates individually. Routes <c>light/error</c> and
    /// <c>stage/load-failed</c> messages through the injected
    /// <see cref="IAdapterMessageSink"/>, mirrors the failure to <see cref="AdapterLogger"/>,
    /// and stamps the latest error onto <see cref="StageLightingVolumeOutputAdapterDiagnostics"/>.
    /// </summary>
    internal sealed class AdapterErrorReporter
    {
        private readonly IAdapterMessageSink _sink;
        private readonly AdapterLogger _logger;
        private readonly StageLightingVolumeOutputAdapterDiagnostics _diagnostics;
        private readonly Func<long> _utcNowUnixMs;

        public AdapterErrorReporter(
            IAdapterMessageSink sink,
            AdapterLogger logger,
            StageLightingVolumeOutputAdapterDiagnostics diagnostics,
            Func<long>? utcNowUnixMs = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _utcNowUnixMs = utcNowUnixMs ?? DefaultUtcNowUnixMs;
        }

        public void ReportLightError(string? lightId, string errorCode, string message)
        {
            errorCode ??= "internal_error";
            message ??= string.Empty;
            var dto = new LightErrorDto(LightId: lightId, CorrelationId: string.Empty, ErrorCode: errorCode, Message: message);
            _sink.PublishEvent(StageLightingTopics.LightError, dto);
            _logger.Error("AdapterErrorReporter", "light_error", context: message,
                topic: StageLightingTopics.LightError, lightId: lightId);
            _diagnostics.RecordError(message, _utcNowUnixMs());
        }

        public void ReportStageLoadFailed(string addressableKey, string errorCode, string message)
        {
            addressableKey ??= string.Empty;
            errorCode ??= "load_failed";
            message ??= string.Empty;
            var dto = new StageLoadFailedDto(AddressableKey: addressableKey, ErrorCode: errorCode, Message: message);
            _sink.PublishEvent(StageLightingTopics.StageLoadFailed, dto);
            _logger.Error("AdapterErrorReporter", "stage_load_failed", context: message,
                topic: StageLightingTopics.StageLoadFailed);
            _diagnostics.RecordError(message, _utcNowUnixMs());
        }

        private static long DefaultUtcNowUnixMs()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
