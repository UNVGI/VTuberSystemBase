#nullable enable
using System.Collections.Generic;

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Public contract of the shell composition root (design.md §Bootstrap §UiShellBootstrapper;
    /// Requirements 1.1, 1.4, 3.1, 5.1, 8.1, 8.2, 9.1, 9.7).
    /// </summary>
    public interface IUiShellBootstrapper
    {
        /// <summary>
        /// Builds every shell subsystem in the order defined by <see cref="BootstrapStep"/>.
        /// Returns <see cref="BootstrapResult"/>; on failure the result carries the originating
        /// <see cref="BootstrapErrorCode"/> and any partial wiring already performed is rolled
        /// back via the same reverse-order disposal that <see cref="StopShell"/> applies.
        /// Calling <see cref="StartShell"/> while the shell is already running is a no-op and
        /// returns <see cref="BootstrapResult.Ok"/>.
        /// </summary>
        BootstrapResult StartShell(UiShellConfig config);

        /// <summary>
        /// Disposes every subsystem the previous <see cref="StartShell"/> built, in reverse
        /// initialisation order. Safe to call multiple times.
        /// </summary>
        void StopShell();

        bool IsRunning { get; }

        /// <summary>
        /// Ordered record of <see cref="BootstrapStep"/> values reached by the most recent
        /// <see cref="StartShell"/> call. Tests assert against this sequence to fix the
        /// initialisation order without coupling to implementation internals.
        /// </summary>
        IReadOnlyList<BootstrapStep> InitializationSteps { get; }
    }
}
