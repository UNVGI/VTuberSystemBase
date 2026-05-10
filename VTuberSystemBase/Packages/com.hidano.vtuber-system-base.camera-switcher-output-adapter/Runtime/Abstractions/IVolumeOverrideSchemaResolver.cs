#nullable enable
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions
{
    /// <summary>
    /// Builds the <see cref="VolumeMetadataResponse"/> that backs
    /// <c>camera/{id}/volume/overrides/metadata</c> Request handling (CSO-11,
    /// Requirement 7). The response is built by Reflection over the URP
    /// <c>VolumeManager</c> registered <c>VolumeComponent</c> types.
    /// </summary>
    /// <remarks>
    /// Implementations MUST cache the response after the first invocation and return
    /// the same instance on subsequent calls (Requirement 7.5). Implementations MUST
    /// return an empty <see cref="VolumeMetadataResponse"/> on unrecoverable
    /// Reflection failures rather than throwing (Requirement 7.8).
    /// </remarks>
    public interface IVolumeOverrideSchemaResolver
    {
        /// <summary>Returns the (cached) override schema list for the running URP.</summary>
        VolumeMetadataResponse GetSchema();
    }
}
