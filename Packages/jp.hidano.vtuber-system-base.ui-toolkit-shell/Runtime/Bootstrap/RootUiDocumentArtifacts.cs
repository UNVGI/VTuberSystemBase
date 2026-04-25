#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// The artefacts produced by an <see cref="IRootUiDocumentFactory"/> call: the shared
    /// <see cref="UnityEngine.UIElements.PanelSettings"/>, the root <see cref="VisualElement"/>
    /// (taken from the live <see cref="UIDocument"/> in production or freshly composed in
    /// tests), and the optional notification bar host element the bootstrapper hands to
    /// <see cref="VTuberSystemBase.UiToolkitShell.Diagnostics.NotificationBarController"/>.
    /// <see cref="DisposeAction"/> is invoked by <c>UiShellBootstrapper.StopShell</c> to
    /// release any GameObject the production factory created.
    /// </summary>
    public sealed class RootUiDocumentArtifacts : IDisposable
    {
        public RootUiDocumentArtifacts(
            PanelSettings panelSettings,
            VisualElement rootVisualElement,
            VisualElement notificationBarHost,
            Action? disposeAction = null)
        {
            PanelSettings = panelSettings ?? throw new ArgumentNullException(nameof(panelSettings));
            RootVisualElement = rootVisualElement ?? throw new ArgumentNullException(nameof(rootVisualElement));
            NotificationBarHost = notificationBarHost ?? throw new ArgumentNullException(nameof(notificationBarHost));
            DisposeAction = disposeAction;
        }

        public PanelSettings PanelSettings { get; }
        public VisualElement RootVisualElement { get; }
        public VisualElement NotificationBarHost { get; }
        public Action? DisposeAction { get; }

        public void Dispose() => DisposeAction?.Invoke();
    }
}
