#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Panels;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Bootstrap
{
    /// <summary>
    /// Production <see cref="IRootUiDocumentFactory"/> that delegates to
    /// <see cref="RootUiDocumentBuilder"/> to create a real GameObject hosting a
    /// <see cref="UIDocument"/>. The notification bar host is resolved by walking the
    /// root visual element looking for the <c>vsb-notification-bar</c> class — falling
    /// back to the root itself when the skin omits the dedicated region so the
    /// notification controller still has a stable parent.
    /// </summary>
    public sealed class DefaultRootUiDocumentFactory : IRootUiDocumentFactory
    {
        public const string NotificationBarClassName = "vsb-notification-bar";

        public RootUiDocumentArtifacts Create(
            UiToolkitShellSkinProfile skinProfile,
            int requestedTargetDisplay,
            IDiagnosticsLogger logger)
        {
            if (skinProfile == null) throw new ArgumentNullException(nameof(skinProfile));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            var builder = new RootUiDocumentBuilder(logger);

            // The bootstrapper has already resolved this value through
            // IDisplayAssignmentStrategy (Requirement 1.6), so use it as-is rather than
            // routing through RootUiDocumentBuilder.CreateSharedPanelSettings (which
            // forces non-zero values back to 0 as a safety net for direct callers).
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = RootUiDocumentBuilder.DefaultPanelSettingsName;
            panelSettings.targetDisplay = requestedTargetDisplay;
            var build = builder.Build(skinProfile, panelSettings);

            var hostGameObject = build.HostGameObject;
            var uiDocument = build.UIDocument;
            var rootVisualElement = uiDocument.rootVisualElement
                ?? throw new InvalidOperationException(
                    "RootUiDocument did not produce a rootVisualElement. " +
                    "UIDocument.OnEnable must run before the bootstrapper accesses the tree.");

            var notificationHost = rootVisualElement.Q<VisualElement>(className: NotificationBarClassName)
                ?? rootVisualElement;

            Action dispose = () =>
            {
                if (hostGameObject != null)
                {
                    UnityEngine.Object.Destroy(hostGameObject);
                }
                if (build.PanelSettings != null)
                {
                    UnityEngine.Object.Destroy(build.PanelSettings);
                }
            };

            return new RootUiDocumentArtifacts(
                build.PanelSettings,
                rootVisualElement,
                notificationHost,
                dispose);
        }
    }
}
