#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Tests.TestSupport;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 12.1: representative consolidation of the
    /// <see cref="TabPanelRegistry"/> contracts most likely to break under
    /// future refactors — preload-completion judgment (success and degraded
    /// paths) and the "<c>style.display</c> swap only / VisualTreeAsset
    /// reference invariant" rule for tab switching. Exhaustive contract tests
    /// live in <c>TabPanelRegistryTests</c> (task 8.2) and
    /// <c>TabPanelRegistrySwitchToTests</c> (task 8.3); this fixture pins the
    /// minimum signal we need green in CI to guard Requirements 2.3, 2.4,
    /// 3.1, 3.5, 3.6, and 10.5.
    /// </summary>
    [TestFixture]
    public sealed class TabPanelRegistryRepresentativeTests
    {
        private RecordingDiagnosticsLogger _logger = null!;
        private TabPanelRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new RecordingDiagnosticsLogger();
            _registry = new TabPanelRegistry(_logger);
        }

        // ---- preload completion: success path --------------------------

        [Test]
        [Description("成功ケース: 3 タブ全 Mount で IsPreloadComplete==true, LoadedCount==3, FailedTabs 空 (Requirement 3.1)")]
        public void PreloadCompletion_AllThreeTabsMounted_IsCompleteWithoutFailures()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.NotifyTabMounted(TabId.StageLighting);
            _registry.NotifyTabMounted(TabId.CameraSwitcher);

            Assert.That(_registry.IsPreloadComplete, Is.True,
                "All three tabs mounted must complete preload.");
            var progress = _registry.GetPreloadProgress();
            Assert.That(progress.LoadedCount, Is.EqualTo(3));
            Assert.That(progress.TotalCount, Is.EqualTo(3));
            Assert.That(progress.FailedTabs, Is.Empty);
        }

        // ---- preload completion: degraded in-progress path -------------

        [Test]
        [Description("縮退ケース: 1 Mount + 1 Failure + 1 Pending で LoadedCount==2 / IsPreloadComplete==false / FailedTabs に該当 ID (Requirement 3.5)")]
        public void PreloadCompletion_OneMountedOneFailedOnePending_LoadedCountIsTwoAndStillProgressing()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.MarkTabFailed(TabId.StageLighting, "skin validation failed");
            // CameraSwitcher intentionally left Pending.

            var progress = _registry.GetPreloadProgress();
            Assert.That(progress.LoadedCount, Is.EqualTo(2),
                "1 mounted + 1 failed must resolve to LoadedCount == 2 " +
                "while the third tab is still pending.");
            Assert.That(progress.TotalCount, Is.EqualTo(3));
            Assert.That(progress.FailedTabs, Has.Member(TabId.StageLighting));
            Assert.That(progress.FailedTabs, Has.No.Member(TabId.Character));
            Assert.That(_registry.IsPreloadComplete, Is.False,
                "Preload must not yet be complete while one tab is still pending.");
        }

        [Test]
        [Description("縮退ケース継続: 残り Pending タブが Mount すると IsPreloadComplete==true, FailedTabs は維持される (Requirement 3.5)")]
        public void PreloadCompletion_DegradedFinalisesWithRemainingMount_IsCompleteWithFailureRecorded()
        {
            _registry.NotifyTabMounted(TabId.Character);
            _registry.MarkTabFailed(TabId.StageLighting, "skin validation failed");
            _registry.NotifyTabMounted(TabId.CameraSwitcher);

            Assert.That(_registry.IsPreloadComplete, Is.True,
                "Failed tabs must not block the rest of preload from completing.");
            var progress = _registry.GetPreloadProgress();
            Assert.That(progress.LoadedCount, Is.EqualTo(3));
            Assert.That(progress.FailedTabs, Has.Member(TabId.StageLighting));
            Assert.That(progress.FailedTabs.Count, Is.EqualTo(1));
        }

        // ---- SwitchTo: VisualTreeAsset reference invariance ------------

        [Test]
        [Description("SwitchTo は bind 済み VisualElement の参照を再 clone・差し替えしない: 複数回切替後も最初の参照と同一 (Requirement 2.4, 3.6)")]
        public void SwitchTo_PreservesBoundVisualElementReferences_AcrossMultipleSwitches()
        {
            var characterRoot = new VisualElement { name = "tab-character" };
            var stageRoot = new VisualElement { name = "tab-stage-lighting" };
            var cameraRoot = new VisualElement { name = "tab-camera-switcher" };

            _registry.NotifyTabMounted(TabId.Character, characterRoot);
            _registry.NotifyTabMounted(TabId.StageLighting, stageRoot);
            _registry.NotifyTabMounted(TabId.CameraSwitcher, cameraRoot);

            var rootsSnapshotBefore = _registry.SnapshotTabRoots();
            Assume.That(rootsSnapshotBefore[TabId.Character], Is.SameAs(characterRoot));
            Assume.That(rootsSnapshotBefore[TabId.StageLighting], Is.SameAs(stageRoot));
            Assume.That(rootsSnapshotBefore[TabId.CameraSwitcher], Is.SameAs(cameraRoot));

            // Drive a representative switch sequence covering every tab and
            // a back-and-forth so any reference re-binding would surface.
            Assume.That(_registry.SwitchTo(TabId.Character).Success, Is.True);
            Assume.That(_registry.SwitchTo(TabId.StageLighting).Success, Is.True);
            Assume.That(_registry.SwitchTo(TabId.CameraSwitcher).Success, Is.True);
            Assume.That(_registry.SwitchTo(TabId.Character).Success, Is.True);

            var rootsSnapshotAfter = _registry.SnapshotTabRoots();
            Assert.That(rootsSnapshotAfter[TabId.Character], Is.SameAs(characterRoot),
                "Character root reference must be invariant across switches.");
            Assert.That(rootsSnapshotAfter[TabId.StageLighting], Is.SameAs(stageRoot),
                "StageLighting root reference must be invariant across switches.");
            Assert.That(rootsSnapshotAfter[TabId.CameraSwitcher], Is.SameAs(cameraRoot),
                "CameraSwitcher root reference must be invariant across switches.");
        }

        [Test]
        [Description("SwitchTo は VisualElement の子要素・hierarchy を破壊しない (再 clone 禁止; Requirement 2.4, 3.6)")]
        public void SwitchTo_PreservesChildHierarchyOfBoundRoots()
        {
            var characterRoot = new VisualElement { name = "tab-character" };
            var stageRoot = new VisualElement { name = "tab-stage-lighting" };
            var cameraRoot = new VisualElement { name = "tab-camera-switcher" };
            var preservedChild = new VisualElement { name = "preserved-child" };
            stageRoot.Add(preservedChild);

            _registry.NotifyTabMounted(TabId.Character, characterRoot);
            _registry.NotifyTabMounted(TabId.StageLighting, stageRoot);
            _registry.NotifyTabMounted(TabId.CameraSwitcher, cameraRoot);

            _registry.SwitchTo(TabId.Character);
            _registry.SwitchTo(TabId.StageLighting);
            _registry.SwitchTo(TabId.Character);
            _registry.SwitchTo(TabId.StageLighting);

            Assert.That(stageRoot.childCount, Is.EqualTo(1),
                "SwitchTo must not re-parse / rebuild the bound visual tree.");
            Assert.That(stageRoot[0], Is.SameAs(preservedChild),
                "The originally bound child element must survive every switch.");
        }

        [Test]
        [Description("SwitchTo は style.display 以外を変更しない: opacity 等の style snapshot が不変 (Requirement 2.4, 3.6)")]
        public void SwitchTo_DoesNotMutateUnrelatedStyleFields()
        {
            var characterRoot = new VisualElement();
            var stageRoot = new VisualElement();
            var cameraRoot = new VisualElement();
            characterRoot.style.opacity = 0.42f;

            _registry.NotifyTabMounted(TabId.Character, characterRoot);
            _registry.NotifyTabMounted(TabId.StageLighting, stageRoot);
            _registry.NotifyTabMounted(TabId.CameraSwitcher, cameraRoot);

            _registry.SwitchTo(TabId.Character);
            _registry.SwitchTo(TabId.StageLighting);
            _registry.SwitchTo(TabId.Character);

            Assert.That(characterRoot.style.opacity.value, Is.EqualTo(0.42f),
                "SwitchTo must only mutate style.display — collateral style " +
                "fields belong exclusively to the bound element.");
        }

        [Test]
        [Description("プリロード完了から SwitchTo 連発まで一気通貫: ActiveTab・display 切替・参照不変が同時に成立する (Requirement 2.3, 2.4, 3.1, 3.6)")]
        public void PreloadThenSwitchSequence_HoldsAllRepresentativeContractsTogether()
        {
            var roots = new Dictionary<TabId, VisualElement>
            {
                { TabId.Character, new VisualElement() },
                { TabId.StageLighting, new VisualElement() },
                { TabId.CameraSwitcher, new VisualElement() },
            };
            foreach (var pair in roots)
            {
                _registry.NotifyTabMounted(pair.Key, pair.Value);
            }
            Assume.That(_registry.IsPreloadComplete, Is.True);

            Assert.That(_registry.SwitchTo(TabId.Character).Success, Is.True);
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.Character));
            Assert.That(roots[TabId.Character].style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
            Assert.That(roots[TabId.StageLighting].style.display.value,
                Is.EqualTo(DisplayStyle.None));

            Assert.That(_registry.SwitchTo(TabId.StageLighting).Success, Is.True);
            Assert.That(_registry.ActiveTab, Is.EqualTo(TabId.StageLighting));
            Assert.That(roots[TabId.Character].style.display.value,
                Is.EqualTo(DisplayStyle.None));
            Assert.That(roots[TabId.StageLighting].style.display.value,
                Is.EqualTo(DisplayStyle.Flex));

            // Reference invariance after the full sequence.
            var snapshot = _registry.SnapshotTabRoots();
            Assert.That(snapshot[TabId.Character], Is.SameAs(roots[TabId.Character]));
            Assert.That(snapshot[TabId.StageLighting], Is.SameAs(roots[TabId.StageLighting]));
            Assert.That(snapshot[TabId.CameraSwitcher], Is.SameAs(roots[TabId.CameraSwitcher]));
        }
    }
}
