#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.CameraSwitcherTab.Contracts;

namespace VTuberSystemBase.CameraSwitcherTab.Domain
{
    /// <summary>
    /// Lookup + ordered list of every camera the UI knows about. The order
    /// reflects the sequence cameras were added (or first observed) and is
    /// preserved across upserts so the UI list rendering is stable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Upsert(CameraMetadata)"/> creates a new entry on first sight
    /// and overwrites the existing one (in place, no reorder) on subsequent
    /// sights. <see cref="Remove(CameraId)"/> shrinks the list and shifts
    /// later entries down. <see cref="Enumerate"/> snapshots the current list
    /// so callers can iterate without observing concurrent mutations.
    /// </para>
    /// <para>
    /// Not thread-safe — the caller (Coordinator) drives every mutation from
    /// the Unity main thread (D-3).
    /// </para>
    /// </remarks>
    public sealed class CameraRegistry
    {
        private readonly Dictionary<string, int> _indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<CameraMetadata> _entries = new List<CameraMetadata>();

        public int Count => _entries.Count;

        /// <summary>True if a camera with id <paramref name="cameraId"/> is registered.</summary>
        public bool Contains(CameraId cameraId)
        {
            if (!cameraId.HasValue) return false;
            return _indexById.ContainsKey(cameraId.Value);
        }

        public bool TryGet(CameraId cameraId, out CameraMetadata metadata)
        {
            if (cameraId.HasValue && _indexById.TryGetValue(cameraId.Value, out var idx))
            {
                metadata = _entries[idx];
                return true;
            }
            metadata = null!;
            return false;
        }

        /// <summary>
        /// Insert a new metadata or overwrite the existing one. Returns true on
        /// insert and false on overwrite. Throws if <paramref name="metadata"/>
        /// has an unset CameraId.
        /// </summary>
        public bool Upsert(CameraMetadata metadata)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (!metadata.Id.HasValue)
                throw new ArgumentException("CameraMetadata.Id must be set.", nameof(metadata));
            var key = metadata.Id.Value;
            if (_indexById.TryGetValue(key, out var idx))
            {
                _entries[idx] = metadata;
                return false;
            }
            _indexById[key] = _entries.Count;
            _entries.Add(metadata);
            return true;
        }

        /// <summary>Remove the camera with id <paramref name="cameraId"/>. Returns true on hit.</summary>
        public bool Remove(CameraId cameraId)
        {
            if (!cameraId.HasValue) return false;
            if (!_indexById.TryGetValue(cameraId.Value, out var idx)) return false;
            _entries.RemoveAt(idx);
            _indexById.Remove(cameraId.Value);
            // Reindex entries after the removed slot.
            for (var i = idx; i < _entries.Count; i++)
            {
                _indexById[_entries[i].Id.Value] = i;
            }
            return true;
        }

        public void Clear()
        {
            _entries.Clear();
            _indexById.Clear();
        }

        /// <summary>Snapshot of the current ordered camera list.</summary>
        public IReadOnlyList<CameraMetadata> Enumerate() => _entries.ToArray();
    }
}
