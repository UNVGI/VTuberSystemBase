#nullable enable
using System;
using UnityEngine.UIElements;

namespace VTuberSystemBase.CameraSwitcherTab.View
{
    /// <summary>
    /// Centralised lookup constants + a tiny <see cref="RequireByName"/> helper
    /// so every Presenter resolves the same region containers under the root
    /// UXML by name (mirrors <c>character-selection-tab.ViewQueryHelpers</c>).
    /// </summary>
    public static class ViewQueryHelpers
    {
        public const string Root = "vsb-cam-tab";
        public const string PreviewActiveRegion = "vsb-cam-tab__preview-active";
        public const string PreviewMultiRegion = "vsb-cam-tab__preview-multi";
        public const string CameraListRegion = "vsb-cam-tab__camera-list";
        public const string VolumeEditorRegion = "vsb-cam-tab__volume-editor";
        public const string PresetPanelRegion = "vsb-cam-tab__preset-panel";
        public const string DiagnosticsRegion = "vsb-cam-tab__diagnostics";

        public static VisualElement RequireByName(VisualElement root, string name)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name is empty", nameof(name));
            var ve = root.Q<VisualElement>(name);
            if (ve is null)
                throw new InvalidOperationException(
                    $"VisualElement '{name}' not found under '{root.name}'. Did the UXML structure drift?");
            return ve;
        }
    }
}
