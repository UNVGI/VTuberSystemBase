#nullable enable
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Test seam for the root UIDocument creation step. Production wiring uses
    /// <see cref="DefaultRootUiDocumentFactory"/>, which delegates to
    /// <see cref="RootUiDocumentBuilder"/> and creates a real GameObject with a
    /// <see cref="UnityEngine.UIElements.UIDocument"/> attached. EditMode tests inject a
    /// fake that returns a precomposed in-memory <see cref="UnityEngine.UIElements.VisualElement"/>
    /// tree so the bootstrapper can be exercised without spinning up GameObjects.
    /// </summary>
    public interface IRootUiDocumentFactory
    {
        RootUiDocumentArtifacts Create(
            UiToolkitShellSkinProfile skinProfile,
            int requestedTargetDisplay,
            IDiagnosticsLogger logger);
    }
}
