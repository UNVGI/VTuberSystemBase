#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.EditorTools
{
    /// <summary>
    /// Validates that a stage-lighting-volume tab UXML (the bundled default or a skin
    /// override) carries every required well-known element id. Missing ids are
    /// recorded into the supplied <see cref="IDiagnosticsLogger"/> so the shell
    /// notification bar can surface a warning at preload time.
    /// (Task 7.2, Requirements 1.8, 12.4, 12.6, 12.7.)
    /// </summary>
    public static class UxmlImportValidator
    {
        /// <summary>Element IDs the runtime queries; all must exist or the tab cannot bind.</summary>
        public static readonly IReadOnlyList<string> RequiredElementIds = new[]
        {
            "preview-panel",
            "preset-section",
            "stage-selection-section",
            "light-list-section",
            "light-editor-section",
            "volume-override-section",
        };

        public static IReadOnlyList<string> FindMissing(VisualElement root)
        {
            var missing = new List<string>();
            if (root is null)
            {
                missing.AddRange(RequiredElementIds);
                return missing;
            }
            foreach (var id in RequiredElementIds)
            {
                if (root.Q<VisualElement>(id) is null)
                    missing.Add(id);
            }
            return missing;
        }

        /// <summary>
        /// Convenience wrapper that logs a warning per missing id and returns true when
        /// the UXML is structurally valid for the runtime to bind.
        /// </summary>
        public static bool ValidateAndLog(VisualElement root, IDiagnosticsLogger? logger)
        {
            var missing = FindMissing(root);
            if (missing.Count == 0) return true;
            for (int i = 0; i < missing.Count; i++)
            {
                logger?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"StageLightingVolumeTab UXML missing required element '{missing[i]}'.",
                    new { elementId = missing[i] });
            }
            return false;
        }
    }
}
