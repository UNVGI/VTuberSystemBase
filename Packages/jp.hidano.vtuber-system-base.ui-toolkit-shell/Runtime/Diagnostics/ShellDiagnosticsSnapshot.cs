#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.UiToolkitShell.Diagnostics
{
    /// <summary>
    /// Aggregated, read-only snapshot of the shell's diagnostic state at a single moment.
    /// Produced by <see cref="IShellDiagnosticsSnapshotProvider.Capture"/> and consumed by
    /// tests, the diagnostics surface, and external monitoring (Requirements 3.7, 4.9, 11.9).
    /// All members are value copies — the struct is safe to retain after the underlying
    /// subsystems have moved on.
    /// </summary>
    public readonly struct ShellDiagnosticsSnapshot
    {
        public ShellDiagnosticsSnapshot(
            PreloadProgress preload,
            AssetLoaderSnapshot assetLoad,
            ConnectionStatusCode connectionStatus,
            int activeSubscriptionCount,
            TabId activeTab,
            DateTimeOffset capturedAt)
        {
            Preload = preload;
            AssetLoad = assetLoad;
            ConnectionStatus = connectionStatus;
            ActiveSubscriptionCount = activeSubscriptionCount;
            ActiveTab = activeTab;
            CapturedAt = capturedAt;
        }

        public PreloadProgress Preload { get; }

        public AssetLoaderSnapshot AssetLoad { get; }

        public ConnectionStatusCode ConnectionStatus { get; }

        public int ActiveSubscriptionCount { get; }

        public TabId ActiveTab { get; }

        public DateTimeOffset CapturedAt { get; }
    }
}
