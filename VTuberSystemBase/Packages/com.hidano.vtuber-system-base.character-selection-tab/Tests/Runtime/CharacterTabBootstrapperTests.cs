#nullable enable
using NUnit.Framework;
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Bootstrap;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.CharacterSelectionTab.View;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 6.1 acceptance: bootstrapper composes the dependency graph,
    /// activate/deactivate is idempotent, dispose drains every Track()'d
    /// resource and releases the asset scope.
    /// </summary>
    [TestFixture]
    public sealed class CharacterTabBootstrapperTests
    {
        private static VisualElement BuildRoot()
        {
            var root = new VisualElement { name = ViewQueryHelpers.TabRootName };
            root.Add(new VisualElement { name = ViewQueryHelpers.PresetBarRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.PlayerCardsRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.AvatarCatalogRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.SettingsPanelRegion });
            root.Add(new VisualElement { name = ViewQueryHelpers.DiagnosticsRegion });
            return root;
        }

        [Test]
        public void Construct_AndDispose_ReleasesAssetScopeAndTrackedResources()
        {
            var handle = new FakeTabLifecycleHandle();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus();
            var loader = new FakeAsyncAssetLoader();
            var logger = new FakeDiagnosticsLogger();
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var root = BuildRoot();

            var boot = new CharacterTabBootstrapper(
                handle, cmd, sub, conn, loader, logger, storage, clock, root);

            Assert.Greater(handle.TrackedResourceCount, 0, "Track() should have registered presenters and binder.");
            // The default thumbnail probe issues one load against the loader.
            Assert.Greater(loader.LoadCount, 0);

            boot.Dispose();

            Assert.IsTrue(handle.IsDisposed);
            // ReleaseAll fired for the scope by the lifecycle handle. (FakeAsyncAssetLoader records scope releases.)
            CollectionAssert.Contains(loader.ScopeReleases, "tab:character");
        }

        [Test]
        public void DoubleDispose_IsIdempotent()
        {
            var handle = new FakeTabLifecycleHandle();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus();
            var loader = new FakeAsyncAssetLoader();
            var logger = new FakeDiagnosticsLogger();
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var root = BuildRoot();
            var boot = new CharacterTabBootstrapper(
                handle, cmd, sub, conn, loader, logger, storage, clock, root);

            boot.Dispose();
            Assert.DoesNotThrow(() => boot.Dispose());
        }

        [Test]
        public void DoubleActivate_IsNoOp()
        {
            var handle = new FakeTabLifecycleHandle();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus();
            var loader = new FakeAsyncAssetLoader();
            var logger = new FakeDiagnosticsLogger();
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var root = BuildRoot();
            using var boot = new CharacterTabBootstrapper(
                handle, cmd, sub, conn, loader, logger, storage, clock, root);

            handle.FireActivated();
            handle.FireActivated();
            handle.FireDeactivated();
            handle.FireDeactivated();

            Assert.IsFalse(boot.IsActivated);
        }

        [Test]
        public void InvalidConfig_FallsBackToDefault()
        {
            var handle = new FakeTabLifecycleHandle();
            var cmd = new FakeUiCommandClient();
            var sub = new FakeUiSubscriptionClient();
            var conn = new FakeConnectionStatus();
            var loader = new FakeAsyncAssetLoader();
            var logger = new FakeDiagnosticsLogger();
            var storage = new InMemoryPresetStorage();
            var clock = new ManualClock();
            var root = BuildRoot();
            var bad = new CharacterTabConfig
            {
                PresetDebounce = TimeSpan.Zero,
            };
            using var boot = new CharacterTabBootstrapper(
                handle, cmd, sub, conn, loader, logger, storage, clock, root,
                configOverride: bad);

            bool sawError = false;
            foreach (var e in logger.Entries)
            {
                if (e.Message.Contains("CharacterTabConfig invalid"))
                {
                    sawError = true;
                    break;
                }
            }
            Assert.IsTrue(sawError, "expected error log on invalid config.");
        }
    }
}
