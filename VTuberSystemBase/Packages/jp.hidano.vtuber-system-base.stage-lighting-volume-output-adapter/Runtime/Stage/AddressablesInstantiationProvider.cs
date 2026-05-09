#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Exceptions;
using UnityEngine.ResourceManagement.ResourceLocations;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage
{
    /// <summary>
    /// Production <see cref="IInstantiationProvider"/> backed by Unity Addressables. Wraps
    /// <c>Addressables.InstantiateAsync</c>, <c>Addressables.ReleaseInstance</c>, and
    /// <c>Addressables.LoadResourceLocationsAsync</c> with structured error reporting and
    /// cancellation support.
    /// </summary>
    public sealed class AddressablesInstantiationProvider : IInstantiationProvider
    {
        private readonly AdapterLogger? _logger;

        public AddressablesInstantiationProvider() : this(null) { }
        internal AddressablesInstantiationProvider(AdapterLogger? logger)
        {
            _logger = logger;
        }

        public Task<InstantiationResult> InstantiateAsync(string addressableKey, Transform parent, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(addressableKey))
                return Task.FromResult(InstantiationResult.Fail("not_found", "addressableKey is null or empty"));

            var tcs = new TaskCompletionSource<InstantiationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            AsyncOperationHandle<GameObject> handle;
            try
            {
                handle = Addressables.InstantiateAsync(addressableKey, parent, instantiateInWorldSpace: false);
            }
            catch (InvalidKeyException ex)
            {
                return Task.FromResult(InstantiationResult.Fail("not_found", ex.Message));
            }
            catch (Exception ex)
            {
                return Task.FromResult(InstantiationResult.Fail("instantiate_failed", ex.Message));
            }

            ct.Register(() =>
            {
                try { Addressables.Release(handle); } catch { /* ignore */ }
            });

            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
                {
                    tcs.TrySetResult(InstantiationResult.Ok(op.Result));
                }
                else
                {
                    var ex = op.OperationException;
                    var code = (ex is InvalidKeyException) ? "not_found"
                        : (op.Status == AsyncOperationStatus.Failed) ? "load_failed"
                        : "instantiate_failed";
                    var msg = ex?.Message ?? $"Addressables status={op.Status}";
                    tcs.TrySetResult(InstantiationResult.Fail(code, msg));
                }
            };

            return tcs.Task;
        }

        public void ReleaseInstance(GameObject gameObject)
        {
            if (gameObject == null) return;
            bool released = Addressables.ReleaseInstance(gameObject);
            if (!released)
            {
                _logger?.Warning("AddressablesInstantiationProvider", "release_unmanaged",
                    context: "ReleaseInstance returned false (asset not tracked).");
            }
        }

        public Task<IReadOnlyList<string>> LoadResourceLocationsAsync(string label, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(label))
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            AsyncOperationHandle<IList<IResourceLocation>> handle;
            try
            {
                handle = Addressables.LoadResourceLocationsAsync(label, typeof(GameObject));
            }
            catch (Exception ex)
            {
                _logger?.Warning("AddressablesInstantiationProvider", "load_locations_failed",
                    context: ex.Message);
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }

            ct.Register(() =>
            {
                try { Addressables.Release(handle); } catch { /* ignore */ }
            });

            handle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
                {
                    var list = new List<string>(op.Result.Count);
                    foreach (var loc in op.Result)
                    {
                        if (loc?.PrimaryKey != null) list.Add(loc.PrimaryKey);
                    }
                    tcs.TrySetResult(list);
                }
                else
                {
                    _logger?.Warning("AddressablesInstantiationProvider", "load_locations_failed",
                        context: op.OperationException?.Message ?? $"status={op.Status}");
                    tcs.TrySetResult(Array.Empty<string>());
                }
                try { Addressables.Release(handle); } catch { /* ignore */ }
            };
            return tcs.Task;
        }
    }
}
