#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Abstractions;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Domain
{
    /// <summary>
    /// In-memory <see cref="CameraEntry"/> registry indexed by <see cref="CameraId"/>.
    /// Insertion order is preserved across enumeration (Requirement 4.3, CSO-6).
    /// </summary>
    /// <remarks>
    /// The registry maintains a parallel <see cref="List{CameraEntry}"/> sorted by
    /// <c>AllocOrder</c> ascending. Because <c>AllocOrder</c> is monotonic and
    /// matches insertion time, the list is naturally append-only with O(1) inserts.
    /// Removals scan the list (O(n)); n is bounded by <c>MaxCameras</c> and a list
    /// scan is cheap at typical magnitudes (≤32).
    /// </remarks>
    public sealed class CameraEntryRegistry
    {
        private readonly Dictionary<CameraId, CameraEntry> _byId = new Dictionary<CameraId, CameraEntry>();
        private readonly List<CameraEntry> _ordered = new List<CameraEntry>();

        public int Count => _ordered.Count;

        public IReadOnlyList<CameraEntry> Enumerate() => _ordered;

        public bool TryGet(CameraId cameraId, out CameraEntry entry)
        {
            return _byId.TryGetValue(cameraId, out entry!);
        }

        public bool Contains(CameraId cameraId) => _byId.ContainsKey(cameraId);

        /// <summary>
        /// Inserts <paramref name="entry"/> if its cameraId is not already present;
        /// otherwise replaces the existing entry in place (preserving its position
        /// in the AllocOrder list).
        /// </summary>
        public void Upsert(CameraEntry entry)
        {
            if (_byId.TryGetValue(entry.CameraId, out var existing))
            {
                _byId[entry.CameraId] = entry;
                var idx = _ordered.IndexOf(existing);
                if (idx >= 0) _ordered[idx] = entry;
                else InsertOrdered(entry);
            }
            else
            {
                _byId[entry.CameraId] = entry;
                InsertOrdered(entry);
            }
        }

        public bool Remove(CameraId cameraId)
        {
            if (!_byId.TryGetValue(cameraId, out var existing)) return false;
            _byId.Remove(cameraId);
            _ordered.Remove(existing);
            return true;
        }

        public void Clear()
        {
            _byId.Clear();
            _ordered.Clear();
        }

        private void InsertOrdered(CameraEntry entry)
        {
            // Maintain AllocOrder ascending. The fast path (append) covers the
            // common monotonic insertion pattern.
            if (_ordered.Count == 0 || _ordered[^1].AllocOrder <= entry.AllocOrder)
            {
                _ordered.Add(entry);
                return;
            }
            for (var i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i].AllocOrder > entry.AllocOrder)
                {
                    _ordered.Insert(i, entry);
                    return;
                }
            }
            _ordered.Add(entry);
        }
    }
}
