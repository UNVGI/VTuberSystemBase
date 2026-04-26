#nullable enable
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 12.2: Unit tests pinning the production
    /// <see cref="AddressablesAssetLoader"/> wrapper-layer contracts most likely
    /// to break under refactor — duplicate-load suppression (Req 4.7) and
    /// completion (Cancel) callback delivery on the Unity main thread (Req 4.3,
    /// 4.8). Exhaustive contract behaviour is exercised against the
    /// <see cref="FakeAsyncAssetLoader"/> in
    /// <see cref="AsyncAssetLoaderContractTests"/>; this fixture targets the
    /// real Addressables-backed loader so structural changes around the shared
    /// entry cache, scope index, and cancel/release paths get caught in CI.
    /// </summary>
    /// <remarks>
    /// Addressables in EditMode without project-level configuration emits
    /// console errors (missing settings asset, unknown key) when LoadAssetAsync
    /// is invoked. Each test that touches the loader flips
    /// <see cref="LogAssert.ignoreFailingMessages"/> inside the test body so
    /// those environmental logs do not mask the wrapper-layer assertions; the
    /// flag has to be set inside the test scope (Unity Test Framework creates
    /// a fresh LogScope per BeforeAction/Test/AfterAction phase, so a SetUp
    /// assignment is dropped before the test body runs).
    /// </remarks>
    [TestFixture]
    public sealed class AddressablesAssetLoaderTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private AddressablesAssetLoader _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _sut = new AddressablesAssetLoader(_logger);
            MainThreadAffinity.Capture();
        }

        [TearDown]
        public void TearDown()
        {
            try { _sut.Dispose(); } catch { /* idempotent guard */ }
            MainThreadAffinity.Reset();
        }

        // ---- Constructor / argument validation -------------------------------

        [Test]
        [Description("Constructor は null logger を拒否する (Composition Root invariant)")]
        public void Constructor_NullLogger_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AddressablesAssetLoader(null!));
        }

        [Test]
        [Description("LoadAsync は空 key / 空 scopeId / null callback を即時拒否する (design.md §AssetLoading Preconditions)")]
        public void LoadAsync_InvalidArguments_ThrowImmediately()
        {
            // Argument validation runs before any Addressables interaction, so no
            // environmental log suppression is needed for this test.
            Assert.Throws<ArgumentException>(
                () => _sut.LoadAsync<Texture2D>(string.Empty, "scope", _ => { }),
                "Empty addressableKey must be rejected at the boundary.");
            Assert.Throws<ArgumentException>(
                () => _sut.LoadAsync<Texture2D>("key", string.Empty, _ => { }),
                "Empty scopeId must be rejected at the boundary.");
            Assert.Throws<ArgumentNullException>(
                () => _sut.LoadAsync<Texture2D>("key", "scope", null!),
                "Null callback must be rejected at the boundary.");
        }

        // ---- Duplicate-load suppression (Req 4.7) ----------------------------

        [Test]
        [Description("同一 (key, type) の連続 LoadAsync は 1 本の underlying ハンドルに集約され、" +
                     "両 wrapper を Release しきった時点でのみ AssetUnloaded ログが 1 回出る (Req 4.7, 11.3)")]
        public void LoadAsync_SameKeyTwice_DedupsToSingleSharedEntry_EmitsOneAssetUnloadedLog()
        {
            LogAssert.ignoreFailingMessages = true;

            var firstCallbacks = 0;
            var secondCallbacks = 0;
            var h1 = _sut.LoadAsync<Texture2D>(
                "character/avatar/main", "character-tab", _ => firstCallbacks++);
            var h2 = _sut.LoadAsync<Texture2D>(
                "character/avatar/main", "character-tab", _ => secondCallbacks++);

            Assert.That(h1, Is.Not.SameAs(h2),
                "Each LoadAsync call returns its own wrapper even though the underlying entry is shared.");
            Assert.That(h1.AddressableKey, Is.EqualTo("character/avatar/main"));
            Assert.That(h2.AddressableKey, Is.EqualTo("character/avatar/main"));

            // Release both wrappers. With dedupe: exactly one AssetUnloaded log
            // (last subscriber drops). Without dedupe: two AssetUnloaded logs
            // (each wrapper would own its own underlying handle).
            _sut.Release(h1);
            _sut.Release(h2);

            int unloadCount = CountLogs(LogCategory.AssetLoad, "AssetUnloaded");
            Assert.That(unloadCount, Is.EqualTo(1),
                "Same (key, type) load requests must collapse onto a single underlying " +
                "Addressables handle; AssetUnloaded must fire exactly once across both releases.");

            int startedCount = CountLogs(LogCategory.AssetLoad, "AssetLoadStarted");
            Assert.That(startedCount, Is.EqualTo(2),
                "Both LoadAsync calls must be observable from the diagnostics log even though " +
                "they collapse to one underlying load (per-call AssetLoadStarted contract).");
        }

        [Test]
        [Description("LoadAsync が 1 回しか呼ばれていない場合、Release で AssetUnloaded ログは 1 回 (regression baseline for the dedupe count)")]
        public void LoadAsync_SingleCall_Release_EmitsOneAssetUnloadedLog()
        {
            LogAssert.ignoreFailingMessages = true;

            var handle = _sut.LoadAsync<Texture2D>(
                "stage/light/key", "stage-tab", _ => { });

            _sut.Release(handle);

            int unloadCount = CountLogs(LogCategory.AssetLoad, "AssetUnloaded");
            Assert.That(unloadCount, Is.EqualTo(1),
                "Single LoadAsync followed by Release must produce exactly one AssetUnloaded log; " +
                "this anchors the dedupe count expected from the duplicate-load test.");
        }

        // ---- Cancel: callback contract & main-thread delivery (Req 4.3, 4.8) ----

        [Test]
        [Description("Cancel: pending wrapper の callback は LoadErrorCode.Cancelled で 1 回だけ発火し、" +
                     "AddressableKey が保持される (design.md §AssetLoading Invariants)")]
        public void Cancel_PendingHandle_DeliversCancelledCallbackWithCorrectKey()
        {
            LogAssert.ignoreFailingMessages = true;

            int callCount = 0;
            AssetLoadResult<Texture2D>? observed = null;
            var handle = _sut.LoadAsync<Texture2D>(
                "camera/preview/render",
                "camera-tab",
                result => { callCount++; observed = result; });

            // If Addressables synchronously settled the underlying handle (e.g. early
            // failure injection), Cancel becomes a no-op by design and the callback
            // would already have fired with the underlying error. Skip cleanly so the
            // wrapper-layer contract under test is not masked by environment.
            if (handle.State != AssetLoadState.Pending)
            {
                Assert.Inconclusive(
                    $"Underlying Addressables handle synchronously settled to {handle.State}; " +
                    "Cancel-of-pending semantics not observable in this EditMode environment.");
                return;
            }

            handle.Cancel();

            Assert.That(callCount, Is.EqualTo(1),
                "Cancel must invoke the wrapper callback exactly once.");
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Cancelled));
            Assert.That(observed!.Value.Success, Is.False);
            Assert.That(observed.Value.Error.HasValue, Is.True);
            var err = observed.Value.Error!.Value;
            Assert.That(err.Code, Is.EqualTo(LoadErrorCode.Cancelled),
                "Cancelled wrappers must surface LoadErrorCode.Cancelled in the callback.");
            Assert.That(err.AddressableKey, Is.EqualTo("camera/preview/render"),
                "Cancelled error must carry the original AddressableKey.");
        }

        [Test]
        [Description("重複ロードのうち片方を Cancel しても、他方は Pending を維持し callback も発火しない (Req 4.7 + 4.8)")]
        public void Cancel_OneOfDuplicateHandles_DoesNotAffectTheOther()
        {
            LogAssert.ignoreFailingMessages = true;

            int cb1Count = 0;
            int cb2Count = 0;
            var h1 = _sut.LoadAsync<Texture2D>("character/avatar/main", "character-tab", _ => cb1Count++);
            var h2 = _sut.LoadAsync<Texture2D>("character/avatar/main", "character-tab", _ => cb2Count++);

            if (h1.State != AssetLoadState.Pending || h2.State != AssetLoadState.Pending)
            {
                Assert.Inconclusive("Both wrappers settled synchronously; cannot exercise per-wrapper Cancel isolation.");
                return;
            }

            h1.Cancel();

            Assert.That(h1.State, Is.EqualTo(AssetLoadState.Cancelled));
            Assert.That(cb1Count, Is.EqualTo(1),
                "Cancelled wrapper's callback must fire exactly once.");
            Assert.That(h2.State, Is.EqualTo(AssetLoadState.Pending),
                "Sibling duplicate wrapper must remain pending after the first is cancelled.");
            Assert.That(cb2Count, Is.EqualTo(0),
                "Sibling duplicate wrapper's callback must not fire from the first's cancellation.");

            // Drain h2 so the loader teardown in [TearDown] is balanced.
            h2.Cancel();
        }

        [Test]
        [Description("Cancel callback は呼出しスレッド (= Unity メインスレッド for EditMode tests) で発火する (Req 4.3)")]
        public void Cancel_CallbackFiresOnCapturedMainThread()
        {
            LogAssert.ignoreFailingMessages = true;

            var recorder = new MainThreadAffinity.Recorder();
            var handle = _sut.LoadAsync<Texture2D>(
                "stage/light/key",
                "stage-tab",
                _ => recorder.Record());

            if (handle.State != AssetLoadState.Pending)
            {
                Assert.Inconclusive(
                    $"Underlying handle settled synchronously to {handle.State}; " +
                    "cannot exercise Cancel main-thread delivery.");
                return;
            }

            handle.Cancel();

            Assert.That(recorder.WasInvoked, Is.True);
            Assert.That(recorder.Matches(MainThreadAffinity.CapturedThreadId), Is.True,
                $"Cancel callback should fire on the captured main thread " +
                $"{MainThreadAffinity.CapturedThreadId}, observed {recorder.ObservedThreadId}.");
        }

        [Test]
        [Description("Cancel 後の再 Cancel は callback を再発火させない (callback は高々 1 回; design.md §AssetLoading Invariants)")]
        public void Cancel_Twice_DoesNotInvokeCallbackTwice()
        {
            LogAssert.ignoreFailingMessages = true;

            int callCount = 0;
            var handle = _sut.LoadAsync<Texture2D>(
                "stage/lighting/key",
                "stage-tab",
                _ => callCount++);

            if (handle.State != AssetLoadState.Pending)
            {
                Assert.Inconclusive(
                    $"Underlying handle settled synchronously to {handle.State}; " +
                    "double-Cancel idempotency not observable.");
                return;
            }

            handle.Cancel();
            handle.Cancel();

            Assert.That(callCount, Is.EqualTo(1),
                "Repeated Cancel must be idempotent; the wrapper callback contract is " +
                "at-most-once.");
            Assert.That(handle.State, Is.EqualTo(AssetLoadState.Cancelled));
        }

        // ---- Helpers ----

        private int CountLogs(LogCategory category, string substring)
        {
            int count = 0;
            foreach (var entry in _logger.Entries)
            {
                if (entry.Category == category && entry.Message != null
                    && entry.Message.Contains(substring))
                {
                    count++;
                }
            }
            return count;
        }
    }
}
