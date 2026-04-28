#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Outcome envelope returned by <c>ITabPanelRegistry.SwitchTo(TabId)</c>
    /// (design.md §Panels §TabPanelRegistry; Requirement 2.3, 2.4, 2.5, 2.6,
    /// 2.7, 2.8). The struct is intentionally <c>readonly</c> so the value
    /// can be copied freely between subsystems (TabBarController, diagnostics
    /// snapshot) without aliasing the registry's internal state.
    /// </summary>
    /// <remarks>
    /// On success, <see cref="Success"/> is <c>true</c> and <see cref="Error"/>
    /// is <c>null</c>. On failure, <see cref="Success"/> is <c>false</c> and
    /// <see cref="Error"/> carries one of the three known failure modes
    /// catalogued by <see cref="SwitchErrorCode"/>. Callers should match on
    /// <see cref="Error"/> rather than relying on the absence of a side effect
    /// (e.g. the active tab pointer is only mutated when <c>Success</c> is
    /// <c>true</c>).
    /// </remarks>
    public readonly struct SwitchResult
    {
        private SwitchResult(bool success, SwitchErrorCode? error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }

        public SwitchErrorCode? Error { get; }

        public static SwitchResult Ok() => new SwitchResult(true, null);

        public static SwitchResult Failed(SwitchErrorCode error) =>
            new SwitchResult(false, error);
    }

    /// <summary>
    /// Reason a <c>SwitchTo</c> request was rejected.
    /// <list type="bullet">
    /// <item><description><see cref="PreloadIncomplete"/>: the registry has
    /// not yet observed all three tabs as mounted-or-failed (Requirement 2.7,
    /// 3.1).</description></item>
    /// <item><description><see cref="TabDisabled"/>: the requested tab was
    /// recorded as failed by the bootstrapper or skin validator and is held
    /// in the disabled state (Requirement 3.5, 6.6).</description></item>
    /// <item><description><see cref="AlreadyActive"/>: the requested tab is
    /// already the active tab; the registry refuses to re-fire activation
    /// events to avoid surprising tab specs that subscribe via
    /// <c>ITabLifecycleHandle.OnActivated</c>.</description></item>
    /// </list>
    /// </summary>
    public enum SwitchErrorCode
    {
        PreloadIncomplete,
        TabDisabled,
        AlreadyActive,
    }
}
