#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 5.1: <c>IAsyncAssetLoader</c> の契約テスト。<c>FakeAsyncAssetLoader</c> を SUT として、
    /// design.md §AssetLoading / §AddressablesAssetLoader と Requirement 4.1〜4.9 の
    /// Postconditions / Invariants を網羅する。
    /// 検証範囲:
    /// - <c>LoadAsync</c> がハンドルを即時返却し、Completion は callback 経由のみ・メインスレッドで配信する（Req 4.3）。
    /// - 同一 key の重複 <c>LoadAsync</c> に対して両 callback が安全に呼ばれる（Req 4.7）。
    /// - 失敗時に <c>LoadError</c>（コード / キー / 内訳）が callback に伝搬する（Req 4.4）。
    /// - <c>Release</c> / <c>ReleaseAll(scopeId)</c> / <c>Cancel</c> の状態遷移と「callback は高々 1 回」契約（Req 4.8、design.md §AssetLoading Invariants）。
    /// - <c>GetSnapshot()</c> の件数整合（Req 4.9）。
    /// 5.2 で <c>AddressablesAssetLoader</c> 本実装が入った後も同じ契約を満たすこと。
    /// </summary>
    [TestFixture]
    public sealed class AsyncAssetLoaderContractTests
    {
        private readonly List<UnityEngine.Object> spawnedAssets = new();

        [SetUp]
        public void SetUp()
        {
            MainThreadAffinity.Capture();
        }

        [TearDown]
        public void TearDown()
        {
            MainThreadAffinity.Reset();
            foreach (var asset in spawnedAssets)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
            spawnedAssets.Clear();
        }

        private Texture2D NewTexture(string name)
        {
            var tex = new Texture2D(1, 1) { name = name };
            spawnedAssets.Add(tex);
            return tex;
        }

        private static IAsyncAssetLoader NewSut(FakeAsyncAssetLoader.CompletionMode mode = FakeAsyncAssetLoader.CompletionMode.Deferred)
            => new FakeAsyncAssetLoader { Mode = mode };

        [Test]
        [Description("LoadAsync は引数で渡した key / scopeId を保持したハンドルを即時返却し、deferred モードでは callback が動く前に呼出元へ戻る（Req 4.3; design.md §AssetLoading Postconditions）")]
        public void LoadAsync_ReturnsHandleSynchronously_WithProvidedKeyAndScopeBeforeCallback()
        {
            var loader = NewSut();
            bool callbackFired = false;

            var handle = loader.LoadAsync<Texture2D>(
                "character/avatar/main",
                "character-tab",
                _ => callbackFired = true);

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle.AddressableKey, Is.EqualTo("character/avatar/main"));
            Assert.That(handle.ScopeId, Is.EqualTo("character-tab"));
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Pending),
                "deferred mode: handle must be Pending before any explicit resolution");
            Assert.That(callbackFired, Is.False,
                "completion must be delivered exclusively via the callback after LoadAsync returns");
        }

        [Test]
        [Description("Deferred モードでは外部から resolve するまで callback は呼ばれない（design.md §AssetLoading: completion via callback only）")]
        public void LoadAsync_DeferredMode_DoesNotInvokeCallback_UntilExplicitResolution()
        {
            var loader = (FakeAsyncAssetLoader)NewSut();
            int callCount = 0;
            AssetLoadResult<Texture2D>? observed = null;

            var handle = loader.LoadAsync<Texture2D>(
                "stage/light/key",
                "stage-tab",
                result =>
                {
                    callCount++;
                    observed = result;
                });

            Assert.That(callCount, Is.EqualTo(0));
            Assert.That(observed.HasValue, Is.False);

            var asset = NewTexture("stage/light/key");
            Assert.That(loader.CompleteWith(handle, asset), Is.True);

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(observed!.Value.Success, Is.True);
            Assert.That(observed.Value.Asset, Is.SameAs(asset));
        }

        [Test]
        [Description("Immediate モード + RegisterAsset で成功結果が callback に届き、ハンドルは Completed に遷移する（Req 4.3 成功パス、design.md AssetLoadResult.Ok）")]
        public void LoadAsync_ImmediateMode_RegisteredAsset_DeliversSuccessAndAdvancesHandleState()
        {
            var loader = (FakeAsyncAssetLoader)NewSut(FakeAsyncAssetLoader.CompletionMode.Immediate);
            var registered = NewTexture("camera/preview/render");
            loader.RegisterAsset("camera/preview/render", registered);

            AssetLoadResult<Texture2D>? observed = null;
            var handle = loader.LoadAsync<Texture2D>(
                "camera/preview/render",
                "camera-tab",
                result => observed = result);

            Assert.That(observed.HasValue, Is.True, "Immediate mode must dispatch the callback before LoadAsync returns");
            Assert.That(observed!.Value.Success, Is.True);
            Assert.That(observed.Value.Asset, Is.SameAs(registered));
            Assert.That(observed.Value.Error, Is.Null);
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Completed));
        }

        [Test]
        [Description("失敗時 LoadError は ErrorCode / AddressableKey / Detail / InnerException を保持して callback に渡される（Req 4.4; design.md §AssetLoading LoadError）")]
        public void LoadAsync_RegisteredFailure_DeliversLoadErrorWithKeyAndCodeAndInnerException()
        {
            var loader = (FakeAsyncAssetLoader)NewSut(FakeAsyncAssetLoader.CompletionMode.Immediate);
            var inner = new InvalidOperationException("disk read failed");
            loader.RegisterFailure(
                "stage/lighting/missing",
                new LoadError(LoadErrorCode.IoFailure, "stage/lighting/missing", "io-failure-detail", inner));

            AssetLoadResult<Texture2D>? observed = null;
            var handle = loader.LoadAsync<Texture2D>(
                "stage/lighting/missing",
                "stage-tab",
                result => observed = result);

            Assert.That(observed.HasValue, Is.True);
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Asset, Is.Null);
            Assert.That(observed.Value.Error.HasValue, Is.True);
            var err = observed.Value.Error!.Value;
            Assert.That(err.Code, Is.EqualTo(LoadErrorCode.IoFailure));
            Assert.That(err.AddressableKey, Is.EqualTo("stage/lighting/missing"));
            Assert.That(err.Detail, Is.EqualTo("io-failure-detail"));
            Assert.That(err.InnerException, Is.SameAs(inner));
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Failed));
        }

        [Test]
        [Description("同一 key の重複 LoadAsync 要求に対して、両 callback が Completion 時に呼ばれる（Req 4.7 「複数 Completion を配信する構造」を契約として固定）")]
        public void LoadAsync_DuplicateKey_BothCallbacksReceiveCompletion_OnResolvePending()
        {
            var loader = (FakeAsyncAssetLoader)NewSut();
            int firstCallCount = 0;
            int secondCallCount = 0;
            AssetLoadResult<Texture2D>? firstObserved = null;
            AssetLoadResult<Texture2D>? secondObserved = null;

            loader.LoadAsync<Texture2D>(
                "character/avatar/main",
                "character-tab",
                result =>
                {
                    firstCallCount++;
                    firstObserved = result;
                });
            loader.LoadAsync<Texture2D>(
                "character/avatar/main",
                "character-tab",
                result =>
                {
                    secondCallCount++;
                    secondObserved = result;
                });

            // No completion should have fired before resolution.
            Assert.That(firstCallCount, Is.EqualTo(0));
            Assert.That(secondCallCount, Is.EqualTo(0));

            var asset = NewTexture("character/avatar/main");
            loader.RegisterAsset("character/avatar/main", asset);
            loader.ResolvePending("character/avatar/main");

            Assert.That(firstCallCount, Is.EqualTo(1), "first request must receive exactly one completion");
            Assert.That(secondCallCount, Is.EqualTo(1), "second request must also receive exactly one completion");
            Assert.That(firstObserved!.Value.Success, Is.True);
            Assert.That(secondObserved!.Value.Success, Is.True);
            Assert.That(firstObserved.Value.Asset, Is.SameAs(asset));
            Assert.That(secondObserved.Value.Asset, Is.SameAs(asset));
        }

        [Test]
        [Description("callback は EditMode テスト実行スレッド（=Unity メインスレッド）で発火する（Req 4.3; design.md §AssetLoading: Completion は main thread）")]
        public void LoadAsync_CallbackInvokedOnCapturedMainThread()
        {
            var loader = (FakeAsyncAssetLoader)NewSut();
            var recorder = new MainThreadAffinity.Recorder();

            var handle = loader.LoadAsync<Texture2D>(
                "character/avatar/main",
                "character-tab",
                _ => recorder.Record());

            var asset = NewTexture("character/avatar/main");
            loader.CompleteWith(handle, asset);

            Assert.That(recorder.WasInvoked, Is.True);
            Assert.That(
                recorder.Matches(MainThreadAffinity.CapturedThreadId),
                Is.True,
                $"expected callback on captured main thread {MainThreadAffinity.CapturedThreadId} but observed {recorder.ObservedThreadId}");
        }

        [Test]
        [Description("Cancel(): 進行中ハンドルは LoadErrorCode.Cancelled で 1 回だけ callback が呼ばれ、State = Cancelled になる（design.md §AssetLoading Invariants）")]
        public void Cancel_PendingHandle_FiresCancelledError_AndCallbackInvokedOnceOnly()
        {
            var loader = NewSut();
            int callCount = 0;
            AssetLoadResult<Texture2D>? observed = null;

            var handle = loader.LoadAsync<Texture2D>(
                "camera/preview/render",
                "camera-tab",
                result =>
                {
                    callCount++;
                    observed = result;
                });

            handle.Cancel();

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Cancelled));
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Error.HasValue, Is.True);
            Assert.That(observed.Value.Error!.Value.Code, Is.EqualTo(LoadErrorCode.Cancelled));
            Assert.That(observed.Value.Error.Value.AddressableKey, Is.EqualTo("camera/preview/render"));
        }

        [Test]
        [Description("Cancel() は callback が settle 済みの場合に no-op となり、callback は再発火しない（design.md §AssetLoading: callback は高々 1 回）")]
        public void Cancel_AfterSettle_IsNoOp_AndDoesNotRefireCallback()
        {
            var loader = (FakeAsyncAssetLoader)NewSut();
            int callCount = 0;

            var handle = loader.LoadAsync<Texture2D>(
                "stage/light/key",
                "stage-tab",
                _ => callCount++);

            loader.FailWith(handle, new LoadError(LoadErrorCode.Unknown, handle.AddressableKey));
            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Failed));

            handle.Cancel();

            Assert.That(callCount, Is.EqualTo(1), "Cancel after settle must not re-invoke the callback");
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Failed),
                "settle state must not be overwritten by a late Cancel");
        }

        [Test]
        [Description("Release(): 進行中ハンドルは Cancelled 系エラーで callback を 1 回呼び、State = Released になる（Req 4.8、design.md §AssetLoading Release）")]
        public void Release_PendingHandle_DispatchesCancelledError_AndMarksHandleReleased()
        {
            var loader = NewSut();
            int callCount = 0;
            AssetLoadResult<Texture2D>? observed = null;

            var handle = loader.LoadAsync<Texture2D>(
                "stage/lighting/key",
                "stage-tab",
                result =>
                {
                    callCount++;
                    observed = result;
                });

            loader.Release(handle);

            Assert.That(callCount, Is.EqualTo(1));
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Released));
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Error.HasValue, Is.True);
            Assert.That(observed.Value.Error!.Value.Code, Is.EqualTo(LoadErrorCode.Cancelled));
        }

        [Test]
        [Description("ReleaseAll(scopeId) は対象 scope のハンドルだけを解放し、別 scope のハンドルには影響しない（Req 4.8、design.md §AssetLoading scope 一括）")]
        public void ReleaseAll_FiltersByScope_AndLeavesOtherScopesIntact()
        {
            var loader = (FakeAsyncAssetLoader)NewSut();
            int stageCallbacks = 0;
            int cameraCallbacks = 0;

            var stageHandleA = loader.LoadAsync<Texture2D>("stage/a", "stage-tab", _ => stageCallbacks++);
            var stageHandleB = loader.LoadAsync<Texture2D>("stage/b", "stage-tab", _ => stageCallbacks++);
            var cameraHandle = loader.LoadAsync<Texture2D>("camera/a", "camera-tab", _ => cameraCallbacks++);

            loader.ReleaseAll("stage-tab");

            Assert.That(stageHandleA.State, Is.EqualTo(AssetLoadState.Released));
            Assert.That(stageHandleB.State, Is.EqualTo(AssetLoadState.Released));
            Assert.That(stageCallbacks, Is.EqualTo(2),
                "both stage-tab handles must each receive exactly one Cancelled completion");
            Assert.That(cameraHandle.State, Is.EqualTo(AssetLoadState.Pending),
                "ReleaseAll must not touch handles registered under another scopeId");
            Assert.That(cameraCallbacks, Is.EqualTo(0));

            // Cleanly drain the remaining handle to keep the loader balanced.
            loader.FailWith(cameraHandle, new LoadError(LoadErrorCode.Unknown, cameraHandle.AddressableKey));
        }

        [Test]
        [Description("GetSnapshot は LoadAsync / 完了 / 失敗 / Cancel / Release の遷移に整合した件数を返し、PendingByScope は scope ごとの未完了数を提供する（Req 4.9; design.md §AssetLoaderSnapshot）")]
        public void GetSnapshot_PendingCompletedFailedCounters_StayInternallyConsistent()
        {
            var loader = (FakeAsyncAssetLoader)NewSut();

            var h1 = loader.LoadAsync<Texture2D>("a", "scope-x", _ => { });
            var h2 = loader.LoadAsync<Texture2D>("b", "scope-x", _ => { });
            var h3 = loader.LoadAsync<Texture2D>("c", "scope-y", _ => { });

            var initial = loader.GetSnapshot();
            Assert.That(initial.PendingCount, Is.EqualTo(3));
            Assert.That(initial.CompletedCount, Is.EqualTo(0));
            Assert.That(initial.FailedCount, Is.EqualTo(0));
            Assert.That(initial.PendingByScope["scope-x"], Is.EqualTo(2));
            Assert.That(initial.PendingByScope["scope-y"], Is.EqualTo(1));

            // Resolve one success.
            var asset = NewTexture("a");
            loader.CompleteWith(h1, asset);

            var afterSuccess = loader.GetSnapshot();
            Assert.That(afterSuccess.PendingCount, Is.EqualTo(2));
            Assert.That(afterSuccess.CompletedCount, Is.EqualTo(1));
            Assert.That(afterSuccess.FailedCount, Is.EqualTo(0));
            Assert.That(afterSuccess.PendingByScope.ContainsKey("scope-x"), Is.True);
            Assert.That(afterSuccess.PendingByScope["scope-x"], Is.EqualTo(1));

            // Resolve one failure.
            loader.FailWith(h2, new LoadError(LoadErrorCode.KeyNotFound, h2.AddressableKey));
            var afterFailure = loader.GetSnapshot();
            Assert.That(afterFailure.PendingCount, Is.EqualTo(1));
            Assert.That(afterFailure.CompletedCount, Is.EqualTo(1));
            Assert.That(afterFailure.FailedCount, Is.EqualTo(1));
            Assert.That(afterFailure.PendingByScope.ContainsKey("scope-x"), Is.False,
                "scope-x should be dropped from PendingByScope once it has no pending handles");

            // Drain the remaining handle so subsequent fixtures are not affected by leftover state.
            h3.Cancel();
            var afterDrain = loader.GetSnapshot();
            Assert.That(afterDrain.PendingCount, Is.EqualTo(0));
            Assert.That(afterDrain.PendingByScope, Is.Empty);
        }

        [Test]
        [Description("LoadAsync の boundary preconditions: 空 key / 空 scopeId は ArgumentException、null callback は ArgumentNullException を即座に投げる（design.md §AssetLoading Preconditions）")]
        public void LoadAsync_InvalidArguments_ThrowImmediately()
        {
            var loader = NewSut();

            Assert.Throws<ArgumentException>(
                () => loader.LoadAsync<Texture2D>(string.Empty, "scope", _ => { }),
                "empty addressableKey must throw ArgumentException at the boundary");

            Assert.Throws<ArgumentException>(
                () => loader.LoadAsync<Texture2D>("key", string.Empty, _ => { }),
                "empty scopeId must throw ArgumentException at the boundary");

            Assert.Throws<ArgumentNullException>(
                () => loader.LoadAsync<Texture2D>("key", "scope", null!),
                "null callback must throw ArgumentNullException at the boundary");
        }
    }
}
