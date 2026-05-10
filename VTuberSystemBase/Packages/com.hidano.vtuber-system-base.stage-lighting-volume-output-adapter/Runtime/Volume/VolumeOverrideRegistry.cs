#nullable enable
using System;
using System.Collections.Generic;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Volume
{
    /// <summary>
    /// Cache mapping <c>typeFullName -&gt; Type</c> for every URP <c>VolumeComponent</c>
    /// known at startup. Built once from <c>VolumeManager.instance.baseComponentTypeArray</c>
    /// and consumed by <c>VolumeOverrideHandler</c> when applying topic-driven overrides.
    /// </summary>
    public sealed class VolumeOverrideRegistry
    {
        private readonly Dictionary<string, Type> _typeFullNameToType = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, string> _typeToTypeFullName = new();

        public int Count => _typeFullNameToType.Count;

        public void Build(IReadOnlyList<Type> volumeComponentTypes)
        {
            if (volumeComponentTypes == null) return;
            _typeFullNameToType.Clear();
            _typeToTypeFullName.Clear();
            foreach (var t in volumeComponentTypes)
            {
                if (t == null || t.FullName == null) continue;
                _typeFullNameToType[t.FullName] = t;
                _typeToTypeFullName[t] = t.FullName;
            }
        }

        public bool Contains(string typeFullName)
            => !string.IsNullOrEmpty(typeFullName) && _typeFullNameToType.ContainsKey(typeFullName);

        public bool GetTypeByFullName(string typeFullName, out Type type)
        {
            if (string.IsNullOrEmpty(typeFullName))
            {
                type = null!;
                return false;
            }
            return _typeFullNameToType.TryGetValue(typeFullName, out type!);
        }

        public bool GetFullNameByType(Type type, out string fullName)
        {
            if (type == null)
            {
                fullName = string.Empty;
                return false;
            }
            return _typeToTypeFullName.TryGetValue(type, out fullName!);
        }

        public IReadOnlyCollection<string> AllTypeFullNames => _typeFullNameToType.Keys;
    }
}
