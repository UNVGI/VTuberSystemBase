using UnityEngine;

namespace VTuberSystemBase.StageLightingVolumeTab.Contracts
{
    /// <summary>
    /// Same-process Singleton accessor used to bridge the main-output-side
    /// <c>StagePreviewHost</c> (which owns the live <see cref="RenderTexture"/>) and the
    /// UI side that needs to render that texture into a <c>VisualElement</c>. The
    /// RenderTexture native handle cannot be marshalled over IPC, so this locator is
    /// the agreed-upon shared contract within a single Unity process.
    /// </summary>
    /// <remarks>
    /// Lifecycle:
    /// <list type="bullet">
    ///   <item><description>The host's <c>Awake</c> calls <see cref="Register"/>.</description></item>
    ///   <item><description>The host's <c>OnDestroy</c> calls <see cref="Unregister"/>.</description></item>
    ///   <item><description>The UI side reads <see cref="Current"/> and subscribes to <see cref="IPreviewHostService.RenderTextureChanged"/>.</description></item>
    /// </list>
    /// Duplicate <see cref="Register"/> calls log a warning and adopt the latest service.
    /// </remarks>
    public static class StagePreviewHostLocator
    {
        private static IPreviewHostService? _instance;

        /// <summary>
        /// Currently registered host service, or null when no host is active.
        /// </summary>
        public static IPreviewHostService? Current => _instance;

        /// <summary>
        /// Registers <paramref name="service"/> as the active host. If another service is
        /// already registered, the previous one is replaced and a warning is logged.
        /// </summary>
        public static void Register(IPreviewHostService service)
        {
            if (service is null)
            {
                Debug.LogError("[StagePreviewHostLocator] Register called with null service.");
                return;
            }

            if (_instance is not null && !ReferenceEquals(_instance, service))
            {
                Debug.LogWarning("[StagePreviewHostLocator] Replacing existing host service. Latest registration wins.");
            }

            _instance = service;
        }

        /// <summary>
        /// Unregisters <paramref name="service"/> if it is the current host. No-op when a
        /// different service has already taken over.
        /// </summary>
        public static void Unregister(IPreviewHostService service)
        {
            if (ReferenceEquals(_instance, service))
            {
                _instance = null;
            }
        }
    }
}
