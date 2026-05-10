#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Adapters.Volume
{
    /// <summary>
    /// Resolves <see cref="VolumeComponent"/> derived type names against the URP
    /// <see cref="VolumeManager.instance"/>'s registered type array. Used by
    /// <see cref="GlobalEnabledLocalVolumeBinder"/> for AddOverride / RemoveOverride
    /// (Requirement 6.2 / 6.3).
    /// </summary>
    /// <remarks>
    /// Lookups are cached on first use; the URP type array is static for the
    /// lifetime of the process.
    /// </remarks>
    public sealed class VolumeComponentTypeResolver
    {
        private Dictionary<string, Type>? _cache;

        public Type? Resolve(string overrideTypeName)
        {
            if (string.IsNullOrEmpty(overrideTypeName)) return null;
            EnsureCache();
            return _cache!.TryGetValue(overrideTypeName, out var t) ? t : null;
        }

        public IReadOnlyDictionary<string, Type> ResolveAll()
        {
            EnsureCache();
            return _cache!;
        }

        public void InvalidateCache() => _cache = null;

        private void EnsureCache()
        {
            if (_cache != null) return;
            var dict = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var t in VolumeComponentTypeCollector.Collect())
            {
                if (t == null) continue;
                // Last write wins on duplicates — URP's array shouldn't have any.
                dict[t.Name] = t;
            }
            _cache = dict;
        }
    }
}
