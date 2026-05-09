#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Stage;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    /// <summary>
    /// In-memory <see cref="IInstantiationProvider"/> double. Predefined responses can be
    /// keyed by addressable key; <c>InstantiateAsync</c> resolves immediately. Released
    /// instances are recorded so tests can assert lazy-swap ordering.
    /// </summary>
    internal sealed class FakeInstantiationProvider : IInstantiationProvider
    {
        public sealed class KeyConfig
        {
            public bool Success { get; set; } = true;
            public string? ErrorCode { get; set; }
            public string? ErrorMessage { get; set; }
            /// <summary>Optional GameObject factory; default creates a fresh empty GO named after the key.</summary>
            public Func<string, Transform, GameObject>? Factory { get; set; }
        }

        private readonly Dictionary<string, KeyConfig> _keys = new();
        private readonly Dictionary<string, IReadOnlyList<string>> _labelLocations = new();

        public readonly List<GameObject> ReleasedInstances = new();
        public readonly List<string> InstantiatedKeys = new();

        public KeyConfig Configure(string key, KeyConfig? cfg = null)
        {
            cfg ??= new KeyConfig();
            _keys[key] = cfg;
            return cfg;
        }

        public void ConfigureLabel(string label, IReadOnlyList<string> primaryKeys)
        {
            _labelLocations[label] = primaryKeys;
        }

        public Task<InstantiationResult> InstantiateAsync(string addressableKey, Transform parent, CancellationToken ct = default)
        {
            InstantiatedKeys.Add(addressableKey);
            if (!_keys.TryGetValue(addressableKey, out var cfg))
            {
                return Task.FromResult(InstantiationResult.Fail("not_found", $"key '{addressableKey}' not configured"));
            }
            if (!cfg.Success)
            {
                return Task.FromResult(InstantiationResult.Fail(cfg.ErrorCode ?? "load_failed", cfg.ErrorMessage ?? "configured failure"));
            }
            GameObject go;
            if (cfg.Factory != null)
            {
                go = cfg.Factory(addressableKey, parent);
            }
            else
            {
                go = new GameObject($"Stage_{addressableKey}");
                go.transform.SetParent(parent, worldPositionStays: false);
            }
            return Task.FromResult(InstantiationResult.Ok(go));
        }

        public void ReleaseInstance(GameObject gameObject)
        {
            if (gameObject == null) return;
            ReleasedInstances.Add(gameObject);
            Object.DestroyImmediate(gameObject);
        }

        public Task<IReadOnlyList<string>> LoadResourceLocationsAsync(string label, CancellationToken ct = default)
        {
            return Task.FromResult(_labelLocations.TryGetValue(label, out var list) ? list : Array.Empty<string>());
        }
    }
}
