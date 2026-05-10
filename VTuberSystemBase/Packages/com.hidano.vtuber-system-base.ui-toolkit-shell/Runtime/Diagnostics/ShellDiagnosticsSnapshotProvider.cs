#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.AssetLoading;
using VTuberSystemBase.UiToolkitShell.Commands;
using VTuberSystemBase.UiToolkitShell.Panels;

namespace VTuberSystemBase.UiToolkitShell.Diagnostics
{
    /// <summary>
    /// Default <see cref="IShellDiagnosticsSnapshotProvider"/> implementation. Each subsystem
    /// is supplied as a sampling delegate so the provider can be wired up before the
    /// concrete subsystems exist (Requirement 11.9, task 3.3). Production wiring binds these
    /// delegates to <c>ITabPanelRegistry</c>, <c>IAsyncAssetLoader</c>, <c>IConnectionStatus</c>,
    /// and <c>IUiSubscriptionClient</c> at <c>UiShellBootstrapper</c> composition time;
    /// tests inject lambdas that close over mutable test fixtures.
    /// </summary>
    public sealed class ShellDiagnosticsSnapshotProvider : IShellDiagnosticsSnapshotProvider
    {
        private readonly Func<PreloadProgress> _preload;
        private readonly Func<AssetLoaderSnapshot> _assetLoad;
        private readonly Func<ConnectionStatusCode> _connectionStatus;
        private readonly Func<int> _activeSubscriptionCount;
        private readonly Func<TabId> _activeTab;
        private readonly Func<DateTimeOffset> _clock;

        public ShellDiagnosticsSnapshotProvider(
            Func<PreloadProgress> preload,
            Func<AssetLoaderSnapshot> assetLoad,
            Func<ConnectionStatusCode> connectionStatus,
            Func<int> activeSubscriptionCount,
            Func<TabId> activeTab,
            Func<DateTimeOffset>? clock = null)
        {
            _preload = preload ?? throw new ArgumentNullException(nameof(preload));
            _assetLoad = assetLoad ?? throw new ArgumentNullException(nameof(assetLoad));
            _connectionStatus = connectionStatus ?? throw new ArgumentNullException(nameof(connectionStatus));
            _activeSubscriptionCount = activeSubscriptionCount ?? throw new ArgumentNullException(nameof(activeSubscriptionCount));
            _activeTab = activeTab ?? throw new ArgumentNullException(nameof(activeTab));
            _clock = clock ?? DefaultClock;
        }

        public ShellDiagnosticsSnapshot Capture()
        {
            return new ShellDiagnosticsSnapshot(
                preload: _preload(),
                assetLoad: _assetLoad(),
                connectionStatus: _connectionStatus(),
                activeSubscriptionCount: _activeSubscriptionCount(),
                activeTab: _activeTab(),
                capturedAt: _clock());
        }

        private static DateTimeOffset DefaultClock() => DateTimeOffset.UtcNow;
    }
}
