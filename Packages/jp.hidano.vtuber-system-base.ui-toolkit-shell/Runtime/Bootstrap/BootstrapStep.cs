#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Discrete steps recorded by <c>UiShellBootstrapper.StartShell</c> as initialisation
    /// progresses (design.md §UiShellBootstrapper Responsibilities &amp; Constraints — initialisation
    /// order). Tests assert against the recorded sequence to fix the order:
    /// <c>PanelSettings → RootUIDocument → TabUIDocuments → TabPanelRegistry/TabBarController →
    /// SkinValidator → AddressablesAssetLoader → UiCommandClient/UiSubscriptionClient →
    /// MainOutputStatusWatcher → IpcConnectionAttempt</c>.
    /// </summary>
    public enum BootstrapStep
    {
        ConfigValidated,
        DiagnosticsLoggerReady,
        CommonUiRegistered,
        PanelSettingsCreated,
        RootUiDocumentBuilt,
        TabUiDocumentsMounted,
        TabPanelRegistryReady,
        TabBarControllerReady,
        SkinValidated,
        AssetLoaderReady,
        AddressablesInitialized,
        ConnectionStatusReady,
        UiCommandClientReady,
        UiSubscriptionClientReady,
        NotificationBarControllerReady,
        MainOutputStatusWatcherReady,
        IpcConnectionAttempted,
        ShellRunning,
    }
}
