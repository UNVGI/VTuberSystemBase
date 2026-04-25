#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 8.2: <see cref="TabPanelRegistry"/> preload-completion judgment
    /// tests. Pin the contract that <c>IsPreloadComplete</c> flips to
    /// <c>true</c> exactly when every tab has reported either a mount
    /// (<c>NotifyTabMounted</c>) or a failure (<c>MarkTabFailed</c>),
    /// that <see cref="PreloadProgress"/> reports the correct
    /// <c>LoadedCount / TotalCount / FailedTabs</c>, that
    /// <c>RegisterTab</c> returns a single live
    /// <see cref="ITabLifecycleHandle"/> per tab whose <c>Dispose</c>
    /// detaches every callback, and that failed tabs do not block the other
    /// two from completing (Requirements 2.1, 3.1, 3.3, 3.4, 3.5, 3.6, 3.7,
    /// 5.7, 10.1, 10.2).
    /// </summary>
    [TestFixture]
    public sealed class TabPanelRegistryTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private TabPanelRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _registry = new TabPanelRegistry(_logger);
        }

        // ---- construction / defaults -----------------------------------

        [Test]
        [Description("コンストラクタは null logger を拒否する（DI 契約）")]
        public void Constructor_NullLogger_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new TabPanelRegistry(null!));
        }

        [Test]
        [Description("初期状態では IsPreloadComplete が false で、LoadedCount=0, TotalCount=3, FailedTabs 空")]
        public void NewRegistry_HasZeroLoaded_AndNotComplete()
        {
            Assert.That(_registry.IsPreloadComplete, Is.False);
            Assert.That(_registry.TotalTabCount, Is.EqualTo(3));
            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(0));
            Assert.That(p.TotalCount, Is.EqualTo(3));
            Assert.That(p.FailedTabs, Is.Empty);
        }

        // ---- mount progression -----------------------------------------

        [Test]
        [Description("1 タブだけ Mount しても IsPreloadComplete は false / LoadedCount=1")]
        public void NotifyTabMounted_OneOfThree_ProgressOneNotComplete()
        {
            _registry.NotifyTabMounted(TabId.Character);

            Assert.That(_registry.IsPreloadComplete, Is.False);
            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(1));
            Assert.That(p.TotalCount, Is.EqualTo(3));
            Assert.That(p.FailedTabs, Is.Empty);
        }

        [Test]
        [Description("2 タブまで Mount しても IsPreloadComplete は false / LoadedCount=2")]
        public void NotifyTabMounted_TwoOfThree_ProgressTwoNotComplete()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.StageLighting);

            Assert.That(_registry.IsPreloadComplete, Is.False);
            Assert.That(_registry.GetPreloadProgress().LoadedCount, Is.EqualTo(2));
        }

        [Test]
        [Description("3 タブ全て Mount で IsPreloadComplete=true / LoadedCount=3 / FailedTabs 空（Requirement 3.1）")]
        public void NotifyTabMounted_AllThree_IsCompleteAndNoFailures()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.StageLighting);
            _registry.NotifyTabMounted(TabId.CameraSwitcher);

            Assert.That(_registry.IsPreloadComplete, Is.True);
            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(3));
            Assert.That(p.TotalCount, Is.EqualTo(3));
            Assert.That(p.FailedTabs, Is.Empty);
        }

        [Test]
        [Description("同一タブの Mount を複数回呼んでもカウントは 1 度しか進まない（OnEnable 再入対策）")]
        public void NotifyTabMounted_Idempotent_DoesNotDoubleCount()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.Character);

            Assert.That(_registry.GetPreloadProgress().LoadedCount, Is.EqualTo(1));
            Assert.That(_registry.IsPreloadComplete, Is.False);
        }

        // ---- failure progression ---------------------------------------

        [Test]
        [Description("失敗 1 件注入で FailedTabs に当該 ID が積まれ、LoadedCount にも反映される（失敗もプリロード完了の一部; Requirement 3.5）")]
        public void MarkTabFailed_OneTab_RecordedAndCountedTowardLoaded()
        {
            _registry.MarkTabFailed(TabId.StageLighting, "vsb-tab-root missing");

            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(1));
            Assert.That(p.FailedTabs, Has.Member(TabId.StageLighting));
        }

        [Test]
        [Description("2 タブ Mount + 1 タブ Failure で IsPreloadComplete=true、FailedTabs に該当 ID（観測可能完了状態）")]
        public void TwoMountsPlusOneFailure_IsCompleteWithFailureRecorded()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.CameraSwitcher);
            _registry.MarkTabFailed(TabId.StageLighting, "skin validation failed");

            Assert.That(_registry.IsPreloadComplete, Is.True);
            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(3));
            Assert.That(p.FailedTabs, Has.Member(TabId.StageLighting));
            Assert.That(p.FailedTabs, Has.No.Member(TabId.Character));
            Assert.That(p.FailedTabs, Has.No.Member(TabId.CameraSwitcher));
        }

        [Test]
        [Description("3 タブ全て失敗でも IsPreloadComplete=true（縮退起動も完了は完了）")]
        public void AllThreeFailed_IsComplete()
        {
            _registry.MarkTabFailed(TabId.Character, "x");
            _registry.MarkTabFailed(TabId.StageLighting, "y");
            _registry.MarkTabFailed(TabId.CameraSwitcher, "z");

            Assert.That(_registry.IsPreloadComplete, Is.True);
            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(3));
            Assert.That(p.FailedTabs.Count, Is.EqualTo(3));
        }

        [Test]
        [Description("Mount 後に MarkTabFailed されても先勝ち：失敗が記録されない、LoadedCount は変わらない")]
        public void MarkTabFailed_AfterMount_IsNoOp()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.MarkTabFailed(TabId.Character, "late failure");

            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(1));
            Assert.That(p.FailedTabs, Is.Empty);
        }

        [Test]
        [Description("Failure 後に NotifyTabMounted されても上書きされない（失敗状態が維持される）")]
        public void NotifyTabMounted_AfterFailure_IsNoOp()
        {
            _registry.MarkTabFailed(TabId.Character, "first failure");
            _registry.NotifyTabMounted(TabId.Character);

            var p = _registry.GetPreloadProgress();
            Assert.That(p.LoadedCount, Is.EqualTo(1));
            Assert.That(p.FailedTabs, Has.Member(TabId.Character));
        }

        [Test]
        [Description("MarkTabFailed の reason は null/empty を許容しない（診断ログのトレーサビリティ確保）")]
        public void MarkTabFailed_NullOrEmptyReason_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => _registry.MarkTabFailed(TabId.Character, null!));
            Assert.Throws<ArgumentException>(
                () => _registry.MarkTabFailed(TabId.Character, string.Empty));
        }

        // ---- preload event publication ---------------------------------

        [Test]
        [Description("NotifyTabMounted で OnPreloadChanged が PreloadOutcome.Succeeded で 1 回発火する")]
        public void NotifyTabMounted_RaisesSucceededEvent()
        {
            var events = new List<PreloadEvent>();
            _registry.OnPreloadChanged += events.Add;

            _registry.NotifyTabMounted(TabId.StageLighting);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].TabId, Is.EqualTo(TabId.StageLighting));
            Assert.That(events[0].Outcome, Is.EqualTo(PreloadOutcome.Succeeded));
        }

        [Test]
        [Description("MarkTabFailed で OnPreloadChanged が PreloadOutcome.Failed で 1 回発火する")]
        public void MarkTabFailed_RaisesFailedEvent()
        {
            var events = new List<PreloadEvent>();
            _registry.OnPreloadChanged += events.Add;

            _registry.MarkTabFailed(TabId.CameraSwitcher, "missing class");

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].TabId, Is.EqualTo(TabId.CameraSwitcher));
            Assert.That(events[0].Outcome, Is.EqualTo(PreloadOutcome.Failed));
        }

        [Test]
        [Description("冪等な NotifyTabMounted の 2 回目以降は OnPreloadChanged を再発火しない")]
        public void NotifyTabMounted_DuplicateMount_DoesNotRefire()
        {
            var events = new List<PreloadEvent>();
            _registry.OnPreloadChanged += events.Add;

            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.Character);

            Assert.That(events.Count, Is.EqualTo(1));
        }

        // ---- diagnostic logging ----------------------------------------

        [Test]
        [Description("Mount / Failure それぞれ Preload カテゴリでログを残す（Requirement 11.1）")]
        public void Notifications_EmitPreloadLogEntries()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.MarkTabFailed(TabId.StageLighting, "rule violation");

            Assert.That(_logger.Entries.Count, Is.GreaterThanOrEqualTo(2));
            foreach (var entry in _logger.Entries)
            {
                Assert.That(entry.Category, Is.EqualTo(LogCategory.Preload));
            }
        }

        [Test]
        [Description("MarkTabFailed のログレベルは Error 以上で出る（オペレーターが診断画面で確認できるレベル）")]
        public void MarkTabFailed_LogsAtErrorLevel()
        {
            _registry.MarkTabFailed(TabId.StageLighting, "vsb-tab-root missing");

            var hasError = false;
            foreach (var entry in _logger.Entries)
            {
                if (entry.Level == LogLevel.Error) { hasError = true; break; }
            }
            Assert.That(hasError, Is.True,
                "MarkTabFailed must produce at least one Error-level log entry");
        }

        // ---- RegisterTab / ITabLifecycleHandle -------------------------

        [Test]
        [Description("RegisterTab は ITabLifecycleHandle を返し、TabId を保持する")]
        public void RegisterTab_ReturnsHandleWithTabId()
        {
            var handle = _registry.RegisterTab(
                TabId.Character, new TabMetadata("Character"));

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle.TabId, Is.EqualTo(TabId.Character));
        }

        [Test]
        [Description("登録直後の handle.IsActive は false（プリロード未完了 → 起動アクティベートまでは非活性）")]
        public void RegisterTab_HandleIsInitiallyInactive()
        {
            var handle = _registry.RegisterTab(
                TabId.Character, new TabMetadata("Character"));

            Assert.That(handle.IsActive, Is.False);
        }

        [Test]
        [Description("同一 TabId に対する 2 回目の RegisterTab は InvalidOperationException")]
        public void RegisterTab_SameTabTwice_Throws()
        {
            _registry.RegisterTab(TabId.Character, new TabMetadata("Character"));

            Assert.Throws<InvalidOperationException>(
                () => _registry.RegisterTab(
                    TabId.Character, new TabMetadata("Character (dup)")));
        }

        [Test]
        [Description("Dispose した handle に対する OnActivated / OnDeactivated 購読は再発火を起こさない（購読解除契約; Requirement 5.7）")]
        public void HandleDispose_DetachesCallbacks()
        {
            var handle = _registry.RegisterTab(
                TabId.Character, new TabMetadata("Character"));
            var activatedCount = 0;
            var deactivatedCount = 0;
            handle.OnActivated += () => activatedCount++;
            handle.OnDeactivated += () => deactivatedCount++;

            handle.Dispose();

            // After Dispose the registry is no longer allowed to invoke
            // callbacks for this handle. Re-registration should be possible
            // (the slot is freed).
            Assert.That(() => _registry.RegisterTab(
                    TabId.Character, new TabMetadata("Character")),
                Throws.Nothing);
            Assert.That(activatedCount, Is.EqualTo(0));
            Assert.That(deactivatedCount, Is.EqualTo(0));
        }

        [Test]
        [Description("Dispose 済みの handle を再 Dispose しても例外にならない（IDisposable 契約）")]
        public void HandleDispose_IsIdempotent()
        {
            var handle = _registry.RegisterTab(
                TabId.Character, new TabMetadata("Character"));
            handle.Dispose();

            Assert.That(() => handle.Dispose(), Throws.Nothing);
        }

        [Test]
        [Description("Mount は RegisterTab されていないタブにも適用できる（タブ spec 不在でも shell 単独起動可能; Requirement 10.1, 10.2）")]
        public void NotifyTabMounted_WithoutRegisterTab_StillProgresses()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.StageLighting);
            _registry.NotifyTabMounted(TabId.CameraSwitcher);

            Assert.That(_registry.IsPreloadComplete, Is.True);
        }

        // ---- snapshot value-copy semantics -----------------------------

        [Test]
        [Description("GetPreloadProgress で取得した snapshot は呼出時点のコピーで、後の変化に影響されない")]
        public void GetPreloadProgress_ReturnsValueSnapshot()
        {
            _registry.NotifyTabMounted(TabId.Character);
            var snapshotA = _registry.GetPreloadProgress();

            _registry.NotifyTabMounted(TabId.StageLighting);
            var snapshotB = _registry.GetPreloadProgress();

            Assert.That(snapshotA.LoadedCount, Is.EqualTo(1));
            Assert.That(snapshotB.LoadedCount, Is.EqualTo(2));
        }
    }
}
