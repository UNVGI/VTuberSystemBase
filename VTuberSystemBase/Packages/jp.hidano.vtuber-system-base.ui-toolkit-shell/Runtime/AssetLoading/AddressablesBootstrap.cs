#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Bootstrap step that wraps <see cref="IAddressablesInitializer"/> and translates the
    /// outcome into a <see cref="BootstrapResult"/>. On failure it returns
    /// <see cref="BootstrapErrorCode.AddressablesInitFailed"/> so the surrounding
    /// <c>UiShellBootstrapper.StartShell</c> (Task 10.1) can abort startup safely without
    /// throwing into the Unity host (design.md §AssetLoading
    /// §AddressablesAssetLoader Implementation Notes; Requirement 4.1, 11.3, 9.1).
    /// </summary>
    /// <remarks>
    /// All four lifecycle events the task description calls out
    /// (<c>ロード開始/完了/失敗/アンロード</c>) are emitted to <see cref="LogCategory.AssetLoad"/>:
    /// the bootstrap covers the init-started / init-completed / init-failed legs, while
    /// the unload leg is handled by <see cref="AddressablesAssetLoader"/> itself when an
    /// underlying handle is released.
    /// </remarks>
    public sealed class AddressablesBootstrap
    {
        private readonly IAddressablesInitializer _initializer;
        private readonly IDiagnosticsLogger _logger;

        public AddressablesBootstrap(IAddressablesInitializer initializer, IDiagnosticsLogger logger)
        {
            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Begins Addressables initialization and reports the outcome via
        /// <paramref name="onCompleted"/>. Started/Completed/Failed events are written to
        /// <see cref="LogCategory.AssetLoad"/>. The callback fires exactly once on the
        /// Unity main thread.
        /// </summary>
        public void Initialize(Action<BootstrapResult> onCompleted)
        {
            if (onCompleted is null) throw new ArgumentNullException(nameof(onCompleted));

            _logger.Log(LogLevel.Info, LogCategory.AssetLoad, "AddressablesInitStarted");

            _initializer.InitializeAsync(initResult =>
            {
                if (initResult.Success)
                {
                    _logger.Log(LogLevel.Info, LogCategory.AssetLoad, "AddressablesInitCompleted");
                    onCompleted(BootstrapResult.Ok());
                    return;
                }

                var detail = initResult.Detail
                    ?? initResult.Exception?.Message
                    ?? "Addressables initialization failed without detail";
                _logger.Log(LogLevel.Error, LogCategory.AssetLoad,
                    $"AddressablesInitFailed detail={detail}");
                onCompleted(BootstrapResult.Fail(BootstrapErrorCode.AddressablesInitFailed, detail));
            });
        }
    }
}
