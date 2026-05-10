#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Future-proofing hook (Requirement 1.6, design.md §UiShellBootstrapper.DisplayAssignmentHook).
    /// The current shell implementation pins <c>PanelSettings.targetDisplay = 0</c>; the
    /// <c>runtime-display-selector-integration</c> spec (#7) will plug in a strategy that
    /// chooses a different display when an external monitor is available.
    /// </summary>
    /// <remarks>
    /// The default implementation supplied by <c>UiShellBootstrapper</c> always returns 0 so
    /// the shell's "Display 1 only" guarantee remains structural for the present spec, while
    /// leaving the seam available for the later integration without re-shaping the bootstrap.
    /// </remarks>
    public interface IDisplayAssignmentStrategy
    {
        int ResolveTargetDisplay(int requested);
    }

    /// <summary>
    /// Default <see cref="IDisplayAssignmentStrategy"/> that always returns 0, irrespective of
    /// the <paramref name="requested"/> value. <see cref="Panels.RootUiDocumentBuilder"/> is
    /// responsible for emitting the warning log when a non-zero value is requested.
    /// </summary>
    public sealed class FixedDisplayZeroStrategy : IDisplayAssignmentStrategy
    {
        public static readonly FixedDisplayZeroStrategy Instance = new FixedDisplayZeroStrategy();

        public int ResolveTargetDisplay(int requested) => 0;
    }
}
