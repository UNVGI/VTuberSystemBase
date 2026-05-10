#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.CameraSwitcherTab.Contracts
{
    /// <summary>
    /// Port that resolves a <c>camera/{id}/preview/handle</c> textureKey into a
    /// concrete object usable by the UI (typically a <c>UnityEngine.RenderTexture</c>).
    /// </summary>
    /// <remarks>
    /// The implementation is expected to look up an entry in a Service Locator
    /// keyed by <see cref="PreviewHandleStatePayload.TextureKey"/>; the texture
    /// itself never travels over IPC. <see cref="ResolveAsync"/> returns a
    /// <see cref="PreviewHandleResolution"/> that is null on miss (no handle
    /// available) or carries the resolved handle on hit. The handle MAY be
    /// re-resolved after a <see cref="Release"/> so callers can rebind on
    /// re-attach.
    /// </remarks>
    public interface IPreviewHandleResolver
    {
        Task<PreviewHandleResolution> ResolveAsync(string textureKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Release the resolver's reference to the handle keyed by
        /// <paramref name="textureKey"/>. Idempotent.
        /// </summary>
        void Release(string textureKey);
    }

    /// <summary>Outcome of <see cref="IPreviewHandleResolver.ResolveAsync"/>.</summary>
    public sealed class PreviewHandleResolution
    {
        public PreviewHandleResolution(object? handle, bool found, string? failureDetail = null)
        {
            Handle = handle;
            Found = found;
            FailureDetail = failureDetail;
        }

        /// <summary>True if a handle was found.</summary>
        public bool Found { get; }

        /// <summary>The resolved handle (typically a <c>UnityEngine.RenderTexture</c>), or null on miss.</summary>
        public object? Handle { get; }

        /// <summary>Optional human-readable detail when <see cref="Found"/> is false.</summary>
        public string? FailureDetail { get; }

        public static PreviewHandleResolution Miss(string? detail = null)
            => new PreviewHandleResolution(null, false, detail);

        public static PreviewHandleResolution Hit(object handle)
            => new PreviewHandleResolution(handle, true);
    }
}
