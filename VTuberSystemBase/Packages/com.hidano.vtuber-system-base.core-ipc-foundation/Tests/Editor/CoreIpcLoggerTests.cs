#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core.Diagnostics;

namespace VTuberSystemBase.CoreIpc.Tests.Editor
{
    [TestFixture]
    public sealed class CoreIpcLoggerTests
    {
        private readonly struct LogEntry
        {
            public LogEntry(LogLevel level, string message)
            {
                Level = level;
                Message = message;
            }

            public LogLevel Level { get; }

            public string Message { get; }
        }

        private static (CoreIpcLogger Logger, List<LogEntry> Sink) CreateLogger(LogLevel minimumLevel)
        {
            var entries = new List<LogEntry>();
            var logger = new CoreIpcLogger(minimumLevel, (level, message) =>
                entries.Add(new LogEntry(level, message)));
            return (logger, entries);
        }

        [Test]
        public void NullSink_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new CoreIpcLogger(LogLevel.Info, null!));
        }

        [Test]
        public void DefaultMinimumLevel_IsInfo()
        {
            var logger = new CoreIpcLogger();
            Assert.AreEqual(LogLevel.Info, logger.MinimumLevel);
        }

        [Test]
        public void IsEnabled_RespectsMinimumLevel()
        {
            var logger = new CoreIpcLogger(LogLevel.Warning, (_, _) => { });

            Assert.IsFalse(logger.IsEnabled(LogLevel.Trace));
            Assert.IsFalse(logger.IsEnabled(LogLevel.Debug));
            Assert.IsFalse(logger.IsEnabled(LogLevel.Info));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Warning));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Error));
        }

        [Test]
        public void Info_AtInfoLevel_IsEmittedToSink()
        {
            var (logger, sink) = CreateLogger(LogLevel.Info);

            logger.Info("hello");

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(LogLevel.Info, sink[0].Level);
            Assert.AreEqual("hello", sink[0].Message);
        }

        [Test]
        public void TraceAndDebug_AtInfoLevel_AreFilteredOut()
        {
            var (logger, sink) = CreateLogger(LogLevel.Info);

            logger.Trace("t");
            logger.Debug("d");

            Assert.AreEqual(0, sink.Count, "Messages below MinimumLevel must not reach the sink.");
        }

        [Test]
        public void WarningAndError_AtInfoLevel_ReachSinkWithCorrectLevels()
        {
            var (logger, sink) = CreateLogger(LogLevel.Info);

            logger.Warning("w");
            logger.Error("e");

            Assert.AreEqual(2, sink.Count);
            Assert.AreEqual(LogLevel.Warning, sink[0].Level);
            Assert.AreEqual("w", sink[0].Message);
            Assert.AreEqual(LogLevel.Error, sink[1].Level);
            Assert.AreEqual("e", sink[1].Message);
        }

        [Test]
        public void AllConvenienceMethods_AtTraceLevel_RouteToCorrectLevels()
        {
            var (logger, sink) = CreateLogger(LogLevel.Trace);

            logger.Trace("t");
            logger.Debug("d");
            logger.Info("i");
            logger.Warning("w");
            logger.Error("e");

            Assert.AreEqual(5, sink.Count);
            Assert.AreEqual(LogLevel.Trace, sink[0].Level);
            Assert.AreEqual(LogLevel.Debug, sink[1].Level);
            Assert.AreEqual(LogLevel.Info, sink[2].Level);
            Assert.AreEqual(LogLevel.Warning, sink[3].Level);
            Assert.AreEqual(LogLevel.Error, sink[4].Level);
        }

        [Test]
        public void Log_BelowMinimumLevel_IsSuppressed()
        {
            var (logger, sink) = CreateLogger(LogLevel.Error);

            logger.Log(LogLevel.Trace, "t");
            logger.Log(LogLevel.Debug, "d");
            logger.Log(LogLevel.Info, "i");
            logger.Log(LogLevel.Warning, "w");
            logger.Log(LogLevel.Error, "e");

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(LogLevel.Error, sink[0].Level);
            Assert.AreEqual("e", sink[0].Message);
        }

        [Test]
        public void MinimumLevel_RuntimeChange_TakesEffectImmediately()
        {
            var (logger, sink) = CreateLogger(LogLevel.Info);

            logger.Debug("filtered");
            Assert.AreEqual(0, sink.Count, "Debug must be filtered while MinimumLevel=Info.");

            logger.MinimumLevel = LogLevel.Trace;
            logger.Debug("kept");

            Assert.AreEqual(1, sink.Count, "After lowering MinimumLevel to Trace, Debug must reach the sink.");
            Assert.AreEqual(LogLevel.Debug, sink[0].Level);
            Assert.AreEqual("kept", sink[0].Message);

            logger.MinimumLevel = LogLevel.Error;
            logger.Warning("filtered-again");

            Assert.AreEqual(1, sink.Count,
                "After raising MinimumLevel to Error, Warning must be filtered out again.");
        }

        [Test]
        public void NullMessage_IsCoercedToEmptyString()
        {
            var (logger, sink) = CreateLogger(LogLevel.Trace);

            logger.Info(null!);

            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(string.Empty, sink[0].Message);
        }

        [Test]
        public void LogConnectionEvent_BelowMinimumLevel_IsSuppressed()
        {
            var (logger, sink) = CreateLogger(LogLevel.Warning);

            logger.LogConnectionEvent(LogLevel.Info, "connection.starting");

            Assert.AreEqual(0, sink.Count);
        }

        [Test]
        public void LogConnectionEvent_IncludesAllStructuredFields()
        {
            var (logger, sink) = CreateLogger(LogLevel.Trace);

            logger.LogConnectionEvent(
                LogLevel.Info,
                "connection.established",
                kind: MessageKind.Request,
                topic: "ui/light/create",
                correlationId: "abc-123",
                detail: "remote=127.0.0.1:61874");

            Assert.AreEqual(1, sink.Count);
            string message = sink[0].Message;
            StringAssert.StartsWith(CoreIpcLogger.LogTagPrefix, message);
            StringAssert.Contains("connection.established", message);
            StringAssert.Contains("kind=Request", message);
            StringAssert.Contains("topic=ui/light/create", message);
            StringAssert.Contains("correlationId=abc-123", message);
            StringAssert.Contains("remote=127.0.0.1:61874", message);
        }

        [Test]
        public void LogConnectionEvent_OmitsEmptyOptionalFields()
        {
            var (logger, sink) = CreateLogger(LogLevel.Trace);

            logger.LogConnectionEvent(
                LogLevel.Info,
                "connection.starting",
                kind: null,
                topic: null,
                correlationId: null,
                detail: null);

            Assert.AreEqual(1, sink.Count);
            string message = sink[0].Message;
            Assert.AreEqual($"{CoreIpcLogger.LogTagPrefix} connection.starting", message,
                "Optional fields with null/empty values must be omitted from the structured message.");
        }

        [Test]
        public void LogConnectionLifecycleHelpers_EmitAtExpectedLevels()
        {
            var (logger, sink) = CreateLogger(LogLevel.Trace);

            logger.LogConnectionStarting();
            logger.LogConnectionEstablished();
            logger.LogConnectionClosed();
            logger.LogConnectionReconnecting(attempt: 3);

            Assert.AreEqual(4, sink.Count);
            Assert.AreEqual(LogLevel.Info, sink[0].Level);
            StringAssert.Contains("connection.starting", sink[0].Message);
            Assert.AreEqual(LogLevel.Info, sink[1].Level);
            StringAssert.Contains("connection.established", sink[1].Message);
            Assert.AreEqual(LogLevel.Info, sink[2].Level);
            StringAssert.Contains("connection.closed", sink[2].Message);
            Assert.AreEqual(LogLevel.Warning, sink[3].Level);
            StringAssert.Contains("connection.reconnecting", sink[3].Message);
            StringAssert.Contains("attempt=3", sink[3].Message);
        }

        [Test]
        public void LogConnectionReconnecting_BelowWarning_IsFilteredAtInfoMinimum()
        {
            var (logger, sink) = CreateLogger(LogLevel.Error);

            logger.LogConnectionReconnecting(attempt: 1);

            Assert.AreEqual(0, sink.Count,
                "Reconnecting helper logs at Warning, which must be suppressed while MinimumLevel=Error.");
        }

        [Test]
        public void FormatConnectionEvent_StaticHelper_ProducesStableShape()
        {
            string formatted = CoreIpcLogger.FormatConnectionEvent(
                "connection.closed",
                kind: MessageKind.Event,
                topic: "stage/lighting",
                correlationId: null,
                detail: "code=1000");

            Assert.AreEqual(
                $"{CoreIpcLogger.LogTagPrefix} connection.closed kind=Event topic=stage/lighting code=1000",
                formatted);
        }
    }
}
