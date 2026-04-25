#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    [TestFixture]
    public sealed class FakeAsyncAssetLoaderSmokeTests
    {
        private readonly List<Object> spawnedAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in spawnedAssets)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();
        }

        private Texture2D NewTexture(string name)
        {
            var tex = new Texture2D(1, 1) { name = name };
            spawnedAssets.Add(tex);
            return tex;
        }

        [Test]
        public void LoadAsync_ImmediateMode_RegisteredAsset_DeliversSuccessResultSynchronously()
        {
            var loader = new FakeAsyncAssetLoader { Mode = FakeAsyncAssetLoader.CompletionMode.Immediate };
            var registered = NewTexture("character/avatar/main");
            loader.RegisterAsset("character/avatar/main", registered);

            AssetLoadResult<Texture2D>? observed = null;
            var handle = loader.LoadAsync<Texture2D>(
                "character/avatar/main",
                "character-tab",
                result => observed = result);

            Assert.That(observed.HasValue, Is.True, "Immediate mode must invoke callback before LoadAsync returns");
            Assert.That(observed!.Value.Success, Is.True);
            Assert.That(observed.Value.Asset, Is.SameAs(registered));
            Assert.That(observed.Value.Error, Is.Null);
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Completed));
            Assert.That(loader.CompletedCount, Is.EqualTo(1));
            Assert.That(loader.FailedCount, Is.EqualTo(0));
        }

        [Test]
        public void LoadAsync_DeferredMode_FailWith_InjectsFailureOnExplicitTrigger()
        {
            var loader = new FakeAsyncAssetLoader { Mode = FakeAsyncAssetLoader.CompletionMode.Deferred };

            AssetLoadResult<Texture2D>? observed = null;
            var handle = loader.LoadAsync<Texture2D>(
                "stage/lighting/missing",
                "stage-tab",
                result => observed = result);

            Assert.That(observed.HasValue, Is.False, "Deferred mode must not invoke callback before explicit resolution");
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Pending));
            Assert.That(loader.PendingHandles, Has.Count.EqualTo(1));

            var injected = new LoadError(
                LoadErrorCode.KeyNotFound,
                handle.AddressableKey,
                "Injected by test");
            var dispatched = loader.FailWith(handle, injected);

            Assert.That(dispatched, Is.True);
            Assert.That(observed.HasValue, Is.True);
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Asset, Is.Null);
            Assert.That(observed.Value.Error.HasValue, Is.True);
            Assert.That(observed.Value.Error!.Value.Code, Is.EqualTo(LoadErrorCode.KeyNotFound));
            Assert.That(observed.Value.Error.Value.AddressableKey, Is.EqualTo("stage/lighting/missing"));
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Failed));
            Assert.That(loader.FailedCount, Is.EqualTo(1));
            Assert.That(loader.CompletedCount, Is.EqualTo(0));
        }

        [Test]
        public void Cancel_PendingHandle_DispatchesCancelledErrorAndUpdatesCounters()
        {
            var loader = new FakeAsyncAssetLoader { Mode = FakeAsyncAssetLoader.CompletionMode.Deferred };

            AssetLoadResult<Texture2D>? observed = null;
            var handle = loader.LoadAsync<Texture2D>(
                "camera/preview/render",
                "camera-tab",
                result => observed = result);

            handle.Cancel();

            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Cancelled));
            Assert.That(observed.HasValue, Is.True);
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Error.HasValue, Is.True);
            Assert.That(observed.Value.Error!.Value.Code, Is.EqualTo(LoadErrorCode.Cancelled));
            Assert.That(loader.CancelledCount, Is.EqualTo(1));
            Assert.That(loader.PendingHandles, Has.Count.EqualTo(0));

            // Cancel after settle is a no-op (callback must not fire twice).
            int callCount = 0;
            AssetLoadResult<Texture2D>? secondObserved = null;
            var second = loader.LoadAsync<Texture2D>(
                "camera/preview/render2",
                "camera-tab",
                result =>
                {
                    callCount++;
                    secondObserved = result;
                });
            loader.FailWith(second, new LoadError(LoadErrorCode.IoFailure, second.AddressableKey, "boom"));
            Assert.That(callCount, Is.EqualTo(1));
            second.Cancel();
            Assert.That(callCount, Is.EqualTo(1), "Cancel after the handle has settled must be a no-op");
            Assert.That(secondObserved!.Value.Error!.Value.Code, Is.EqualTo(LoadErrorCode.IoFailure));
        }

        [Test]
        public void GetSnapshot_ReflectsLiveCounters_AndSnapshotOverrideTakesPrecedence()
        {
            var loader = new FakeAsyncAssetLoader { Mode = FakeAsyncAssetLoader.CompletionMode.Deferred };

            var h1 = loader.LoadAsync<Texture2D>("a", "scope-x", _ => { });
            var h2 = loader.LoadAsync<Texture2D>("b", "scope-x", _ => { });
            var h3 = loader.LoadAsync<Texture2D>("c", "scope-y", _ => { });

            var live = loader.GetSnapshot();
            Assert.That(live.PendingCount, Is.EqualTo(3));
            Assert.That(live.PendingByScope["scope-x"], Is.EqualTo(2));
            Assert.That(live.PendingByScope["scope-y"], Is.EqualTo(1));
            Assert.That(live.CompletedCount, Is.EqualTo(0));
            Assert.That(live.FailedCount, Is.EqualTo(0));

            // Settle one to push counters off zero.
            loader.CompleteWith(h1, NewTexture("a"));
            var afterOne = loader.GetSnapshot();
            Assert.That(afterOne.CompletedCount, Is.EqualTo(1));
            Assert.That(afterOne.PendingCount, Is.EqualTo(2));

            // Override replaces the live snapshot entirely.
            var overridden = new AssetLoaderSnapshot(
                pendingCount: 99,
                completedCount: 7,
                failedCount: 3,
                pendingByScope: new Dictionary<string, int> { ["dummy"] = 99 });
            loader.SnapshotOverride = overridden;

            var got = loader.GetSnapshot();
            Assert.That(got.PendingCount, Is.EqualTo(99));
            Assert.That(got.CompletedCount, Is.EqualTo(7));
            Assert.That(got.FailedCount, Is.EqualTo(3));
            Assert.That(got.PendingByScope["dummy"], Is.EqualTo(99));

            // Clearing the override restores the live snapshot.
            loader.SnapshotOverride = null;
            var restored = loader.GetSnapshot();
            Assert.That(restored.CompletedCount, Is.EqualTo(1));

            // Drain remaining pending handles to keep the test side-effect free.
            loader.FailWith(h2, new LoadError(LoadErrorCode.Unknown, h2.AddressableKey));
            loader.FailWith(h3, new LoadError(LoadErrorCode.Unknown, h3.AddressableKey));
        }
    }
}
