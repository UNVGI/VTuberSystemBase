#nullable enable
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class AdapterErrorReporterTests
    {
        [Test]
        public void ReportLightError_PublishesEvent_AndUpdatesDiagnostics()
        {
            var sink = new RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag, utcNowUnixMs: () => 12345);

            // Expect the structured error log line.
            LogAssert.Expect(LogType.Error, new Regex("light_error.*topic=light/error.*lightId=abc"));
            reporter.ReportLightError("abc", "internal_error", "msg");

            Assert.That(sink.PublishedEvents.Count, Is.EqualTo(1));
            Assert.That(sink.PublishedEvents[0].Topic, Is.EqualTo(StageLightingTopics.LightError));
            var dto = (LightErrorDto)sink.PublishedEvents[0].Payload!;
            Assert.That(dto.LightId, Is.EqualTo("abc"));
            Assert.That(dto.CorrelationId, Is.EqualTo(string.Empty));
            Assert.That(dto.ErrorCode, Is.EqualTo("internal_error"));
            Assert.That(dto.Message, Is.EqualTo("msg"));

            var snap = diag.Capture();
            Assert.That(snap.LastErrorMessage, Is.EqualTo("msg"));
            Assert.That(snap.LastErrorAtUnixMs, Is.EqualTo(12345));
        }

        [Test]
        public void ReportStageLoadFailed_PublishesEvent_AndUpdatesDiagnostics()
        {
            var sink = new RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag, utcNowUnixMs: () => 99);

            LogAssert.Expect(LogType.Error, new Regex("stage_load_failed.*topic=stage/load-failed"));
            reporter.ReportStageLoadFailed("Stages/Default", "not_found", "bad key");

            Assert.That(sink.PublishedEvents.Count, Is.EqualTo(1));
            Assert.That(sink.PublishedEvents[0].Topic, Is.EqualTo(StageLightingTopics.StageLoadFailed));
            var dto = (StageLoadFailedDto)sink.PublishedEvents[0].Payload!;
            Assert.That(dto.AddressableKey, Is.EqualTo("Stages/Default"));
            Assert.That(dto.ErrorCode, Is.EqualTo("not_found"));
            Assert.That(dto.Message, Is.EqualTo("bad key"));

            var snap = diag.Capture();
            Assert.That(snap.LastErrorMessage, Is.EqualTo("bad key"));
            Assert.That(snap.LastErrorAtUnixMs, Is.EqualTo(99));
        }

        [Test]
        public void ReportLightError_NullLightId_IsAccepted()
        {
            var sink = new RecordingMessageSink();
            var logger = new AdapterLogger();
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var reporter = new AdapterErrorReporter(sink, logger, diag);
            LogAssert.Expect(LogType.Error, new Regex(".+"));
            reporter.ReportLightError(null, "limit_exceeded", "too many");
            var dto = (LightErrorDto)sink.PublishedEvents[0].Payload!;
            Assert.That(dto.LightId, Is.Null);
            Assert.That(dto.ErrorCode, Is.EqualTo("limit_exceeded"));
        }
    }
}
