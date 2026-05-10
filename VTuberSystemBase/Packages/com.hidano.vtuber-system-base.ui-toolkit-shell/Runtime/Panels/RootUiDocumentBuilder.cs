#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using VTuberSystemBase.UiToolkitShell.Skin;

namespace VTuberSystemBase.UiToolkitShell.Panels
{
    /// <summary>
    /// Factory that materialises the single shared <see cref="PanelSettings"/>
    /// (<c>targetDisplay = 0</c>) and the root <see cref="UIDocument"/> the shell
    /// owns (design.md §Panels §RootUiDocumentBuilder; Requirement 1.1, 1.2,
    /// 1.3, 1.7). <c>UiShellBootstrapper</c> calls
    /// <see cref="CreateSharedPanelSettings"/> once during initialisation and
    /// hands the resulting asset to every tab <see cref="UIDocument"/> so that
    /// the shell never accidentally renders to Display 2+ (the main output
    /// surface).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Display 1 enforcement (Requirement 1.7).</b> A non-zero
    /// <paramref name="requestedTargetDisplay"/> is treated as a configuration
    /// mistake — a warning is recorded on
    /// <see cref="LogCategory.Lifecycle"/> and the value is forced back to 0.
    /// This keeps the "Display 1 only" guarantee structural rather than
    /// dependent on configuration discipline.
    /// </para>
    /// <para>
    /// The builder is intentionally thin: it does not validate the skin
    /// profile (that is <c>SkinValidator</c> / <c>UiToolkitShellSkinProfile</c>'s
    /// job) beyond requiring a non-null <see cref="UiToolkitShellSkinProfile.RootVisualTreeAsset"/>
    /// so that the resulting <see cref="UIDocument"/> has something to render.
    /// </para>
    /// </remarks>
    public sealed class RootUiDocumentBuilder
    {
        /// <summary>
        /// Name of the GameObject that hosts the root <see cref="UIDocument"/>.
        /// Surfaced as a constant so tests and lifecycle drivers can
        /// <c>Find</c> the host without leaking string literals.
        /// </summary>
        public const string DefaultRootGameObjectName = "VsbUiToolkitShellRoot";

        /// <summary>
        /// Name of the shared <see cref="PanelSettings"/> ScriptableObject
        /// instance. The instance is created in-memory via
        /// <see cref="ScriptableObject.CreateInstance{T}"/> rather than loaded
        /// from disk so the package ships without a hard-coded asset GUID
        /// dependency.
        /// </summary>
        public const string DefaultPanelSettingsName = "VsbUiToolkitShellPanelSettings";

        private readonly IDiagnosticsLogger _logger;

        public RootUiDocumentBuilder(IDiagnosticsLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates the single shared <see cref="PanelSettings"/> with
        /// <c>targetDisplay</c> forced to 0. A non-zero
        /// <paramref name="requestedTargetDisplay"/> emits a warning and is
        /// silently coerced to 0 (Requirement 1.7).
        /// </summary>
        public PanelSettings CreateSharedPanelSettings(int requestedTargetDisplay)
        {
            var effectiveDisplay = ForceDisplayZero(requestedTargetDisplay);
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = DefaultPanelSettingsName;
            panelSettings.targetDisplay = effectiveDisplay;
            return panelSettings;
        }

        /// <summary>
        /// Builds the root <see cref="UIDocument"/> hosted on a hidden
        /// GameObject and shares the supplied <paramref name="panelSettings"/>
        /// with it. The caller is responsible for handing the same
        /// <see cref="PanelSettings"/> instance to every tab UIDocument
        /// (design.md §Architecture: "PanelSettings 1 本を全 UIDocument で共有").
        /// </summary>
        public RootUiDocumentBuildResult Build(
            UiToolkitShellSkinProfile profile,
            PanelSettings panelSettings)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (panelSettings == null) throw new ArgumentNullException(nameof(panelSettings));
            if (profile.RootVisualTreeAsset == null)
            {
                throw new ArgumentException(
                    "UiToolkitShellSkinProfile.RootVisualTreeAsset must be assigned " +
                    "before RootUiDocumentBuilder.Build runs (Requirement 6.8).",
                    nameof(profile));
            }

            var hostGameObject = new GameObject(DefaultRootGameObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var uiDocument = hostGameObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = panelSettings;
            uiDocument.visualTreeAsset = profile.RootVisualTreeAsset;

            return new RootUiDocumentBuildResult(panelSettings, hostGameObject, uiDocument);
        }

        /// <summary>
        /// Convenience overload that creates the shared
        /// <see cref="PanelSettings"/> in one step.
        /// </summary>
        public RootUiDocumentBuildResult Build(
            UiToolkitShellSkinProfile profile,
            int requestedTargetDisplay)
        {
            var panelSettings = CreateSharedPanelSettings(requestedTargetDisplay);
            return Build(profile, panelSettings);
        }

        private int ForceDisplayZero(int requestedTargetDisplay)
        {
            if (requestedTargetDisplay == 0)
            {
                return 0;
            }

            _logger.Log(
                LogLevel.Warning,
                LogCategory.Lifecycle,
                $"RootUiDocumentBuilder: requested PanelSettings.targetDisplay=" +
                $"{requestedTargetDisplay} overridden to 0. The UI Toolkit Shell is " +
                "restricted to Display 1 (Requirement 1.7).");
            return 0;
        }
    }

    /// <summary>
    /// Tuple of artefacts produced by
    /// <see cref="RootUiDocumentBuilder.Build(UiToolkitShellSkinProfile, PanelSettings)"/>.
    /// All three are owned by the caller (typically <c>UiShellBootstrapper</c>)
    /// and must be disposed in reverse order during <c>StopShell</c>.
    /// </summary>
    public readonly struct RootUiDocumentBuildResult
    {
        public RootUiDocumentBuildResult(
            PanelSettings panelSettings,
            GameObject hostGameObject,
            UIDocument uiDocument)
        {
            PanelSettings = panelSettings;
            HostGameObject = hostGameObject;
            UIDocument = uiDocument;
        }

        public PanelSettings PanelSettings { get; }

        public GameObject HostGameObject { get; }

        public UIDocument UIDocument { get; }
    }
}
