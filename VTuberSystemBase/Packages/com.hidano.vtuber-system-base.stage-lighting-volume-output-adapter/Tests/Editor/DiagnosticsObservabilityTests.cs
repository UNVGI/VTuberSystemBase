#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class DiagnosticsObservabilityTests
    {
        [Test]
        public void Capture_DoesNotMutateUnderlyingState()
        {
            var d = new StageLightingVolumeOutputAdapterDiagnostics();
            d.SetReady(true);
            d.SetLightCount(3);
            var first = d.Capture();
            var second = d.Capture();
            Assert.That(first, Is.EqualTo(second));
            Assert.That(d.IsReady, Is.True);
            Assert.That(d.LightCount, Is.EqualTo(3));
        }

        [Test]
        public void LoggerLevel_ConfigurableAtRuntime()
        {
            var cfg = new AdapterLoggerConfig { MinLevel = AdapterLogLevel.Info };
            var logger = new AdapterLogger(cfg);

            // Info passes through.
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("X.y"));
            logger.Info("X", "y");

            // Switch to Warning.
            cfg.MinLevel = AdapterLogLevel.Warning;
            logger.Info("X", "z"); // suppressed
            LogAssert.NoUnexpectedReceived();
        }
    }
}
