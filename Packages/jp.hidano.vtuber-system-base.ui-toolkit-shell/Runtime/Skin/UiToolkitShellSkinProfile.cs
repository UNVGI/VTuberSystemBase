#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;

namespace VTuberSystemBase.UiToolkitShell.Skin
{
    /// <summary>
    /// Skin extension point as a Unity asset (design.md §Skin §UiToolkitShellSkinProfile).
    /// Holds the root UXML/USS and the per-tab UXML/USS the bootstrapper hands to
    /// <c>RootUiDocumentBuilder</c> and <c>TabPanelRegistry</c>. Users replace styling
    /// without forking the package by creating their own asset via the
    /// <c>Assets &gt; Create &gt; VTuberSystemBase / UI Toolkit Shell / Skin Profile</c>
    /// menu and assigning it to <c>UiShellConfig.SkinProfile</c> (Requirement 6.3, 6.4,
    /// 6.7, 6.8).
    /// </summary>
    /// <remarks>
    /// Per-tab <c>VisualTreeAsset</c> fields are nullable: if a tab spec has not yet
    /// been authored the bootstrapper falls back to <c>EmptyTabShell.uxml</c>
    /// (Requirement 10.2). Only <see cref="RootVisualTreeAsset"/> is mandatory because
    /// without it the tab bar and notification bar cannot be built; <see cref="Validate"/>
    /// reports that hard requirement as <see cref="BootstrapErrorCode.SkinProfileMissing"/>.
    /// </remarks>
    [CreateAssetMenu(
        menuName = CreateAssetMenuName,
        fileName = "UiToolkitShellSkinProfile.asset",
        order = 100)]
    public sealed class UiToolkitShellSkinProfile : ScriptableObject
    {
        public const string CreateAssetMenuName =
            "VTuberSystemBase/UI Toolkit Shell/Skin Profile";

        [Header("Root (tab bar + notification bar)")]
        public VisualTreeAsset? RootVisualTreeAsset;
        public List<StyleSheet> RootStyleSheets = new List<StyleSheet>();

        [Header("Tab: Character Selection")]
        public VisualTreeAsset? CharacterTabVisualTreeAsset;
        public List<StyleSheet> CharacterTabStyleSheets = new List<StyleSheet>();

        [Header("Tab: Stage Lighting Volume")]
        public VisualTreeAsset? StageLightingTabVisualTreeAsset;
        public List<StyleSheet> StageLightingTabStyleSheets = new List<StyleSheet>();

        [Header("Tab: Camera Switcher")]
        public VisualTreeAsset? CameraSwitcherTabVisualTreeAsset;
        public List<StyleSheet> CameraSwitcherTabStyleSheets = new List<StyleSheet>();

        [Header("Common UI library (optional USS override)")]
        public List<StyleSheet> CommonUiStyleSheets = new List<StyleSheet>();

        /// <summary>
        /// Surfaces the single hard precondition <c>UiShellBootstrapper</c> needs from a
        /// skin profile: a non-null <see cref="RootVisualTreeAsset"/>. Returns
        /// <see cref="BootstrapErrorCode.SkinProfileMissing"/> when the profile is
        /// itself null or when the root UXML reference is empty; returns <c>null</c>
        /// when validation passes (design.md §Skin Implementation Notes).
        /// </summary>
        public static BootstrapErrorCode? Validate(UiToolkitShellSkinProfile? profile)
        {
            if (profile == null)
            {
                return BootstrapErrorCode.SkinProfileMissing;
            }

            if (profile.RootVisualTreeAsset == null)
            {
                return BootstrapErrorCode.SkinProfileMissing;
            }

            return null;
        }
    }
}
