#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    [TestFixture]
    public sealed class AvatarThumbnailResolverTests
    {
        private const string DefaultKey = "vsb/default-thumbnail";

        private static Sprite MakeSprite()
        {
            var tex = new Texture2D(1, 1);
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        }

        [Test]
        public void Hit_ReturnsSpriteAndIsFallbackFalse()
        {
            var loader = new FakeAsyncAssetLoader();
            var sprite = MakeSprite();
            try
            {
                loader.RegisterAsset("avatars/alice.thumbnail", sprite);
                var resolver = new AvatarThumbnailResolver(loader, DefaultKey);

                AvatarThumbnailResult got = default;
                resolver.LoadThumbnail("avatars/alice", "tab:character", r => got = r);

                Assert.IsTrue(got.Success);
                Assert.IsFalse(got.IsFallback);
                Assert.AreSame(sprite, got.Sprite);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void Miss_FallsBackToDefaultAndLogs()
        {
            var loader = new FakeAsyncAssetLoader();
            var defaultSprite = MakeSprite();
            try
            {
                loader.RegisterAsset(DefaultKey, defaultSprite);
                var log = new FakeDiagnosticsLogger();
                var resolver = new AvatarThumbnailResolver(loader, DefaultKey, log);

                AvatarThumbnailResult got = default;
                resolver.LoadThumbnail("avatars/missing", "tab:character", r => got = r);

                Assert.IsTrue(got.Success, "expected fallback success");
                Assert.IsTrue(got.IsFallback);
                Assert.AreSame(defaultSprite, got.Sprite);
                Assert.IsTrue(log.Entries.Count > 0);
                Assert.AreEqual(LogCategory.AssetLoad, log.Entries[0].Category);
            }
            finally
            {
                Object.DestroyImmediate(defaultSprite);
            }
        }

        [Test]
        public void DefaultMissing_ReturnsFailedResult()
        {
            var loader = new FakeAsyncAssetLoader();
            // No RegisterAsset for either main or default → both fail.
            var resolver = new AvatarThumbnailResolver(loader, DefaultKey);

            AvatarThumbnailResult got = default;
            resolver.LoadThumbnail("avatars/x", "tab:character", r => got = r);
            Assert.IsFalse(got.Success);
            Assert.IsNotNull(got.Error);
        }

        [Test]
        public void ReleaseAll_DelegatesToLoader()
        {
            var loader = new FakeAsyncAssetLoader();
            var resolver = new AvatarThumbnailResolver(loader, DefaultKey);
            resolver.ReleaseAll("tab:character");
            Assert.Contains("tab:character", loader.ScopeReleases);
        }
    }
}
