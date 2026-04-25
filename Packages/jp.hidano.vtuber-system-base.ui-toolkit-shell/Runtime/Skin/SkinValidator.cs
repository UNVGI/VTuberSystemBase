#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.UiToolkitShell.Skin
{
    /// <summary>
    /// Walks <c>rootVisualElement</c> hierarchies after preload completion and reports any
    /// missing required USS class names defined by <see cref="SkinValidationRules"/>
    /// (Requirement 6.1, 6.2, 6.5, 6.6; design.md §Skin §SkinValidator).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The validator is <b>side-effect free</b> on the inputs: it reads class lists via
    /// <see cref="VisualElement.ClassListContains(string)"/> and UQuery, never adds or
    /// removes classes, and never marks tabs as failed itself. The caller
    /// (<c>UiShellBootstrapper</c>) inspects the returned <see cref="SkinValidationReport"/>
    /// and applies <c>TabPanelRegistry</c> failure marks based on it.
    /// </para>
    /// <para>
    /// The only side effect is logging: every issue is emitted to the injected
    /// <see cref="IDiagnosticsLogger"/> at <see cref="LogLevel.Error"/> with category
    /// <see cref="LogCategory.Skin"/>, so operators can diagnose UXML/skin breakage from
    /// either the Unity Console or the in-shell diagnostics surface.
    /// </para>
    /// </remarks>
    public sealed class SkinValidator : ISkinValidator
    {
        private readonly IDiagnosticsLogger _logger;

        public SkinValidator(IDiagnosticsLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public SkinValidationReport Validate(
            VisualElement rootPanel,
            IReadOnlyDictionary<TabId, VisualElement> tabRoots)
        {
            if (rootPanel is null) throw new ArgumentNullException(nameof(rootPanel));
            if (tabRoots is null) throw new ArgumentNullException(nameof(tabRoots));

            var issues = new List<SkinValidationIssue>();

            foreach (var className in SkinValidationRules.RequiredRootClasses)
            {
                if (HasClassInTree(rootPanel, className)) continue;
                AppendAndLog(issues, new SkinValidationIssue(
                    tabId: null,
                    missingSelector: className,
                    detail: $"Required root selector '{className}' is missing on the root panel."));
            }

            foreach (var pair in tabRoots)
            {
                var tabId = pair.Key;
                var tabRoot = pair.Value;

                if (tabRoot is null)
                {
                    AppendAndLog(issues, new SkinValidationIssue(
                        tabId: tabId,
                        missingSelector: string.Empty,
                        detail: $"Tab '{tabId}' rootVisualElement is null."));
                    continue;
                }

                foreach (var className in SkinValidationRules.RequiredTabClassesFor(tabId))
                {
                    if (HasClassInTree(tabRoot, className)) continue;
                    AppendAndLog(issues, new SkinValidationIssue(
                        tabId: tabId,
                        missingSelector: className,
                        detail: $"Required selector '{className}' is missing on tab '{tabId}'."));
                }
            }

            return new SkinValidationReport(issues);
        }

        private void AppendAndLog(List<SkinValidationIssue> issues, SkinValidationIssue issue)
        {
            issues.Add(issue);
            _logger.Log(LogLevel.Error, LogCategory.Skin, FormatLogMessage(issue));
        }

        private static string FormatLogMessage(SkinValidationIssue issue)
        {
            var scope = issue.TabId.HasValue ? $"Tab[{issue.TabId.Value}]" : "Root";
            return $"SkinValidationIssue scope={scope} missingSelector='{issue.MissingSelector}' detail='{issue.Detail}'";
        }

        private static bool HasClassInTree(VisualElement root, string className)
        {
            if (root.ClassListContains(className)) return true;
            return root.Q<VisualElement>(name: null, className: className) != null;
        }
    }

    /// <summary>
    /// Service contract for skin validation. Implementations walk a root panel and a
    /// per-tab dictionary of <c>rootVisualElement</c>s and return an aggregated report
    /// without mutating the inputs (design.md §Skin §SkinValidator).
    /// </summary>
    public interface ISkinValidator
    {
        SkinValidationReport Validate(
            VisualElement rootPanel,
            IReadOnlyDictionary<TabId, VisualElement> tabRoots);
    }

    /// <summary>
    /// Aggregate of every required selector that the validator could not locate.
    /// <see cref="AllValid"/> is the convenience flag for the bootstrapper's branch.
    /// </summary>
    public readonly struct SkinValidationReport
    {
        private readonly IReadOnlyList<SkinValidationIssue>? _issues;

        public SkinValidationReport(IReadOnlyList<SkinValidationIssue> issues)
        {
            _issues = issues ?? throw new ArgumentNullException(nameof(issues));
        }

        public bool AllValid => _issues is null || _issues.Count == 0;

        public IReadOnlyList<SkinValidationIssue> Issues =>
            _issues ?? Array.Empty<SkinValidationIssue>();
    }

    /// <summary>
    /// One missing-selector occurrence. <see cref="TabId"/> is <c>null</c> for root-panel
    /// issues (tab bar / notification bar) and set to the offending tab otherwise, so
    /// callers can map directly to <c>TabPanelRegistry.MarkFailed(tabId)</c>.
    /// </summary>
    public readonly struct SkinValidationIssue
    {
        public SkinValidationIssue(TabId? tabId, string missingSelector, string detail)
        {
            TabId = tabId;
            MissingSelector = missingSelector ?? throw new ArgumentNullException(nameof(missingSelector));
            Detail = detail ?? string.Empty;
        }

        public TabId? TabId { get; }

        public string MissingSelector { get; }

        public string Detail { get; }
    }
}
