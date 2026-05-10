#nullable enable
using System;
using System.Collections.Generic;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Read-only snapshot of tab pre-load progress, surfaced through
    /// <c>ITabPanelRegistry.GetPreloadProgress()</c> and aggregated into
    /// <c>ShellDiagnosticsSnapshot</c>. Failed tabs are reported but do not block the
    /// other tabs from completing pre-load (Requirement 3.5).
    /// </summary>
    public readonly struct PreloadProgress
    {
        public PreloadProgress(int loadedCount, int totalCount, IReadOnlyList<TabId>? failedTabs)
        {
            LoadedCount = loadedCount;
            TotalCount = totalCount;
            FailedTabs = failedTabs ?? Array.Empty<TabId>();
        }

        public int LoadedCount { get; }

        public int TotalCount { get; }

        public IReadOnlyList<TabId> FailedTabs { get; }
    }
}
