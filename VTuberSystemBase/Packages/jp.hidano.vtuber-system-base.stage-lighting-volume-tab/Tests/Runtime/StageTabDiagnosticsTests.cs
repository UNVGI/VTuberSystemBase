#nullable enable
using System;
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Diagnostics;
using VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the diagnostics surface implemented by <see cref="StageTabDiagnostics"/>
    /// (Task 3.5, Requirements 10.1-10.8). All log paths must flow through the
    /// shell-side <see cref="IDiagnosticsLogger"/> so they never reach the main
    /// output (Display 2+).
    /// </summary>
    [TestFixture]
    public sealed class StageTabDiagnosticsTests
    {
        [Test]
        public void LogInitializationPhase_RoutesToLoggerWithTabSpecCategory()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);

            sut.LogInitializationPhase("preset-load", success: true);

            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            Assert.That(logger.Entries[0].Category, Is.EqualTo(LogCategory.TabSpec));
            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Info));
            StringAssert.Contains("preset-load", logger.Entries[0].Message);
        }

        [Test]
        public void LogInitializationPhase_FailureUsesErrorLevel()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);

            sut.LogInitializationPhase("preset-load", success: false, error: "boom");

            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Error));
            StringAssert.Contains("boom", logger.Entries[0].Message);
        }

        [Test]
        public void LogCommandSent_TracksTopicAndKind()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);

            sut.LogCommandSent("light/command", "event");

            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            StringAssert.Contains("light/command", logger.Entries[0].Message);
            StringAssert.Contains("event", logger.Entries[0].Message);
        }

        [Test]
        public void LogEventReceived_TracksCorrelationIdWhenPresent()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);

            sut.LogEventReceived("light/added", correlationId: "cid-1");

            StringAssert.Contains("cid-1", logger.Entries[0].Message);
        }

        [Test]
        public void LogAssetLoadFailure_UsesWarningLevelAndAddressableContext()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);

            sut.LogAssetLoadFailure("stages/missing", "KeyNotFound");

            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            StringAssert.Contains("stages/missing", logger.Entries[0].Message);
            StringAssert.Contains("KeyNotFound", logger.Entries[0].Message);
        }

        [Test]
        public void LogPersistenceFailure_UsesErrorLevel()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);

            sut.LogPersistenceFailure("save", "DiskFull");

            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Error));
            StringAssert.Contains("save", logger.Entries[0].Message);
            StringAssert.Contains("DiskFull", logger.Entries[0].Message);
        }

        [Test]
        public void GetSnapshot_ReturnsDefaults_WhenNothingHasBeenSetYet()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);

            var snapshot = sut.GetSnapshot();

            Assert.That(snapshot.ActivePresetName, Is.Null);
            Assert.That(snapshot.CurrentStageKey, Is.Null);
            Assert.That(snapshot.LightCount, Is.EqualTo(0));
            Assert.That(snapshot.LightsInErrorState, Is.EqualTo(0));
            Assert.That(snapshot.VolumeOverridesEnabled, Is.EqualTo(0));
            Assert.That(snapshot.PendingAsyncLoads, Is.EqualTo(0));
            Assert.That(snapshot.LastPersistenceSaveAt, Is.Null);
            Assert.That(snapshot.IpcConnected, Is.False);
        }

        [Test]
        public void GetSnapshot_ReflectsValuesPushedThroughSetters()
        {
            var logger = new FakeDiagnosticsLogger();
            var sut = new StageTabDiagnostics(logger);
            var savedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

            sut.SetActivePresetName("Daylight");
            sut.SetCurrentStageKey("stages/concert-hall");
            sut.SetLightCount(7);
            sut.SetLightsInErrorState(1);
            sut.SetVolumeOverridesEnabled(2);
            sut.SetPendingAsyncLoads(3);
            sut.RecordPersistenceSave(savedAt);
            sut.SetIpcConnected(true);

            var snapshot = sut.GetSnapshot();
            Assert.That(snapshot.ActivePresetName, Is.EqualTo("Daylight"));
            Assert.That(snapshot.CurrentStageKey, Is.EqualTo("stages/concert-hall"));
            Assert.That(snapshot.LightCount, Is.EqualTo(7));
            Assert.That(snapshot.LightsInErrorState, Is.EqualTo(1));
            Assert.That(snapshot.VolumeOverridesEnabled, Is.EqualTo(2));
            Assert.That(snapshot.PendingAsyncLoads, Is.EqualTo(3));
            Assert.That(snapshot.LastPersistenceSaveAt, Is.EqualTo(savedAt));
            Assert.That(snapshot.IpcConnected, Is.True);
        }

        [Test]
        public void Logger_MinimumLevelFiltersTraceAndDebug()
        {
            // Confirms Req 10.7 (log level externally configurable via shell logger).
            var logger = new FakeDiagnosticsLogger { MinimumLevel = LogLevel.Warning };
            var sut = new StageTabDiagnostics(logger);

            sut.LogInitializationPhase("setup", success: true); // Info, dropped
            sut.LogPersistenceFailure("save", "boom");          // Error, kept

            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Error));
        }
    }
}
