#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Service contract for the shell-owned registry that tracks per-tab
    /// preload state, exposes lifecycle handles to tab specs, and (in task
    /// 8.3) coordinates display switching. This task scope (8.2) covers the
    /// preload-completion judgment, registration, and disposal contracts.
    /// Display-switching members (e.g. <c>SwitchTo</c>, <c>ActiveTab</c>,
    /// <c>OnTabSwitched</c>) will extend this interface in task 8.3 once the
    /// <c>style.display</c> swap path is added.
    /// </summary>
    /// <remarks>
    /// All members must be invoked on the Unity main thread (design.md
    /// §State Management). Cross-thread calls throw
    /// <see cref="InvalidOperationException"/> rather than silently
    /// marshalling, since the registry's invariants are intrinsically
    /// single-threaded.
    /// </remarks>
    public interface ITabPanelRegistry
    {
        /// <summary>
        /// Total number of tabs the registry expects (always 3 in the current
        /// shell — Character / StageLighting / CameraSwitcher; design.md
        /// §Panels). Exposed so callers don't have to recompute it from
        /// <see cref="TabId"/>.
        /// </summary>
        int TotalTabCount { get; }

        /// <summary>
        /// Returns a value-copy snapshot of preload progress for diagnostics
        /// and tests (Requirement 3.7). Failed tabs are reported in
        /// <see cref="PreloadProgress.FailedTabs"/> but counted toward
        /// <see cref="PreloadProgress.LoadedCount"/> so the shell can finish
        /// booting even when one tab is degraded (Requirement 3.5).
        /// </summary>
        PreloadProgress GetPreloadProgress();

        /// <summary>
        /// True once every tab has either succeeded or failed at preload
        /// (Requirement 3.1; failed tabs are not blockers per Requirement
        /// 3.5).
        /// </summary>
        bool IsPreloadComplete { get; }

        /// <summary>
        /// Tab-spec entry point: registers the calling tab and returns a
        /// disposable lifecycle handle. Disposing the handle detaches every
        /// callback the tab subscribed via the handle (Requirement 5.7).
        /// Calling <c>RegisterTab</c> a second time for the same
        /// <paramref name="tabId"/> throws
        /// <see cref="InvalidOperationException"/> — the registry tolerates
        /// at most one live handle per tab.
        /// </summary>
        ITabLifecycleHandle RegisterTab(TabId tabId, TabMetadata metadata);

        /// <summary>
        /// Bootstrapper hook: signals that the named tab's UIDocument
        /// <c>OnEnable</c> has fired and its <c>rootVisualElement</c> is
        /// available. Idempotent — repeat calls are no-ops so that a
        /// re-entrant <c>OnEnable</c> does not double-count toward
        /// completion. Marking a tab as mounted after it was failed is also
        /// a no-op so the failure record wins.
        /// </summary>
        void NotifyTabMounted(TabId tabId);

        /// <summary>
        /// Bootstrapper / SkinValidator hook: marks the tab as failed for the
        /// remainder of the shell lifetime (Requirement 3.5). The tab still
        /// counts toward <see cref="IsPreloadComplete"/> so the shell can
        /// finish booting; <see cref="PreloadProgress.FailedTabs"/> records
        /// the id so the notification bar and diagnostics surface can warn.
        /// </summary>
        void MarkTabFailed(TabId tabId, string reason);

        /// <summary>
        /// Fires once per <see cref="PreloadOutcome"/> transition for any
        /// tab. Always raised on the Unity main thread (the only thread
        /// permitted to call into the registry). Subscribers are invoked in
        /// registration order; an exception thrown by a subscriber does not
        /// prevent later subscribers from running.
        /// </summary>
        event Action<PreloadEvent> OnPreloadChanged;
    }
}
