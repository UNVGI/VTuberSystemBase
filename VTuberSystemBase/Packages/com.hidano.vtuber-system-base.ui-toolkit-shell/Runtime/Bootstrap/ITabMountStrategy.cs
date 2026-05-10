#nullable enable
using System;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Pluggable strategy for materialising the three tab UIDocuments and binding their
    /// <c>rootVisualElement</c>s into <see cref="ITabPanelRegistry"/>. Decoupling tab mounting
    /// from <c>UiShellBootstrapper</c> keeps the composition root testable in EditMode (where
    /// real <c>UIDocument</c> GameObjects are awkward to script) and lets the
    /// <c>runtime-display-selector-integration</c> spec drop in a customised strategy without
    /// rewiring the bootstrap.
    /// </summary>
    /// <remarks>
    /// Implementations must call <see cref="ITabPanelRegistry.NotifyTabMounted(TabId, VisualElement)"/>
    /// for every tab that successfully attached and <see cref="ITabPanelRegistry.MarkTabFailed"/>
    /// for any tab that could not attach but should not be treated as a fatal bootstrap error
    /// (Requirement 3.5). Returning <c>false</c> from <see cref="MountTabs"/> causes
    /// <c>UiShellBootstrapper.StartShell</c> to return
    /// <see cref="BootstrapErrorCode.TabUxmlAttachFailed"/>; throwing has the same effect with
    /// the exception message captured into the result detail.
    /// </remarks>
    public interface ITabMountStrategy
    {
        bool MountTabs(TabMountContext context);
    }

    /// <summary>
    /// Container for the artefacts an <see cref="ITabMountStrategy"/> needs in order to
    /// instantiate tab UIDocuments. Construction by <c>UiShellBootstrapper</c> only.
    /// </summary>
    public sealed class TabMountContext
    {
        public TabMountContext(
            ITabPanelRegistry registry,
            UnityEngine.UIElements.PanelSettings panelSettings,
            UiToolkitShellSkinProfile skinProfile,
            VisualElement rootVisualElement,
            IDiagnosticsLogger logger)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            PanelSettings = panelSettings ?? throw new ArgumentNullException(nameof(panelSettings));
            SkinProfile = skinProfile ?? throw new ArgumentNullException(nameof(skinProfile));
            RootVisualElement = rootVisualElement ?? throw new ArgumentNullException(nameof(rootVisualElement));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ITabPanelRegistry Registry { get; }
        public UnityEngine.UIElements.PanelSettings PanelSettings { get; }
        public UiToolkitShellSkinProfile SkinProfile { get; }

        /// <summary>
        /// The root <see cref="VisualElement"/> of the shell's root UIDocument. Strategies that
        /// represent tabs as descendants of this tree (e.g. test fakes that bypass real
        /// UIDocument creation) attach there; the production strategy creates separate
        /// UIDocuments sharing <see cref="PanelSettings"/> and only uses this element to
        /// register the synthetic per-tab roots used for switch tracking.
        /// </summary>
        public VisualElement RootVisualElement { get; }

        public IDiagnosticsLogger Logger { get; }
    }
}
