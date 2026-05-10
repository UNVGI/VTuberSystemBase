#nullable enable
using System;
using UnityEngine.UIElements;

namespace VTuberSystemBase.StageLightingVolumeTab.View
{
    /// <summary>
    /// Thin VisualElement-handle holder for the stage-lighting-volume tab. Resolves the
    /// well-known child elements from the cloned UXML once and surfaces them so each
    /// section view can subscribe to the ViewModel without duplicating Q&lt;&gt; queries.
    /// See design.md §View §StageLightingVolumeTabPanel (Requirements 1.1, 1.2, 1.4).
    /// </summary>
    public sealed class StageLightingVolumeTabPanel
    {
        public VisualElement Root { get; }
        public VisualElement PreviewPanel { get; }
        public VisualElement PresetSection { get; }
        public VisualElement StageSelectionSection { get; }
        public VisualElement LightListSection { get; }
        public VisualElement LightEditorSection { get; }
        public VisualElement VolumeOverrideSection { get; }
        public Label ActivePresetLabel { get; }

        public StageLightingVolumeTabPanel(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            PreviewPanel = Require(root, "preview-panel");
            PresetSection = Require(root, "preset-section");
            StageSelectionSection = Require(root, "stage-selection-section");
            LightListSection = Require(root, "light-list-section");
            LightEditorSection = Require(root, "light-editor-section");
            VolumeOverrideSection = Require(root, "volume-override-section");
            ActivePresetLabel = root.Q<Label>("active-preset-label")
                                ?? new Label();
        }

        private static VisualElement Require(VisualElement root, string name)
        {
            var el = root.Q<VisualElement>(name);
            if (el is null)
            {
                throw new InvalidOperationException(
                    $"StageLightingVolumeTab UXML is missing required element '{name}'. "
                    + "Skin overrides MUST keep this element id intact.");
            }
            return el;
        }

        public void Show()
        {
            Root.style.display = DisplayStyle.Flex;
            Root.style.visibility = Visibility.Visible;
        }

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
        }
    }
}
