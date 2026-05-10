#nullable enable
using NUnit.Framework;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests
{
    /// <summary>
    /// Locks the topic strings in <see cref="StageLightingTopics"/> so a typo on either
    /// the UI side or the main-output adapter side is caught at build time. Topic
    /// values mirror the contract documented in design.md §Contracts §StageLightingTopics
    /// (Task 1.3, Requirement 1.7).
    /// </summary>
    [TestFixture]
    public sealed class StageLightingTopicsTests
    {
        [Test]
        public void StageTopics_HaveExactStringValues()
        {
            Assert.That(StageLightingTopics.StageCatalog, Is.EqualTo("stage/catalog"));
            Assert.That(StageLightingTopics.StageCurrent, Is.EqualTo("stage/current"));
            Assert.That(StageLightingTopics.StageCommand, Is.EqualTo("stage/command"));
            Assert.That(StageLightingTopics.StageLoaded, Is.EqualTo("stage/loaded"));
            Assert.That(StageLightingTopics.StageLoadFailed, Is.EqualTo("stage/load-failed"));
        }

        [Test]
        public void LightTopics_HaveExactStringValues()
        {
            Assert.That(StageLightingTopics.LightsList, Is.EqualTo("lights/list"));
            Assert.That(StageLightingTopics.LightCommand, Is.EqualTo("light/command"));
            Assert.That(StageLightingTopics.LightAdded, Is.EqualTo("light/added"));
            Assert.That(StageLightingTopics.LightError, Is.EqualTo("light/error"));
        }

        [Test]
        public void LightProperty_FormatsExpectedTopic()
        {
            const string lightId = "abc123";
            Assert.That(
                StageLightingTopics.LightProperty(lightId, StageLightingTopics.PropertyIntensity),
                Is.EqualTo("light/abc123/intensity"));
            Assert.That(
                StageLightingTopics.LightProperty(lightId, StageLightingTopics.PropertyColor),
                Is.EqualTo("light/abc123/color"));
            Assert.That(
                StageLightingTopics.LightProperty(lightId, StageLightingTopics.PropertyRotation),
                Is.EqualTo("light/abc123/rotation"));
            Assert.That(
                StageLightingTopics.LightProperty(lightId, StageLightingTopics.PropertyType),
                Is.EqualTo("light/abc123/type"));
            Assert.That(
                StageLightingTopics.LightProperty(lightId, StageLightingTopics.PropertyRange),
                Is.EqualTo("light/abc123/range"));
            Assert.That(
                StageLightingTopics.LightProperty(lightId, StageLightingTopics.PropertySpotAngle),
                Is.EqualTo("light/abc123/spotAngle"));
            Assert.That(
                StageLightingTopics.LightProperty(lightId, StageLightingTopics.PropertyDisplayName),
                Is.EqualTo("light/abc123/displayName"));
        }

        [Test]
        public void LightPropertyConstants_HaveExactStringValues()
        {
            Assert.That(StageLightingTopics.PropertyIntensity, Is.EqualTo("intensity"));
            Assert.That(StageLightingTopics.PropertyColor, Is.EqualTo("color"));
            Assert.That(StageLightingTopics.PropertyRotation, Is.EqualTo("rotation"));
            Assert.That(StageLightingTopics.PropertyType, Is.EqualTo("type"));
            Assert.That(StageLightingTopics.PropertyRange, Is.EqualTo("range"));
            Assert.That(StageLightingTopics.PropertySpotAngle, Is.EqualTo("spotAngle"));
            Assert.That(StageLightingTopics.PropertyDisplayName, Is.EqualTo("displayName"));
        }

        [Test]
        public void VolumeOverrideTopics_FormatExpectedDynamicStrings()
        {
            Assert.That(
                StageLightingTopics.VolumeOverrideSchema,
                Is.EqualTo("volume/override/schema"));
            Assert.That(
                StageLightingTopics.VolumeOverrideEnabled("UnityEngine.Rendering.Universal.Bloom"),
                Is.EqualTo("volume/override/UnityEngine.Rendering.Universal.Bloom/enabled"));
            Assert.That(
                StageLightingTopics.VolumeOverrideParam("UnityEngine.Rendering.Universal.Bloom", "intensity"),
                Is.EqualTo("volume/override/UnityEngine.Rendering.Universal.Bloom/intensity"));
            Assert.That(
                StageLightingTopics.VolumeCommand,
                Is.EqualTo("volume/command"));
        }

        [Test]
        public void PreviewTopics_HaveExactStringValues()
        {
            Assert.That(StageLightingTopics.PreviewCommand, Is.EqualTo("preview/command"));
            Assert.That(StageLightingTopics.PreviewState, Is.EqualTo("preview/state"));
        }
    }
}
