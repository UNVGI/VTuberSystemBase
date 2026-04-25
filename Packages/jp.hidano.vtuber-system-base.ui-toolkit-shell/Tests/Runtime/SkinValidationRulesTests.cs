#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Tests.Runtime
{
    /// <summary>
    /// Task 6.2: <see cref="SkinValidationRules"/> contract tests. Pin the
    /// <c>vsb-</c> prefix + BEM naming convention and the per-tab required
    /// selector lists so <c>SkinValidator</c> (task 6.3) can rely on a single
    /// source of truth (Requirement 6.1, 6.2; design.md §Skin §SkinValidator).
    /// </summary>
    [TestFixture]
    public sealed class SkinValidationRulesTests
    {
        [Test]
        [Description("vsb- プレフィクスが定数として公開されている（命名規約の起点）")]
        public void Prefix_Constant_Equals_Vsb()
        {
            Assert.That(SkinValidationRules.Prefix, Is.EqualTo("vsb-"));
        }

        [Test]
        [Description("Root セクションの必須クラス名が vsb- プレフィクス + BEM 風で固定されている")]
        public void RootSection_Constants_FollowConvention()
        {
            Assert.That(SkinValidationRules.Root.TabBar, Is.EqualTo("vsb-tab-bar"));
            Assert.That(SkinValidationRules.Root.TabBarButton, Is.EqualTo("vsb-tab-bar__button"));
            Assert.That(SkinValidationRules.Root.TabBarButtonActive,
                Is.EqualTo("vsb-tab-bar__button--active"));
            Assert.That(SkinValidationRules.Root.TabBarButtonDisabled,
                Is.EqualTo("vsb-tab-bar__button--disabled"));
            Assert.That(SkinValidationRules.Root.NotificationBar,
                Is.EqualTo("vsb-notification-bar"));
        }

        [Test]
        [Description("各タブの TabRoot 共通クラスは vsb-tab-root で固定（design.md §SkinValidator）")]
        public void EveryTab_Exposes_SharedTabRoot_Constant()
        {
            Assert.That(SkinValidationRules.CharacterTab.TabRoot, Is.EqualTo("vsb-tab-root"));
            Assert.That(SkinValidationRules.StageLightingTab.TabRoot, Is.EqualTo("vsb-tab-root"));
            Assert.That(SkinValidationRules.CameraSwitcherTab.TabRoot, Is.EqualTo("vsb-tab-root"));
        }

        [Test]
        [Description("各タブには tab 識別 modifier が定義されている（BEM 修飾子）")]
        public void EveryTab_Exposes_TabIdentifyingModifier()
        {
            Assert.That(SkinValidationRules.CharacterTab.TabRootModifier,
                Is.EqualTo("vsb-tab-root--character"));
            Assert.That(SkinValidationRules.StageLightingTab.TabRootModifier,
                Is.EqualTo("vsb-tab-root--stage-lighting"));
            Assert.That(SkinValidationRules.CameraSwitcherTab.TabRootModifier,
                Is.EqualTo("vsb-tab-root--camera-switcher"));
        }

        [Test]
        [Description("RequiredRootClasses にタブバー・タブバーボタン・通知バーの必須クラスが含まれる")]
        public void RequiredRootClasses_Contains_AllRootStructuralSelectors()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    SkinValidationRules.Root.TabBar,
                    SkinValidationRules.Root.TabBarButton,
                    SkinValidationRules.Root.NotificationBar,
                },
                SkinValidationRules.RequiredRootClasses);
        }

        [Test]
        [Description("RequiredRootClasses は重複なし、全要素が vsb- プレフィクスを持つ")]
        public void RequiredRootClasses_Are_Unique_And_Prefixed()
        {
            var rules = SkinValidationRules.RequiredRootClasses;
            Assert.That(rules.Count, Is.EqualTo(rules.Distinct().Count()),
                "RequiredRootClasses must not contain duplicates");
            foreach (var s in rules)
            {
                Assert.That(s.StartsWith(SkinValidationRules.Prefix), Is.True,
                    $"'{s}' must start with the '{SkinValidationRules.Prefix}' prefix");
            }
        }

        [Test]
        [Description("RequiredCharacterTabClasses は TabRoot + Character 識別 modifier を含む")]
        public void RequiredCharacterTabClasses_Contains_RootAndModifier()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    SkinValidationRules.CharacterTab.TabRoot,
                    SkinValidationRules.CharacterTab.TabRootModifier,
                },
                SkinValidationRules.RequiredCharacterTabClasses);
        }

        [Test]
        [Description("RequiredStageLightingTabClasses は TabRoot + StageLighting 識別 modifier を含む")]
        public void RequiredStageLightingTabClasses_Contains_RootAndModifier()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    SkinValidationRules.StageLightingTab.TabRoot,
                    SkinValidationRules.StageLightingTab.TabRootModifier,
                },
                SkinValidationRules.RequiredStageLightingTabClasses);
        }

        [Test]
        [Description("RequiredCameraSwitcherTabClasses は TabRoot + CameraSwitcher 識別 modifier を含む")]
        public void RequiredCameraSwitcherTabClasses_Contains_RootAndModifier()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    SkinValidationRules.CameraSwitcherTab.TabRoot,
                    SkinValidationRules.CameraSwitcherTab.TabRootModifier,
                },
                SkinValidationRules.RequiredCameraSwitcherTabClasses);
        }

        [Test]
        [Description("RequiredTabClassesFor は TabId に対応するタブ別クラス一覧を返す（タブ別分離の単一参照点）")]
        public void RequiredTabClassesFor_Resolves_PerTabList()
        {
            Assert.That(SkinValidationRules.RequiredTabClassesFor(TabId.Character),
                Is.SameAs(SkinValidationRules.RequiredCharacterTabClasses));
            Assert.That(SkinValidationRules.RequiredTabClassesFor(TabId.StageLighting),
                Is.SameAs(SkinValidationRules.RequiredStageLightingTabClasses));
            Assert.That(SkinValidationRules.RequiredTabClassesFor(TabId.CameraSwitcher),
                Is.SameAs(SkinValidationRules.RequiredCameraSwitcherTabClasses));
        }

        [Test]
        [Description("未定義 TabId に対しては ArgumentOutOfRangeException を投げる（防御的契約）")]
        public void RequiredTabClassesFor_UnknownTabId_Throws()
        {
            var invalid = (TabId)9999;
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SkinValidationRules.RequiredTabClassesFor(invalid));
        }

        [Test]
        [Description("公開リストは IReadOnlyList<string> であり、書き換え不能")]
        public void PublicLists_AreReadOnly()
        {
            Assert.That(SkinValidationRules.RequiredRootClasses,
                Is.InstanceOf<IReadOnlyList<string>>());
            Assert.That(SkinValidationRules.RequiredCharacterTabClasses,
                Is.InstanceOf<IReadOnlyList<string>>());
            Assert.That(SkinValidationRules.RequiredStageLightingTabClasses,
                Is.InstanceOf<IReadOnlyList<string>>());
            Assert.That(SkinValidationRules.RequiredCameraSwitcherTabClasses,
                Is.InstanceOf<IReadOnlyList<string>>());
        }

        [Test]
        [Description("全 TabId の RequiredTabClassesFor 結果が vsb- プレフィクスで揃っている")]
        public void AllTabRequiredClasses_Are_Prefixed()
        {
            foreach (TabId tabId in Enum.GetValues(typeof(TabId)))
            {
                foreach (var s in SkinValidationRules.RequiredTabClassesFor(tabId))
                {
                    Assert.That(s.StartsWith(SkinValidationRules.Prefix), Is.True,
                        $"{tabId} required class '{s}' must start with '{SkinValidationRules.Prefix}'");
                }
            }
        }
    }
}
