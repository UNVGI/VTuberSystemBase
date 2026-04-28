#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.UiToolkitShell.Skin
{
    /// <summary>
    /// USS class naming convention and the canonical list of selectors that
    /// <c>SkinValidator</c> (task 6.3) checks against ルート / 各タブの
    /// <c>rootVisualElement</c> 階層 (Requirement 6.1, 6.2; design.md §Skin
    /// §SkinValidator).
    ///
    /// <para>
    /// <b>Naming convention</b>: <c>vsb-</c> プレフィクス + BEM 風
    /// (Block__Element--Modifier).
    /// </para>
    ///
    /// <list type="table">
    /// <listheader><term>Form</term><description>Pattern (例)</description></listheader>
    /// <item><term>Block</term><description><c>vsb-{block}</c>（例:
    /// <c>vsb-tab-bar</c>, <c>vsb-notification-bar</c>）</description></item>
    /// <item><term>Element</term><description><c>vsb-{block}__{element}</c>
    /// （例: <c>vsb-tab-bar__button</c>）</description></item>
    /// <item><term>Block modifier</term><description><c>vsb-{block}--{modifier}</c>
    /// （例: <c>vsb-tab-root--character</c>）</description></item>
    /// <item><term>Element modifier</term><description><c>vsb-{block}__{element}--{modifier}</c>
    /// （例: <c>vsb-tab-bar__button--active</c>,
    /// <c>vsb-tab-bar__button--disabled</c>）</description></item>
    /// </list>
    ///
    /// <para>
    /// 利用者プロジェクトのスキン USS は本クラスのクラス名群を起点としてセレクタを
    /// 書く想定であり、<b>ここで列挙したクラス名の改名・削除は SemVer major 相当の
    /// 破壊的変更</b> として扱う（design.md §Revalidation Trigger; UI-3 のリスク欄）。
    /// </para>
    ///
    /// <para>
    /// <c>SkinValidator</c> は <see cref="RequiredRootClasses"/> をルートパネルに対して、
    /// <see cref="RequiredTabClassesFor(TabId)"/> を各タブ <c>rootVisualElement</c>
    /// に対して必須クラスの存在検証を行う。State 系のクラス
    /// （<see cref="Root.TabBarButtonActive"/> / <see cref="Root.TabBarButtonDisabled"/>）
    /// は <c>TabBarController</c> が実行時に付け外しする運用クラスであり、
    /// 起動時検証の対象には含まれない。
    /// </para>
    /// </summary>
    public static class SkinValidationRules
    {
        /// <summary>
        /// All shell-defined USS class names start with this prefix
        /// (UI-3 / Requirement 6.2).
        /// </summary>
        public const string Prefix = "vsb-";

        /// <summary>
        /// Root-panel selectors that must exist on the UXML the
        /// <c>RootUiDocumentBuilder</c> attaches (tab bar + notification bar).
        /// </summary>
        public static class Root
        {
            public const string TabBar = "vsb-tab-bar";
            public const string TabBarButton = "vsb-tab-bar__button";
            public const string TabBarButtonActive = "vsb-tab-bar__button--active";
            public const string TabBarButtonDisabled = "vsb-tab-bar__button--disabled";
            public const string NotificationBar = "vsb-notification-bar";
        }

        /// <summary>
        /// Selectors required on the Character tab's <c>rootVisualElement</c>.
        /// </summary>
        public static class CharacterTab
        {
            public const string TabRoot = "vsb-tab-root";
            public const string TabRootModifier = "vsb-tab-root--character";
        }

        /// <summary>
        /// Selectors required on the Stage-Lighting tab's <c>rootVisualElement</c>.
        /// </summary>
        public static class StageLightingTab
        {
            public const string TabRoot = "vsb-tab-root";
            public const string TabRootModifier = "vsb-tab-root--stage-lighting";
        }

        /// <summary>
        /// Selectors required on the Camera-Switcher tab's <c>rootVisualElement</c>.
        /// </summary>
        public static class CameraSwitcherTab
        {
            public const string TabRoot = "vsb-tab-root";
            public const string TabRootModifier = "vsb-tab-root--camera-switcher";
        }

        /// <summary>
        /// Required classes on the root panel (tab bar / tab bar button /
        /// notification bar). State modifiers like
        /// <see cref="Root.TabBarButtonActive"/> are intentionally excluded
        /// because they are applied by <c>TabBarController</c> at runtime
        /// rather than authored in UXML.
        /// </summary>
        public static IReadOnlyList<string> RequiredRootClasses { get; } = new[]
        {
            Root.TabBar,
            Root.TabBarButton,
            Root.NotificationBar,
        };

        /// <summary>Required classes on the Character tab root.</summary>
        public static IReadOnlyList<string> RequiredCharacterTabClasses { get; } = new[]
        {
            CharacterTab.TabRoot,
            CharacterTab.TabRootModifier,
        };

        /// <summary>Required classes on the Stage-Lighting tab root.</summary>
        public static IReadOnlyList<string> RequiredStageLightingTabClasses { get; } = new[]
        {
            StageLightingTab.TabRoot,
            StageLightingTab.TabRootModifier,
        };

        /// <summary>Required classes on the Camera-Switcher tab root.</summary>
        public static IReadOnlyList<string> RequiredCameraSwitcherTabClasses { get; } = new[]
        {
            CameraSwitcherTab.TabRoot,
            CameraSwitcherTab.TabRootModifier,
        };

        /// <summary>
        /// Resolves the per-tab required class list. Used by <c>SkinValidator</c>
        /// to walk <c>IReadOnlyDictionary&lt;TabId, VisualElement&gt;</c>
        /// without branching on every tab id at the call site.
        /// </summary>
        public static IReadOnlyList<string> RequiredTabClassesFor(TabId tabId)
        {
            switch (tabId)
            {
                case TabId.Character:
                    return RequiredCharacterTabClasses;
                case TabId.StageLighting:
                    return RequiredStageLightingTabClasses;
                case TabId.CameraSwitcher:
                    return RequiredCameraSwitcherTabClasses;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(tabId), tabId, "Unknown TabId");
            }
        }
    }
}
