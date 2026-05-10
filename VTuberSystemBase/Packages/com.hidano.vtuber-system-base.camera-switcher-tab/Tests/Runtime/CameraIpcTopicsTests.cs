#nullable enable
using NUnit.Framework;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Tests
{
    /// <summary>
    /// Task 1.2 acceptance test: <see cref="CameraIpcTopics"/> produces the topic
    /// strings declared in design.md without typos and the dynamic builders apply
    /// the same Safe encoding policy used by character-selection-tab's
    /// CharacterTopics.
    /// </summary>
    [TestFixture]
    public sealed class CameraIpcTopicsTests
    {
        [Test]
        public void StaticTopics_MatchDesign()
        {
            Assert.AreEqual("camera/command", CameraIpcTopics.CameraCommand);
            Assert.AreEqual("cameras/list", CameraIpcTopics.CamerasList);
            Assert.AreEqual("cameras/active", CameraIpcTopics.CamerasActive);
            Assert.AreEqual("camera/created", CameraIpcTopics.CameraCreated);
            Assert.AreEqual("camera/error", CameraIpcTopics.CameraError);
            Assert.AreEqual("camera/preset/command", CameraIpcTopics.PresetCommand);
            Assert.AreEqual("camera/preset/list", CameraIpcTopics.PresetList);
            Assert.AreEqual("camera/preset/active", CameraIpcTopics.PresetActive);
            Assert.AreEqual("camera/preview/command", CameraIpcTopics.PreviewCommand);
        }

        [Test]
        public void DynamicTopics_BuildExpectedPaths()
        {
            Assert.AreEqual("camera/cam-a/metadata/displayName",
                CameraIpcTopics.CameraMetadata("cam-a", CameraMetadataKeys.DisplayName));
            Assert.AreEqual("camera/cam-a/metadata/",
                CameraIpcTopics.CameraMetadataPrefix("cam-a"));
            Assert.AreEqual("camera/cam-a/volume/command",
                CameraIpcTopics.VolumeCommand("cam-a"));
            Assert.AreEqual("camera/cam-a/volume/enabled",
                CameraIpcTopics.VolumeEnabled("cam-a"));
            Assert.AreEqual("camera/cam-a/volume/override/Bloom/enabled",
                CameraIpcTopics.VolumeOverrideEnabled("cam-a", "Bloom"));
            Assert.AreEqual("camera/cam-a/volume/override/Bloom/intensity",
                CameraIpcTopics.VolumeOverrideParam("cam-a", "Bloom", "intensity"));
            Assert.AreEqual("camera/cam-a/volume/overrides",
                CameraIpcTopics.VolumeOverridesList("cam-a"));
            Assert.AreEqual("camera/cam-a/volume/overrides/metadata",
                CameraIpcTopics.VolumeOverridesMetadata("cam-a"));
            Assert.AreEqual("camera/cam-a/preview/handle",
                CameraIpcTopics.PreviewHandle("cam-a"));
        }

        [Test]
        public void CameraIdOverloads_SkipReencoding()
        {
            var id = new CameraId("cam_42-x");
            Assert.AreEqual("camera/cam_42-x/volume/command",
                CameraIpcTopics.VolumeCommand(id));
            Assert.AreEqual("camera/cam_42-x/preview/handle",
                CameraIpcTopics.PreviewHandle(id));
        }

        [Test]
        public void Safe_PercentEncodesUnsafeCharacters()
        {
            // '/' is the topic-segment delimiter and must not appear unescaped.
            var encoded = CameraIpcTopics.Safe("with/slash");
            Assert.IsFalse(encoded.Contains("/"));
        }

        [Test]
        public void Safe_KeepsAllowedCharactersIdempotent()
        {
            const string id = "cam-1.alpha_beta";
            Assert.AreEqual(id, CameraIpcTopics.Safe(id));
        }
    }
}
