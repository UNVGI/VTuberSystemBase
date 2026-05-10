#nullable enable
using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.Presenters;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.AssetLoading;

using AvatarCatalogEntry = VTuberSystemBase.CharacterSelectionTab.Contracts.AvatarCatalogEntry;
namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 5.2 acceptance: catalog application drives item creation /
    /// thumbnail resolution / fallback styling, and an empty catalog disables
    /// the assignment flow without throwing.
    /// </summary>
    [TestFixture]
    public sealed class AvatarCatalogPresenterTests
    {
        private sealed class StubThumbnailResolver : IAvatarThumbnailResolver
        {
            public sealed class Plan
            {
                public Sprite? Sprite;
                public bool IsFallback;
                public bool Fail;
            }

            public Dictionary<string, Plan> Plans { get; } =
                new Dictionary<string, Plan>(StringComparer.Ordinal);
            public List<string> Released { get; } = new List<string>();

            public void LoadThumbnail(string avatarKey, string scopeId, Action<AvatarThumbnailResult> onCompleted)
            {
                if (Plans.TryGetValue(avatarKey, out var plan))
                {
                    if (plan.Fail || plan.Sprite is null)
                    {
                        onCompleted(AvatarThumbnailResult.Fail(
                            new LoadError(LoadErrorCode.KeyNotFound, avatarKey)));
                    }
                    else
                    {
                        onCompleted(AvatarThumbnailResult.Ok(plan.Sprite, plan.IsFallback));
                    }
                    return;
                }
                onCompleted(AvatarThumbnailResult.Fail(
                    new LoadError(LoadErrorCode.KeyNotFound, avatarKey)));
            }

            public void Release(string avatarKey, string scopeId) { }

            public void ReleaseAll(string scopeId) => Released.Add(scopeId);

            public void Dispose() { }
        }

        private static AvatarCatalogPayload Catalog(params (string key, string name)[] entries)
        {
            var list = new List<AvatarCatalogEntry>();
            foreach (var (k, n) in entries)
                list.Add(new AvatarCatalogEntry { AvatarKey = k, DisplayName = n });
            return new AvatarCatalogPayload { Avatars = list };
        }

        [Test]
        public void Render_BuildsItemsAndAttachesSprites()
        {
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            try
            {
                var resolver = new StubThumbnailResolver();
                resolver.Plans["avatars/alice"] = new StubThumbnailResolver.Plan { Sprite = sprite };
                var store = new CharacterTabStateStore();
                var container = new VisualElement();
                using var presenter = new AvatarCatalogPresenter(
                    store, resolver, container, null, "tab:character");

                store.ApplyAvatarCatalog(Catalog(("avatars/alice", "Alice")));

                Assert.IsTrue(presenter.IsAssignmentEnabled);
                Assert.AreEqual(1, presenter.ItemsForTesting.Count);
                var item = presenter.ItemsForTesting["avatars/alice"];
                Assert.IsFalse(item.ClassListContains(AvatarCatalogPresenter.ItemFallbackClass));
                Assert.IsFalse(item.ClassListContains(AvatarCatalogPresenter.ItemLoadingClass));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void EmptyCatalog_ShowsPlaceholderAndDisablesAssignment()
        {
            var resolver = new StubThumbnailResolver();
            var store = new CharacterTabStateStore();
            var container = new VisualElement();
            using var presenter = new AvatarCatalogPresenter(
                store, resolver, container, null, "tab:character");

            store.ApplyAvatarCatalog(Catalog());

            Assert.IsFalse(presenter.IsAssignmentEnabled);
            Assert.IsNotNull(container.Q<VisualElement>(AvatarCatalogPresenter.EmptyMessageName));
        }

        [Test]
        public void FallbackThumbnail_TogglesFallbackClass()
        {
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            try
            {
                var resolver = new StubThumbnailResolver();
                resolver.Plans["avatars/bob"] = new StubThumbnailResolver.Plan
                { Sprite = sprite, IsFallback = true };
                var store = new CharacterTabStateStore();
                var container = new VisualElement();
                using var presenter = new AvatarCatalogPresenter(
                    store, resolver, container, null, "tab:character");

                store.ApplyAvatarCatalog(Catalog(("avatars/bob", "Bob")));

                Assert.IsTrue(presenter.ItemsForTesting["avatars/bob"]
                    .ClassListContains(AvatarCatalogPresenter.ItemFallbackClass));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void Dispose_ReleasesScope()
        {
            var resolver = new StubThumbnailResolver();
            var store = new CharacterTabStateStore();
            var container = new VisualElement();
            var presenter = new AvatarCatalogPresenter(
                store, resolver, container, null, "tab:character");

            presenter.Dispose();

            Assert.Contains("tab:character", resolver.Released);
        }
    }
}
