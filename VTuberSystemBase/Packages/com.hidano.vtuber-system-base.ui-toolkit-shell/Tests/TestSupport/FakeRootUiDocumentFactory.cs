#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Bootstrap;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Tests.TestSupport
{
    /// <summary>
    /// EditMode-friendly <see cref="IRootUiDocumentFactory"/> that builds a precomposed
    /// in-memory <see cref="VisualElement"/> tree without spawning any GameObject. Lets
    /// <c>UiShellBootstrapperTests</c> exercise the composition root in batch mode without
    /// requiring a live <see cref="UnityEngine.UIElements.UIDocument"/>.
    /// </summary>
    public sealed class FakeRootUiDocumentFactory : IRootUiDocumentFactory
    {
        public Func<UiToolkitShellSkinProfile, int, IDiagnosticsLogger, RootUiDocumentArtifacts>? Override { get; set; }
        public bool ShouldThrow { get; set; }
        public Exception? ThrowException { get; set; }
        public PanelSettings? LastPanelSettings { get; private set; }
        public VisualElement? LastRoot { get; private set; }
        public VisualElement? LastNotificationHost { get; private set; }
        public UiToolkitShellSkinProfile? LastSkinProfile { get; private set; }
        public UnityEngine.UIElements.VisualTreeAsset? LastSkinRootVisualTreeAsset { get; private set; }
        public int CreateInvocationCount { get; private set; }
        public int DisposeInvocationCount { get; private set; }
        public int LastRequestedTargetDisplay { get; private set; }

        public RootUiDocumentArtifacts Create(
            UiToolkitShellSkinProfile skinProfile,
            int requestedTargetDisplay,
            IDiagnosticsLogger logger)
        {
            CreateInvocationCount++;
            LastRequestedTargetDisplay = requestedTargetDisplay;
            LastSkinProfile = skinProfile;
            LastSkinRootVisualTreeAsset = skinProfile?.RootVisualTreeAsset;

            if (ShouldThrow)
            {
                throw ThrowException ?? new InvalidOperationException("FakeRootUiDocumentFactory configured to throw");
            }

            if (Override is not null)
            {
                return Override(skinProfile, requestedTargetDisplay, logger);
            }

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "FakeRootPanelSettings";
            panelSettings.targetDisplay = requestedTargetDisplay;
            LastPanelSettings = panelSettings;

            var root = new VisualElement { name = "fake-root" };
            root.AddToClassList("vsb-shell-root");
            LastRoot = root;

            var tabBar = new VisualElement { name = "fake-tab-bar" };
            tabBar.AddToClassList("vsb-tab-bar");
            var charBtn = new Button { name = "vsb-tab-bar__button--character" };
            charBtn.AddToClassList("vsb-tab-bar__button");
            var stageBtn = new Button { name = "vsb-tab-bar__button--stage-lighting" };
            stageBtn.AddToClassList("vsb-tab-bar__button");
            var camBtn = new Button { name = "vsb-tab-bar__button--camera-switcher" };
            camBtn.AddToClassList("vsb-tab-bar__button");
            tabBar.Add(charBtn);
            tabBar.Add(stageBtn);
            tabBar.Add(camBtn);
            root.Add(tabBar);

            var notificationHost = new VisualElement { name = "fake-notification-bar" };
            notificationHost.AddToClassList("vsb-notification-bar");
            root.Add(notificationHost);
            LastNotificationHost = notificationHost;

            return new RootUiDocumentArtifacts(
                panelSettings,
                root,
                notificationHost,
                disposeAction: () =>
                {
                    DisposeInvocationCount++;
                    if (panelSettings != null)
                    {
                        UnityEngine.Object.DestroyImmediate(panelSettings);
                    }
                });
        }
    }
}
