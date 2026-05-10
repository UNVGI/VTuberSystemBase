#nullable enable
using System;
using UnityEngine.UIElements;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Service contract for the shell-owned registry that tracks per-tab
    /// preload state, exposes lifecycle handles to tab specs, and coordinates
    /// display switching via <c>style.display</c> only — VisualTreeAsset
    /// references are bound once during preload and never re-cloned for tab
    /// switches (Requirement 2.4, 3.6).
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
        /// Bootstrapper hook used in production: signals tab mount and binds
        /// the tab content's <see cref="VisualElement"/> root so subsequent
        /// <see cref="SwitchTo"/> calls can toggle <c>style.display</c>
        /// against the same instance (Requirement 2.4, 3.6). The element is
        /// initialised to <see cref="DisplayStyle.None"/> on bind so the tab
        /// remains hidden until <c>TabBarController</c> activates it.
        /// </summary>
        void NotifyTabMounted(TabId tabId, VisualElement rootVisualElement);

        /// <summary>
        /// Identifies the tab whose root <see cref="VisualElement"/> is
        /// currently visible. <c>null</c> until <see cref="SwitchTo"/>
        /// completes successfully for the first time — at startup the shell
        /// keeps every tab hidden so that an early-arriving IPC payload
        /// cannot leak a partially initialised tab (Requirement 3.2, 3.3).
        /// </summary>
        TabId? ActiveTab { get; }

        /// <summary>
        /// Switches the visible tab using only <c>style.display</c> swaps —
        /// <see cref="VisualElement"/> roots and the underlying VisualTreeAsset
        /// references are not touched (Requirement 2.4, 3.6). Returns
        /// <see cref="SwitchResult"/>; on failure the active tab is left
        /// unchanged. Completes synchronously on the Unity main thread and
        /// publishes <see cref="OnTabSwitched"/> within the same frame.
        /// </summary>
        SwitchResult SwitchTo(TabId target);

        /// <summary>
        /// Fires once per successful <see cref="SwitchTo"/> call after the
        /// <c>style.display</c> mutation and lifecycle-handle dispatch
        /// complete. Subscribers receive the elapsed registry-side duration
        /// for diagnostics logging (Requirement 11.2).
        /// </summary>
        event Action<TabSwitchEvent> OnTabSwitched;

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
