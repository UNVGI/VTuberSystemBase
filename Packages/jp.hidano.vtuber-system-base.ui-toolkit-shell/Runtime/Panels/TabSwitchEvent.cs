#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Payload published by <c>ITabPanelRegistry.OnTabSwitched</c> after a
    /// successful <c>SwitchTo</c> call (design.md §Panels §TabPanelRegistry;
    /// Requirement 2.3, 2.5, 2.8, 11.2). The event fires synchronously on the
    /// Unity main thread within the same frame as the
    /// <c>style.display</c> swap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="From"/> is nullable so the very first switch — performed by
    /// <c>TabBarController</c> immediately after preload completion — can
    /// distinguish "no previous tab" from "switched away from a real tab".
    /// Subscribers such as the notification bar use this to suppress
    /// transition-only animations on the initial activation.
    /// </para>
    /// <para>
    /// <see cref="Duration"/> covers only the registry-side work: display
    /// swap, lifecycle handle invocation, and event dispatch. It excludes any
    /// tab-spec time spent inside <c>OnActivated</c>/<c>OnDeactivated</c> so
    /// the diagnostics figure remains a reliable indicator of shell-side cost
    /// (Requirement 11.2 logging surface).
    /// </para>
    /// </remarks>
    public readonly struct TabSwitchEvent
    {
        public TabSwitchEvent(TabId? from, TabId to, TimeSpan duration)
        {
            From = from;
            To = to;
            Duration = duration;
        }

        public TabId? From { get; }

        public TabId To { get; }

        public TimeSpan Duration { get; }
    }
}
