#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage
{
    /// <summary>
    /// Outcome of an <see cref="IInstantiationProvider.InstantiateAsync"/> call.
    /// </summary>
    public readonly struct InstantiationResult
    {
        public bool Success { get; init; }
        public GameObject? Instance { get; init; }
        /// <summary>One of <c>"not_found"</c>, <c>"load_failed"</c>, <c>"instantiate_failed"</c>.</summary>
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }

        public static InstantiationResult Ok(GameObject instance)
            => new InstantiationResult { Success = true, Instance = instance };

        public static InstantiationResult Fail(string errorCode, string errorMessage)
            => new InstantiationResult
            {
                Success = false,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
            };
    }

    /// <summary>
    /// Abstraction over Unity Addressables instantiate / release / catalog APIs. Production
    /// code uses <c>AddressablesInstantiationProvider</c>; tests inject <c>FakeInstantiationProvider</c>.
    /// </summary>
    public interface IInstantiationProvider
    {
        /// <summary>
        /// Instantiates the asset associated with <paramref name="addressableKey"/> as a
        /// child of <paramref name="parent"/>. Reference counting is owned by the provider:
        /// callers must use <see cref="ReleaseInstance"/> to release the asset.
        /// </summary>
        Task<InstantiationResult> InstantiateAsync(string addressableKey, Transform parent, CancellationToken ct = default);

        /// <summary>
        /// Releases an instance previously returned by <see cref="InstantiateAsync"/>.
        /// </summary>
        void ReleaseInstance(GameObject gameObject);

        /// <summary>
        /// Returns the primary keys of all resource locations registered under
        /// <paramref name="label"/> for the GameObject type.
        /// </summary>
        Task<IReadOnlyList<string>> LoadResourceLocationsAsync(string label, CancellationToken ct = default);
    }
}
