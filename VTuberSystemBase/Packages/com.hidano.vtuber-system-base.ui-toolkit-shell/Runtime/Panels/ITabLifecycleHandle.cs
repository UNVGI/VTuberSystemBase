#nullable enable
using System;
using VTuberSystemBase.UiToolkitShell.AssetLoading;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Public lifecycle token returned by
    /// <c>ITabPanelRegistry.RegisterTab(TabId, TabMetadata)</c>. Tab specs hold
    /// the handle for the lifetime of their tab, subscribe to
    /// <see cref="OnActivated"/> / <see cref="OnDeactivated"/> for state save /
    /// restore hooks (Requirement 2.8), and call
    /// <see cref="IDisposable.Dispose"/> when the tab is being torn down so that
    /// the registry can detach all callbacks.
    /// <para>
    /// Disposal is the supported way for tab specs to release every callback
    /// they registered through this handle in a single step. Tab specs feed
    /// their <c>UiSubscriptionClient</c> tokens / <c>AddressablesAssetLoader</c>
    /// scope through <see cref="Track(IDisposable)"/> and
    /// <see cref="TrackAssetScope(IAsyncAssetLoader)"/> so that
    /// <see cref="IDisposable.Dispose"/> drains them all atomically. The shell
    /// also performs a backstop sweep during
    /// <c>UiShellBootstrapper.StopShell</c> (design.md §Risks; task 10.4) by
    /// force-disposing every live handle so a forgotten
    /// <see cref="IDisposable.Dispose"/> cannot leak subscriptions or asset
    /// scopes across PlayMode iterations (Requirement 2.8, 5.7).
    /// </para>
    /// </summary>
    public interface ITabLifecycleHandle : IDisposable
    {
        TabId TabId { get; }

        bool IsActive { get; }

        /// <summary>
        /// Stable scope identifier derived from <see cref="TabId"/>. Tab specs
        /// pass it as the <c>scopeId</c> argument when calling
        /// <c>IAsyncAssetLoader.LoadAsync</c> so that
        /// <see cref="TrackAssetScope(IAsyncAssetLoader)"/> can release every
        /// load the tab acquired in a single sweep.
        /// </summary>
        string ScopeId { get; }

        /// <summary>
        /// True after <see cref="IDisposable.Dispose"/> has run, whether the
        /// tab spec called it or the shell-side backstop did. Monotonic
        /// <c>false → true</c>.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Number of resources currently registered through
        /// <see cref="Track(IDisposable)"/> /
        /// <see cref="TrackAssetScope(IAsyncAssetLoader)"/> that have not yet
        /// been disposed. Exposed for diagnostics tests that pin the backstop
        /// contract (task 10.4).
        /// </summary>
        int TrackedResourceCount { get; }

        event Action OnActivated;

        event Action OnDeactivated;

        /// <summary>
        /// Registers a disposable for cleanup on
        /// <see cref="IDisposable.Dispose"/>. Tab specs typically pass their
        /// <c>UiSubscriptionClient</c> tokens here. After Dispose has fired
        /// further calls dispose the resource immediately so a late
        /// registration cannot survive the handle.
        /// </summary>
        void Track(IDisposable resource);

        /// <summary>
        /// Registers <paramref name="loader"/> so that
        /// <see cref="IDisposable.Dispose"/> calls
        /// <c>loader.ReleaseAll(ScopeId)</c>. Idempotent: registering the same
        /// loader twice is a no-op so tabs can call it from any number of
        /// initialisation paths.
        /// </summary>
        void TrackAssetScope(IAsyncAssetLoader loader);
    }
}
