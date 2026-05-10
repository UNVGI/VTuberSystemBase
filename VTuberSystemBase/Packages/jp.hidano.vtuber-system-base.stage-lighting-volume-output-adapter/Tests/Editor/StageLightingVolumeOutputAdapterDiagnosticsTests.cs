#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    public sealed class StageLightingVolumeOutputAdapterDiagnosticsTests
    {
        [Test]
        public void Defaults_AreEmpty()
        {
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            var snap = diag.Capture();
            Assert.That(snap.IsReady, Is.False);
            Assert.That(snap.RegisteredHandlerCount, Is.EqualTo(0));
            Assert.That(snap.CurrentStageAddressableKey, Is.Null);
            Assert.That(snap.LightCount, Is.EqualTo(0));
            Assert.That(snap.VolumeOverrideTypeCount, Is.EqualTo(0));
            Assert.That(snap.PreviewHostReady, Is.False);
            Assert.That(snap.LastErrorMessage, Is.Null);
            Assert.That(snap.LastErrorAtUnixMs, Is.EqualTo(0));
        }

        [Test]
        public void Setters_UpdateProperties_AndCaptureReturnsSameValues()
        {
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            diag.SetReady(true);
            diag.SetRegisteredHandlerCount(7);
            diag.SetLightCount(3);
            diag.SetVolumeOverrideTypeCount(31);
            diag.SetPreviewHostReady(true);
            diag.SetCurrentStageAddressableKey("Stages/Default");
            diag.RecordError("oops", atUnixMs: 1234567890123);

            var snap = diag.Capture();
            Assert.That(snap.IsReady, Is.True);
            Assert.That(snap.RegisteredHandlerCount, Is.EqualTo(7));
            Assert.That(snap.LightCount, Is.EqualTo(3));
            Assert.That(snap.VolumeOverrideTypeCount, Is.EqualTo(31));
            Assert.That(snap.PreviewHostReady, Is.True);
            Assert.That(snap.CurrentStageAddressableKey, Is.EqualTo("Stages/Default"));
            Assert.That(snap.LastErrorMessage, Is.EqualTo("oops"));
            Assert.That(snap.LastErrorAtUnixMs, Is.EqualTo(1234567890123));

            // Properties should also reflect updates without going via Capture.
            Assert.That(diag.IsReady, Is.True);
            Assert.That(diag.RegisteredHandlerCount, Is.EqualTo(7));
            Assert.That(diag.CurrentStageAddressableKey, Is.EqualTo("Stages/Default"));
        }

        [Test]
        public void IncrementHandlerCount_AddsToCurrentValue()
        {
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            diag.IncrementHandlerCount(2);
            diag.IncrementHandlerCount();
            Assert.That(diag.RegisteredHandlerCount, Is.EqualTo(3));
        }

        [Test]
        public void Capture_IsImmutableSnapshot()
        {
            var diag = new StageLightingVolumeOutputAdapterDiagnostics();
            diag.SetLightCount(10);
            var snap = diag.Capture();
            diag.SetLightCount(0);
            // snap retains the old value.
            Assert.That(snap.LightCount, Is.EqualTo(10));
            Assert.That(diag.LightCount, Is.EqualTo(0));
        }
    }
}
