#nullable enable
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VTuberSystemBase.CharacterSelectionTab.Bootstrap;
using VTuberSystemBase.CharacterSelectionTab.Services;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.CharacterSelectionTab.Tests.TestDoubles;
using VTuberSystemBase.CharacterSelectionTab.View;

namespace VTuberSystemBase.CharacterSelectionTab.Tests
{
    /// <summary>
    /// Task 6.2 acceptance: PresetFlushHook.FlushNowAsync flushes pending edits
    /// idempotently. PlayMode iteration is exercised by re-constructing /
    /// disposing the bootstrapper 5 times and asserting no resource leaks.
    /// </summary>
    [TestFixture]
    public sealed class PresetFlushHookTests
    {
        [Test]
        public async Task FlushNowAsync_PersistsDirtyChanges()
        {
            var clock = new ManualClock();
            var storage = new InMemoryPresetStorage();
            var logic = new PresetStoreLogic(storage, clock, TimeSpan.FromMilliseconds(500));
            await logic.InitializeAsync(CancellationToken.None);
            var created = await logic.CreateAsync("A");
            Assert.IsTrue(created.Success);
            await logic.SetActiveAsync(created.PresetId!);
            using var hook = new PresetFlushHook(logic);
            logic.MarkSlotAssignmentChanged("slot-01", "avatars/alice");

            await hook.FlushNowAsync();

            Assert.IsNotNull(logic.LastSavedAt);
            // Idempotent: second flush should not throw.
            Assert.DoesNotThrowAsync(async () => await hook.FlushNowAsync());
        }

        [Test]
        public async Task BootstrapperIteration_5Times_NoResourceLeak()
        {
            for (int i = 0; i < 5; i++)
            {
                var handle = new FakeTabLifecycleHandle();
                var cmd = new FakeUiCommandClient();
                var sub = new FakeUiSubscriptionClient();
                var conn = new FakeConnectionStatus();
                var loader = new FakeAsyncAssetLoader();
                var logger = new FakeDiagnosticsLogger();
                var storage = new InMemoryPresetStorage();
                var clock = new ManualClock();
                var root = new VisualElement { name = ViewQueryHelpers.TabRootName };
                root.Add(new VisualElement { name = ViewQueryHelpers.PresetBarRegion });
                root.Add(new VisualElement { name = ViewQueryHelpers.PlayerCardsRegion });
                root.Add(new VisualElement { name = ViewQueryHelpers.AvatarCatalogRegion });
                root.Add(new VisualElement { name = ViewQueryHelpers.SettingsPanelRegion });
                root.Add(new VisualElement { name = ViewQueryHelpers.DiagnosticsRegion });

                var boot = new CharacterTabBootstrapper(
                    handle, cmd, sub, conn, loader, logger, storage, clock, root);
                handle.FireActivated();
                handle.FireDeactivated();
                boot.Dispose();

                Assert.IsTrue(handle.IsDisposed, $"iteration {i}: handle must be disposed.");
                Assert.AreEqual(0, handle.TrackedResourceCount,
                    $"iteration {i}: tracked resources must be drained on Dispose.");
                Assert.Contains("tab:character", loader.ScopeReleases,
                    $"iteration {i}: loader scope must be released.");
            }
            await Task.CompletedTask;
        }
    }
}
