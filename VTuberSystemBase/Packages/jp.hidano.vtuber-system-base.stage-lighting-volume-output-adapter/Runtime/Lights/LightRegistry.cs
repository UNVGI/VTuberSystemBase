#nullable enable
using System.Collections.Generic;
using UnityEngine;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Lights
{
    /// <summary>
    /// Per-light record held by <see cref="LightRegistry"/>. Owns the spawned GameObject,
    /// the live <see cref="UnityEngine.Light"/> component, the property handler tokens
    /// returned from <c>RegisterStateHandler</c>, the cached initial values (so the registry
    /// can re-publish on demand), and the current display name.
    /// </summary>
    internal sealed class LightEntry
    {
        public string LightId { get; }
        public GameObject GameObject { get; }
        public Light Light { get; }
        public List<System.IDisposable> PropertyHandlers { get; } = new();
        public LightInitialDto Initial { get; set; }
        public string DisplayName { get; set; }

        public LightEntry(string lightId, GameObject go, Light light, LightInitialDto initial)
        {
            LightId = lightId;
            GameObject = go;
            Light = light;
            Initial = initial;
            DisplayName = initial.DisplayName;
        }
    }

    /// <summary>
    /// Pure data structure mapping <c>lightId</c> to <see cref="LightEntry"/>. Insertion
    /// order is preserved for stable output in <see cref="ToListDto"/>. The registry never
    /// owns GameObject lifetime: callers (LightHandler) destroy GameObjects and dispose
    /// handler tokens around <see cref="Remove"/> / <see cref="Clear"/>.
    /// </summary>
    internal sealed class LightRegistry
    {
        private readonly Dictionary<string, LightEntry> _byId = new();
        private readonly List<string> _order = new();

        public int Count => _byId.Count;

        public IReadOnlyList<string> AllLightIds => _order;

        public bool TryGet(string lightId, out LightEntry entry)
        {
            return _byId.TryGetValue(lightId, out entry!);
        }

        public void Add(string lightId, LightEntry entry)
        {
            _byId[lightId] = entry;
            if (!_order.Contains(lightId)) _order.Add(lightId);
        }

        public bool Remove(string lightId)
        {
            if (!_byId.Remove(lightId)) return false;
            _order.Remove(lightId);
            return true;
        }

        public void Clear()
        {
            _byId.Clear();
            _order.Clear();
        }

        public LightListDto ToListDto()
        {
            var items = new List<LightListItemDto>(_order.Count);
            foreach (var id in _order)
            {
                if (_byId.TryGetValue(id, out var e))
                {
                    items.Add(new LightListItemDto(
                        LightId: id,
                        DisplayName: e.DisplayName,
                        Type: VTuberSystemBase.StageLightingVolumeOutputAdapter.Internal.DtoConverters.ToDto(e.Light != null ? e.Light.type : UnityEngine.LightType.Point)));
                }
            }
            return new LightListDto(items);
        }
    }
}
