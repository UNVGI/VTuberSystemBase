#nullable enable
using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Production <see cref="IAddressablesInitializer"/> backed by
    /// <see cref="Addressables.InitializeAsync()"/>. The Addressables operation's
    /// <c>Completed</c> event is documented to fire on the Unity main thread, which
    /// satisfies the bootstrap contract that the result callback is delivered without
    /// thread marshalling (Requirement 4.3 / 11.3 alignment).
    /// </summary>
    /// <remarks>
    /// Two failure surfaces are translated into <see cref="AddressablesInitResult.Fail"/>:
    /// (1) a synchronous exception thrown from <c>InitializeAsync()</c> itself (rare; would
    /// indicate Addressables is misconfigured to the point that scheduling the operation
    /// fails), and (2) the asynchronous <c>AsyncOperationStatus.Failed</c> outcome reported
    /// via the operation handle. Both paths surface as <see cref="BootstrapErrorCode.AddressablesInitFailed"/>
    /// once they reach <see cref="AddressablesBootstrap"/>.
    /// </remarks>
    public sealed class AddressablesInitializer : IAddressablesInitializer
    {
        public void InitializeAsync(Action<AddressablesInitResult> onCompleted)
        {
            if (onCompleted is null) throw new ArgumentNullException(nameof(onCompleted));

            AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> handle;
            try
            {
                handle = Addressables.InitializeAsync();
            }
            catch (Exception ex)
            {
                onCompleted(AddressablesInitResult.Fail(ex,
                    $"Addressables.InitializeAsync threw before scheduling: {ex.GetType().Name}: {ex.Message}"));
                return;
            }

            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    onCompleted(AddressablesInitResult.Ok());
                }
                else
                {
                    var ex = op.OperationException;
                    onCompleted(AddressablesInitResult.Fail(ex,
                        ex?.Message ?? "Addressables.InitializeAsync reported AsyncOperationStatus.Failed"));
                }
            };
        }
    }
}
