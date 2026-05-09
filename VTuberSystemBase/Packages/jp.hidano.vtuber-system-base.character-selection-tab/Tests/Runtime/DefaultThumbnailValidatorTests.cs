#nullable enable
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.CharacterSelectionTab.View;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 4.3 acceptance: the bootstrap probe surfaces a missing default
    /// thumbnail as an Error log and signals the failure via the completion
    /// callback so the bootstrapper can warn integrators.
    /// </summary>
    [TestFixture]
    public sealed class DefaultThumbnailValidatorTests
    {
        private const string DefaultKey =
            "vtuber-system-base/character/default-avatar-thumbnail";

        [Test]
        public void Probe_LogsErrorWhenKeyMissing()
        {
            var loader = new FakeAsyncAssetLoader();
            loader.RegisterFailure(DefaultKey, LoadErrorCode.KeyNotFound);
            var logger = new FakeDiagnosticsLogger();

            bool? outcome = null;
            DefaultThumbnailValidator.ValidateAsync(
                loader, DefaultKey, "tab:character", logger, ok => outcome = ok);

            Assert.AreEqual(false, outcome);
            bool sawError = false;
            foreach (var entry in logger.Entries)
            {
                if (entry.Level == LogLevel.Error
                    && entry.Category == LogCategory.AssetLoad
                    && entry.Message.Contains("DefaultThumbnail.Probe failed"))
                {
                    sawError = true;
                    break;
                }
            }
            Assert.IsTrue(sawError, "expected DefaultThumbnail.Probe failure entry.");
        }

        [Test]
        public void Probe_ReleasesProbeScopeOnSuccess()
        {
            var sprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f));
            try
            {
                var loader = new FakeAsyncAssetLoader();
                loader.RegisterAsset(DefaultKey, sprite);
                var logger = new FakeDiagnosticsLogger();

                bool? outcome = null;
                DefaultThumbnailValidator.ValidateAsync(
                    loader, DefaultKey, "tab:character", logger, ok => outcome = ok);

                Assert.AreEqual(true, outcome);
                Assert.Contains("tab:character:default-thumbnail-probe", loader.ScopeReleases);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
            }
        }
    }
}
