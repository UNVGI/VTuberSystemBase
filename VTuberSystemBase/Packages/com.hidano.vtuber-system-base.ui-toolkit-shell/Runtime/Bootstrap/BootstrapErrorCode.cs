#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Error codes returned via <see cref="BootstrapResult"/> when the shell fails to start.
    /// design.md §Bootstrap §UiShellBootstrapper enumerates the five fatal startup paths;
    /// each task that owns a bootstrap step is responsible for surfacing its corresponding
    /// code without throwing, so that <c>UiShellBootstrapper.StartShell</c> can return the
    /// failure to its caller and abort startup safely.
    /// </summary>
    public enum BootstrapErrorCode
    {
        /// <summary>
        /// <c>UiShellConfig.SkinProfile</c> is null or its <c>RootVisualTreeAsset</c> is missing.
        /// </summary>
        SkinProfileMissing,

        /// <summary>
        /// The shared <c>PanelSettings</c> could not be assigned to the root <c>UIDocument</c>.
        /// </summary>
        PanelSettingsAssignFailed,

        /// <summary>
        /// One or more tab UXML assets could not be attached to the panel hierarchy.
        /// </summary>
        TabUxmlAttachFailed,

        /// <summary>
        /// <c>Addressables.InitializeAsync()</c> reported failure (Task 5.3, Requirement 4.1).
        /// </summary>
        AddressablesInitFailed,

        /// <summary>
        /// The <c>core-ipc-foundation</c> abstract client implementation could not be resolved
        /// or instantiated (Requirement 5.1, 9.1).
        /// </summary>
        IpcAbstractionUnavailable,
    }
}
