#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Identifies one of the three tabs hosted by ui-toolkit-shell. Values are stable and
    /// referenced by skin profile metadata, diagnostics snapshots, and tab-spec registration.
    /// See design.md §Panels for the full lifecycle.
    /// </summary>
    public enum TabId
    {
        Character,
        StageLighting,
        CameraSwitcher,
    }
}
