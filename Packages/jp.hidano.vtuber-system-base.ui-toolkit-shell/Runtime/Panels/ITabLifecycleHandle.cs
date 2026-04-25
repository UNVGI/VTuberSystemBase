#nullable enable
using System;

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
    /// they registered through this handle in a single step. The shell also
    /// performs a backstop sweep during <c>UiShellBootstrapper.StopShell</c>
    /// (design.md §Risks) to guarantee that a forgotten <see cref="IDisposable.Dispose"/>
    /// cannot leak subscriptions across PlayMode iterations.
    /// </para>
    /// </summary>
    public interface ITabLifecycleHandle : IDisposable
    {
        TabId TabId { get; }

        bool IsActive { get; }

        event Action OnActivated;

        event Action OnDeactivated;
    }
}
