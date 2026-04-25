#nullable enable
namespace VTuberSystemBase.UiToolkitShell.AssetLoading
{
    /// <summary>
    /// Opaque handle returned by <see cref="IAsyncAssetLoader.LoadAsync{T}"/>.
    /// Holds the originating key/scope and exposes a coarse lifecycle state for diagnostics
    /// and a no-op-safe <see cref="Cancel"/> for cooperative cancellation.
    /// </summary>
    public interface IAssetLoadHandle
    {
        string AddressableKey { get; }

        string ScopeId { get; }

        AssetLoadState State { get; }

        /// <summary>
        /// Requests cancellation of an in-flight load. No-op if the handle has already completed,
        /// failed, been cancelled, or been released. When the cancellation is honoured, the
        /// originating <c>onCompleted</c> callback fires with <see cref="LoadErrorCode.Cancelled"/>.
        /// </summary>
        void Cancel();
    }

    public enum AssetLoadState
    {
        Pending,
        Completed,
        Failed,
        Cancelled,
        Released,
    }
}
