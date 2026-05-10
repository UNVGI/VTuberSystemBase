#nullable enable
using System;
using UnityEngine.UIElements;

namespace VTuberSystemBase.CharacterSelectionTab.View
{
    /// <summary>
    /// Centralised <see cref="VisualElement"/> Query helpers for the
    /// Character Selection Tab. Presenters call <see cref="RequireByName"/>
    /// during construction to fail fast on a missing UXML element rather than
    /// silently no-op-ing during update. (task 4.1, design.md §View.)
    /// </summary>
    public static class ViewQueryHelpers
    {
        public const string TabRootName = "vsb-char-tab";
        public const string PresetBarRegion = "vsb-char-tab__preset-bar";
        public const string PlayerCardsRegion = "vsb-char-tab__player-cards";
        public const string AvatarCatalogRegion = "vsb-char-tab__avatar-catalog";
        public const string SettingsPanelRegion = "vsb-char-tab__settings-panel";
        public const string DiagnosticsRegion = "vsb-char-tab__diagnostics";

        /// <summary>
        /// Fetches the first descendant whose <c>name</c> equals
        /// <paramref name="name"/>. Throws when missing, with a message that
        /// names the offending region so the integrator can fix the UXML rather
        /// than chase a NullReferenceException later.
        /// </summary>
        public static VisualElement RequireByName(VisualElement root, string name)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
            var found = root.Q<VisualElement>(name);
            if (found is null)
            {
                throw new InvalidOperationException(
                    $"ViewQueryHelpers: required region '{name}' not found under '{root.name}'. " +
                    "Ensure the tab UXML matches CharacterTab.uxml.");
            }
            return found;
        }

        /// <summary>Optional descendant lookup; returns null when missing.</summary>
        public static VisualElement? FindByName(VisualElement root, string name)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
            return root.Q<VisualElement>(name);
        }
    }
}
