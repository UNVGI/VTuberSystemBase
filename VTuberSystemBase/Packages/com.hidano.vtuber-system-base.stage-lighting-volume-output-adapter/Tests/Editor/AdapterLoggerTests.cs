#nullable enable
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class AdapterLoggerTests
    {
        [Test]
        public void Info_AtDefaultLevel_LogsToConsoleWithExpectedFormat()
        {
            var logger = new AdapterLogger();
            // Default MinLevel = Info -> Info is emitted.
            LogAssert.Expect(LogType.Log, new Regex(@"^\[StageLightingVolumeOutputAdapter\] StageHandler\.swap_started: ctx topic=stage/active$"));
            logger.Info("StageHandler", "swap_started", context: "ctx", topic: "stage/active");
        }

        [Test]
        public void Verbose_AtDefaultLevel_IsSuppressed()
        {
            var logger = new AdapterLogger();
            // No expected log -> should not emit.
            logger.Verbose("StageHandler", "tick");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Info_WhenMinLevelIsWarning_IsSuppressed()
        {
            var cfg = new AdapterLoggerConfig { MinLevel = AdapterLogLevel.Warning };
            var logger = new AdapterLogger(cfg);
            logger.Info("X", "y");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Warning_AtWarningLevel_LogsAsWarning()
        {
            var cfg = new AdapterLoggerConfig { MinLevel = AdapterLogLevel.Warning };
            var logger = new AdapterLogger(cfg);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[StageLightingVolumeOutputAdapter\] LightHandler\.unknown_id: missing lightId=abc$"));
            logger.Warning("LightHandler", "unknown_id", context: "missing", lightId: "abc");
        }

        [Test]
        public void Error_AlwaysLogsAsError_AndIncludesStructuredFields()
        {
            var logger = new AdapterLogger();
            LogAssert.Expect(LogType.Error, new Regex(@"^\[StageLightingVolumeOutputAdapter\] VolumeHandler\.apply_failed: bad type=Foo\.Bar param=intensity exception=InvalidOperationException:boom$"));
            logger.Error("VolumeHandler", "apply_failed", context: "bad",
                typeFullName: "Foo.Bar", paramName: "intensity",
                exception: new System.InvalidOperationException("boom"));
        }

        [Test]
        public void NullStructuredFields_AreOmitted()
        {
            var logger = new AdapterLogger();
            LogAssert.Expect(LogType.Log, new Regex(@"^\[StageLightingVolumeOutputAdapter\] X\.y$"));
            logger.Info("X", "y");
        }
    }
}
