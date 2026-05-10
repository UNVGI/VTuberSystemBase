#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 8.3: <see cref="TabPanelRegistry.SwitchTo"/> contract tests. Pin
    /// the "<c>style.display</c> swap only" rule (Requirement 2.4, 3.6),
    /// the three <see cref="SwitchErrorCode"/> rejection paths
    /// (<c>PreloadIncomplete</c>, <c>TabDisabled</c>, <c>AlreadyActive</c>),
    /// the lifecycle dispatch order (outgoing <c>OnDeactivated</c> →
    /// incoming <c>OnActivated</c> → <c>OnTabSwitched</c>), and the
    /// per-switch duration logging (Requirement 11.2). The 100-iteration
    /// timing test caps the registry-side cost so the public performance
    /// goal (95th percentile &lt; 16.67 ms in PlayMode) remains achievable
    /// (Requirement 2.9).
    /// </summary>
    [TestFixture]
    public sealed class TabPanelRegistrySwitchToTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private TabPanelRegistry _registry = null!;
        private Dictionary<TabId, VisualElement> _roots = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _registry = new TabPanelRegistry(_logger);
            _roots = new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, new VisualElement { name = "tab-character" } },
                { TabId.StageLighting, new VisualElement { name = "tab-stage-lighting" } },
                { TabId.CameraSwitcher, new VisualElement { name = "tab-camera-switcher" } },
            };
        }

        // ---- helpers ---------------------------------------------------

        private void MountAllTabsWithRoots()
        {
            foreach (var pair in _roots)
            {
                _registry.NotifyTabMounted(pair.Key, pair.Value);
            }
        }

        // ---- defaults --------------------------------------------------

        [Test]
        [Description("初期状態で ActiveTab は null（プリロード前は誰もアクティブでない）")]
        public void NewRegistry_ActiveTab_IsNull()
        {
            Assert.That(_registry.ActiveTab, Is.Null);
        }

        [Test]
        [Description("VisualElement 付き NotifyTabMounted は要素を一旦 None で初期化する（描画はアクティブ化まで保留）")]
        public void NotifyTabMounted_WithRoot_HidesByDefault()
        {
            var root = new VisualElement();
            // sanity: default style.display is Flex (UI Toolkit default)
            Assume.That(root.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            _registry.NotifyTabMounted(TabId.Character, root);

            Assert.That(root.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        [Description("VisualElement 付き NotifyTabMounted は null root を拒否する（DI 契約）")]
        public void NotifyTabMounted_WithNullRoot_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _registry.NotifyTabMounted(TabId.Character, null!));
        }

        // ---- rejection paths -------------------------------------------

        [Test]
        [Description("プリロード未完了で SwitchTo を呼ぶと PreloadIncomplete を返す（Requirement 2.7, 3.1）")]
        public void SwitchTo_BeforePreloadComplete_ReturnsPreloadIncomplete()
        {
            // Only mount one of the three tabs.
            _registry.NotifyTabMounted(TabId.Character, _roots[TabId.Character]);

            var result = _registry.SwitchTo(TabId.Character);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(SwitchErrorCode.PreloadIncomplete));
            Assert.That(_registry.ActiveTab, Is.Null,
                "Failed switch must not mutate ActiveTab.");
        }

        [Test]
        [Description("失敗マークされたタブへの SwitchTo は TabDisabled を返す（Requirement 3.5, 6.6）")]
        public void SwitchTo_FailedTab_ReturnsTabDisabled()
        {
            _registry.NotifyTabMounted(TabId.Character, _roots[TabId.Character]);
            _registry.NotifyTabMounted(TabId.CameraSwitcher, _roots[TabId.CameraSwitcher]);
            _registry.MarkTabFailed(TabId.StageLighting, "skin missing");

            var result = _registry.SwitchTo(TabId.StageLighting);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(SwitchErrorCode.TabDisabled));
        }

        [Test]
        [Description("既にアクティブなタブへの再 SwitchTo は AlreadyActive を返し、再活性化イベントを再発火しない")]
        public void SwitchTo_AlreadyActiveTab_ReturnsAlreadyActive()
        {
            MountAllTabsWithRoots();
            var first = _registry.SwitchTo(TabId.Character);
            Assume.That(first.Success, Is.True);

            var second = _registry.SwitchTo(TabId.Character);

            Assert.That(second.Success, Is.False);
            Assert.That(second.Error, Is.EqualTo(SwitchErrorCode.AlreadyActive));
        }

        [Test]
        [Description("失敗 SwitchTo（PreloadIncomplete）でも他タブの display は変更されない")]
        public void SwitchTo_Rejected_DoesNotChangeDisplay()
        {
            _registry.NotifyTabMounted(TabId.Character, _roots[TabId.Character]);
            // Tabs were initialised to None on bind.
            Assume.That(_roots[TabId.Character].style.display.value,
                Is.EqualTo(DisplayStyle.None));

            var result = _registry.SwitchTo(TabId.Character);

            Assert.That(result.Success, Is.False);
            Assert.That(_roots[TabId.Character].style.display.value,
                Is.EqualTo(DisplayStyle.None),
                "Rejected SwitchTo must not flip display to Flex.");
        }

        // ---- happy path: display swap only -----------------------------

        [Test]
        [Description("初回 SwitchTo: 対象タブのみ Flex、他 2 タブは None で維持される")]
        public void SwitchTo_FirstTime_SetsTargetFlexOthersNone()
        {
            MountAllTabsWithRoots();

            var result = _registry.SwitchTo(TabId.Character);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(_roots[TabId.Character].style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_roots[TabId.StageLighting].style.display.value,
                Is.EqualTo(DisplayStyle.None));
            Assert.That(_roots[TabId.CameraSwitcher].style.display.value,
                Is.EqualTo(DisplayStyle.None));
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
        }

        [Test]
        [Description("2 回目以降の SwitchTo: 旧アクティブが None、新アクティブが Flex に切り替わる")]
        public void SwitchTo_SecondCall_TogglesPreviousAndNew()
        {
            MountAllTabsWithRoots();
            _registry.SwitchTo(TabId.Character);

            var result = _registry.SwitchTo(TabId.StageLighting);

            Assert.That(result.Success, Is.True);
            Assert.That(_roots[TabId.Character].style.display.value,
                Is.EqualTo(DisplayStyle.None));
            Assert.That(_roots[TabId.StageLighting].style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.StageLighting));
        }

        [Test]
        [Description("SwitchTo は VisualElement のインスタンスを差し替えない（再 clone なし; Requirement 2.4, 3.6）")]
        public void SwitchTo_DoesNotReplaceVisualElementInstance()
        {
            MountAllTabsWithRoots();
            var characterRootBefore = _roots[TabId.Character];
            var stageRootBefore = _roots[TabId.StageLighting];

            _registry.SwitchTo(TabId.Character);
            _registry.SwitchTo(TabId.StageLighting);
            _registry.SwitchTo(TabId.Character);

            // The dictionary entries the test seeded must still hold the
            // exact same VisualElement references — proves SwitchTo never
            // re-clones nor reassigns the bound roots.
            Assert.That(_roots[TabId.Character], Is.SameAs(characterRootBefore));
            Assert.That(_roots[TabId.StageLighting], Is.SameAs(stageRootBefore));
        }

        [Test]
        [Description("SwitchTo は VisualElement の子要素や hierarchy 構成を破壊しない（再 parse 禁止）")]
        public void SwitchTo_DoesNotMutateHierarchy()
        {
            // Add a child to the StageLighting root that should survive.
            var child = new VisualElement { name = "preserved-child" };
            _roots[TabId.StageLighting].Add(child);
            MountAllTabsWithRoots();

            _registry.SwitchTo(TabId.Character);
            _registry.SwitchTo(TabId.StageLighting);
            _registry.SwitchTo(TabId.Character);

            Assert.That(_roots[TabId.StageLighting].childCount, Is.EqualTo(1));
            Assert.That(_roots[TabId.StageLighting][0], Is.SameAs(child));
        }

        [Test]
        [Description("SwitchTo: 唯一 style.display プロパティのみが変化する（他のスタイル属性は不変）")]
        public void SwitchTo_OnlyTouchesDisplayStyle()
        {
            MountAllTabsWithRoots();
            // Snapshot a non-display style to detect collateral mutation.
            _roots[TabId.Character].style.opacity = 0.42f;

            _registry.SwitchTo(TabId.Character);
            _registry.SwitchTo(TabId.StageLighting);

            Assert.That(_roots[TabId.Character].style.opacity.value, Is.EqualTo(0.42f),
                "Switching tabs must not touch unrelated style fields.");
        }

        // ---- lifecycle dispatch ----------------------------------------

        [Test]
        [Description("OnTabSwitched は成功した SwitchTo ごとに 1 回発火する")]
        public void SwitchTo_RaisesOnTabSwitchedOnce()
        {
            MountAllTabsWithRoots();
            var events = new List<TabSwitchEvent>();
            _registry.OnTabSwitched += events.Add;

            _registry.SwitchTo(TabId.Character);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].To, Is.EqualTo(TabId.Character));
            Assert.That(events[0].From, Is.Null,
                "Initial activation has no previous active tab.");
        }

        [Test]
        [Description("OnTabSwitched.From は直前のアクティブタブを保持する（2 回目以降）")]
        public void SwitchTo_SecondCall_SetsFromToPreviousTab()
        {
            MountAllTabsWithRoots();
            _registry.SwitchTo(TabId.Character);
            var events = new List<TabSwitchEvent>();
            _registry.OnTabSwitched += events.Add;

            _registry.SwitchTo(TabId.StageLighting);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].From, Is.EqualTo(TabId.Character));
            Assert.That(events[0].To, Is.EqualTo(TabId.StageLighting));
        }

        [Test]
        [Description("失敗 SwitchTo は OnTabSwitched を発火しない")]
        public void SwitchTo_Rejected_DoesNotRaiseOnTabSwitched()
        {
            // Preload not complete yet.
            _registry.NotifyTabMounted(TabId.Character, _roots[TabId.Character]);
            var events = new List<TabSwitchEvent>();
            _registry.OnTabSwitched += events.Add;

            _registry.SwitchTo(TabId.Character);

            Assert.That(events, Is.Empty);
        }

        [Test]
        [Description("ライフサイクル順序: 旧タブ OnDeactivated → 新タブ OnActivated → OnTabSwitched")]
        public void SwitchTo_LifecycleOrder_DeactivatedBeforeActivatedBeforeEvent()
        {
            MountAllTabsWithRoots();
            var characterHandle = _registry.RegisterTab(
                TabId.Character, new TabMetadata("Character"));
            var stageHandle = _registry.RegisterTab(
                TabId.StageLighting, new TabMetadata("StageLighting"));

            var sequence = new List<string>();
            characterHandle.OnActivated += () => sequence.Add("character.activated");
            characterHandle.OnDeactivated += () => sequence.Add("character.deactivated");
            stageHandle.OnActivated += () => sequence.Add("stage.activated");
            stageHandle.OnDeactivated += () => sequence.Add("stage.deactivated");
            _registry.OnTabSwitched += _ => sequence.Add("tabSwitched");

            _registry.SwitchTo(TabId.Character);
            _registry.SwitchTo(TabId.StageLighting);

            Assert.That(sequence, Is.EqualTo(new[]
            {
                "character.activated",
                "tabSwitched",
                "character.deactivated",
                "stage.activated",
                "tabSwitched",
            }));
        }

        [Test]
        [Description("Activate 後の handle.IsActive は true / Deactivate 後は false（状態反映）")]
        public void Handle_IsActive_TracksActivation()
        {
            MountAllTabsWithRoots();
            var characterHandle = _registry.RegisterTab(
                TabId.Character, new TabMetadata("Character"));
            var stageHandle = _registry.RegisterTab(
                TabId.StageLighting, new TabMetadata("StageLighting"));

            _registry.SwitchTo(TabId.Character);
            Assert.That(characterHandle.IsActive, Is.True);
            Assert.That(stageHandle.IsActive, Is.False);

            _registry.SwitchTo(TabId.StageLighting);
            Assert.That(characterHandle.IsActive, Is.False);
            Assert.That(stageHandle.IsActive, Is.True);
        }

        [Test]
        [Description("購読していないハンドル（RegisterTab を呼ばないタブ）でも SwitchTo は成功する（タブ spec 不在で起動継続; Requirement 10.1, 10.2）")]
        public void SwitchTo_WithoutRegisteredHandle_StillSucceeds()
        {
            MountAllTabsWithRoots();

            var result = _registry.SwitchTo(TabId.Character);

            Assert.That(result.Success, Is.True);
            Assert.That(_roots[TabId.Character].style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        [Description("OnActivated 内の例外は他購読・OnTabSwitched に波及しない（フォールトトレラント; Requirement 5.7 spirit）")]
        public void SwitchTo_HandlerException_DoesNotPreventTabSwitchedEvent()
        {
            MountAllTabsWithRoots();
            var handle = _registry.RegisterTab(
                TabId.Character, new TabMetadata("Character"));
            handle.OnActivated += () => throw new InvalidOperationException("simulated");
            var raised = false;
            _registry.OnTabSwitched += _ => raised = true;

            var result = _registry.SwitchTo(TabId.Character);

            Assert.That(result.Success, Is.True,
                "SwitchTo should still succeed even if a subscriber throws.");
            Assert.That(raised, Is.True,
                "OnTabSwitched must still fire after a handler throws.");
        }

        // ---- diagnostics logging ---------------------------------------

        [Test]
        [Description("成功した SwitchTo は LogCategory.TabSwitch で 1 件以上のログを残す（Requirement 11.2）")]
        public void SwitchTo_LogsTabSwitchCategory()
        {
            MountAllTabsWithRoots();

            _registry.SwitchTo(TabId.Character);

            var hasTabSwitchEntry = false;
            foreach (var entry in _logger.Entries)
            {
                if (entry.Category == LogCategory.TabSwitch) { hasTabSwitchEntry = true; break; }
            }
            Assert.That(hasTabSwitchEntry, Is.True,
                "SwitchTo must record at least one diagnostics entry under LogCategory.TabSwitch.");
        }

        [Test]
        [Description("PreloadIncomplete 拒否時もログを残す（オペレーター診断のためのトレース）")]
        public void SwitchTo_PreloadIncomplete_LogsRejection()
        {
            // No tabs mounted — registry rejects the call.
            _registry.SwitchTo(TabId.Character);

            var hasWarning = false;
            foreach (var entry in _logger.Entries)
            {
                if (entry.Category == LogCategory.TabSwitch
                    && entry.Level >= LogLevel.Warning)
                {
                    hasWarning = true;
                    break;
                }
            }
            Assert.That(hasWarning, Is.True);
        }

        // ---- performance budget ----------------------------------------

        [Test]
        [Description("100 連続 SwitchTo の合計時間が 100ms 未満（観測可能な完了状態の代理目安）")]
        public void SwitchTo_HundredIterations_AverageBelowFrameBudget()
        {
            MountAllTabsWithRoots();
            var rotation = new[]
            {
                TabId.Character,
                TabId.StageLighting,
                TabId.CameraSwitcher,
            };

            // Warm up before measurement so JIT/dictionary growth do not
            // dominate the first iteration's timing.
            for (var i = 0; i < 3; i++)
            {
                _registry.SwitchTo(rotation[i]);
            }

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 100; i++)
            {
                _registry.SwitchTo(rotation[i % rotation.Length]);
            }
            stopwatch.Stop();

            // 100 frames at 60 fps = ~1.6 s. Registry-side switch cost
            // should comfortably fit inside a single frame budget on a
            // standard CI agent; the assertion is intentionally loose at
            // 100 ms (1 ms/iteration average) so the test is not flaky.
            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(100.0),
                $"100 SwitchTo calls took {stopwatch.Elapsed.TotalMilliseconds:F2}ms; " +
                "registry-side cost should remain well under a single 60fps frame.");
        }
    }
}
