#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Thin abstraction over <c>UnityEngine.AddressableAssets.Addressables.InitializeAsync()</c>.
    /// Production code uses <see cref="AddressablesInitializer"/>; tests inject failures and
    /// control timing through <c>FakeAddressablesInitializer</c>. The abstraction exists so
    /// that <see cref="AddressablesBootstrap"/> can be exercised in EditMode without booting
    /// the real Addressables runtime (Requirement 10.7; design.md §AssetLoading
    /// §AddressablesAssetLoader Implementation Notes).
    /// </summary>
    public interface IAddressablesInitializer
    {
        /// <summary>
        /// Begins the Addressables initialization. <paramref name="onCompleted"/> is invoked
        /// exactly once on the Unity main thread when the underlying operation finishes.
        /// Implementations must not block the calling thread; any I/O is performed by
        /// Addressables on its own worker.
        /// </summary>
        void InitializeAsync(Action<AddressablesInitResult> onCompleted);
    }

    /// <summary>
    /// Outcome of <see cref="IAddressablesInitializer.InitializeAsync"/>. <see cref="Success"/>
    /// is the canonical discriminator; on failure the optional <see cref="Exception"/> carries
    /// the originating exception (if any) and <see cref="Detail"/> a short message that can be
    /// surfaced to the diagnostics log without leaking stack traces into the UI.
    /// </summary>
    public readonly struct AddressablesInitResult
    {
        private AddressablesInitResult(bool success, Exception? exception, string? detail)
        {
            Success = success;
            Exception = exception;
            Detail = detail;
        }

        public bool Success { get; }

        public Exception? Exception { get; }

        public string? Detail { get; }

        public static AddressablesInitResult Ok() => new AddressablesInitResult(true, null, null);

        public static AddressablesInitResult Fail(Exception? exception = null, string? detail = null)
            => new AddressablesInitResult(false, exception, detail);
    }
}
