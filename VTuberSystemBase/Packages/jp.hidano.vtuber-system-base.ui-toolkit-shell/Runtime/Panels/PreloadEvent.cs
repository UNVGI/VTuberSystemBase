#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Per-tab preload progress event. Surfaced through
    /// <c>ITabPanelRegistry.OnPreloadChanged</c> so that
    /// <c>TabBarController</c> can disable / enable buttons,
    /// <c>NotificationBarController</c> can warn on failures, and the
    /// diagnostics surface can record progress (Requirements 3.1, 3.3, 3.5,
    /// 11.1).
    /// </summary>
    public readonly struct PreloadEvent
    {
        public PreloadEvent(TabId tabId, PreloadOutcome outcome)
        {
            TabId = tabId;
            Outcome = outcome;
        }

        public TabId TabId { get; }

        public PreloadOutcome Outcome { get; }
    }

    /// <summary>
    /// Outcome of one tab's preload pipeline. <see cref="Started"/> fires the
    /// first time the registry sees the tab. <see cref="Succeeded"/> fires
    /// when the tab UIDocument has materialised its
    /// <c>rootVisualElement</c>. <see cref="Failed"/> fires when bootstrapper
    /// or skin validation rejects the tab — a failed tab still counts as
    /// "complete" for the global preload-completion judgment so the shell can
    /// finish booting (Requirement 3.5).
    /// </summary>
    public enum PreloadOutcome
    {
        Started,
        Succeeded,
        Failed,
    }
}
