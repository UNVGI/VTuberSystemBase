#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.CharacterSelectionTab.State
{
    /// <summary>
    /// Concrete <see cref="ICharacterTabStateStore"/>. Holds slot map, avatar
    /// catalog, in-flight operations and a per-(slot,key) buffer used for
    /// state reverse-flow suppression while the operator interacts with a
    /// control. (task 2.1.)
    /// </summary>
    public sealed class CharacterTabStateStore : ICharacterTabStateStore
    {
        private readonly SortedDictionary<string, SlotSnapshot> _slots =
            new SortedDictionary<string, SlotSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, InFlightOperationKind> _inFlightKinds =
            new Dictionary<string, InFlightOperationKind>(StringComparer.Ordinal);
        private readonly Dictionary<string, Guid> _inFlightIds =
            new Dictionary<string, Guid>(StringComparer.Ordinal);
        private readonly Dictionary<(string slotId, string settingKey), SettingValue> _bufferedRemote =
            new Dictionary<(string, string), SettingValue>();
        private readonly HashSet<(string slotId, string settingKey)> _interactingKeys =
            new HashSet<(string, string)>();

        private readonly int _ownerThreadId;
        private IReadOnlyList<AvatarCatalogEntry> _avatarCatalog = Array.Empty<AvatarCatalogEntry>();

        public string? ActivePresetId { get; private set; }
        public ConnectionStatusCode ConnectionStatus { get; private set; } = ConnectionStatusCode.Initializing;
        public string? SelectedSlotId { get; private set; }

        /// <summary>
        /// Optional <see cref="IInteractionGuard"/>-style lookup. The store does
        /// not own the guard; the bootstrapper wires <c>guard.OnChanged</c> to
        /// <see cref="MarkInteracting"/> / <see cref="EndInteracting"/>.
        /// </summary>
        public Action<string, string, bool>? OnDiagnosticWarning { get; set; }

        public CharacterTabStateStore()
        {
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public IReadOnlyList<AvatarCatalogEntry> AvatarCatalog
        {
            get
            {
                EnsureMainThread();
                return _avatarCatalog;
            }
        }

        public event Action<StateChangeScope>? OnChanged;

        public SlotSnapshot? GetSlot(string slotId)
        {
            EnsureMainThread();
            return _slots.TryGetValue(slotId, out var s) ? s : null;
        }

        public IReadOnlyList<SlotSnapshot> ListSlots()
        {
            EnsureMainThread();
            return _slots.Values.ToArray();
        }

        public void ApplySlotCatalog(SlotCatalogPayload payload)
        {
            EnsureMainThread();
            if (payload?.Slots is null) return;

            var keep = new HashSet<string>(StringComparer.Ordinal);
            bool changed = false;
            foreach (var entry in payload.Slots)
            {
                if (string.IsNullOrEmpty(entry.SlotId)) continue;
                keep.Add(entry.SlotId);
                if (!_slots.ContainsKey(entry.SlotId))
                {
                    _slots[entry.SlotId] = new SlotSnapshot
                    {
                        SlotId = entry.SlotId,
                        DisplayName = entry.DisplayName,
                        Status = SlotStatus.Empty,
                    };
                    changed = true;
                }
                else if (!string.Equals(_slots[entry.SlotId].DisplayName, entry.DisplayName, StringComparison.Ordinal))
                {
                    _slots[entry.SlotId] = WithDisplayName(_slots[entry.SlotId], entry.DisplayName);
                    changed = true;
                }
            }
            // Remove slots no longer in the catalog.
            var toRemove = _slots.Keys.Where(k => !keep.Contains(k)).ToArray();
            foreach (var k in toRemove)
            {
                _slots.Remove(k);
                _inFlightKinds.Remove(k);
                _inFlightIds.Remove(k);
                changed = true;
            }
            if (changed) Raise(StateChangeScope.SlotCatalog);
        }

        public void ApplyAvatarCatalog(AvatarCatalogPayload payload)
        {
            EnsureMainThread();
            if (payload?.Avatars is null) return;
            // Deduplicate by AvatarKey, preserving first occurrence.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<AvatarCatalogEntry>(payload.Avatars.Count);
            foreach (var e in payload.Avatars)
            {
                if (string.IsNullOrEmpty(e.AvatarKey)) continue;
                if (!seen.Add(e.AvatarKey)) continue;
                list.Add(new AvatarCatalogEntry
                {
                    AvatarKey = e.AvatarKey,
                    DisplayName = e.DisplayName,
                });
            }
            _avatarCatalog = list;
            Raise(StateChangeScope.AvatarCatalog);
        }

        public void ApplyAssignment(string slotId, string? avatarKey)
        {
            EnsureMainThread();
            if (!_slots.TryGetValue(slotId, out var snap))
            {
                OnDiagnosticWarning?.Invoke(slotId, "ApplyAssignment", false);
                return;
            }
            if (string.Equals(snap.AssignedAvatarKey, avatarKey, StringComparison.Ordinal)) return;
            _slots[slotId] = new SlotSnapshot
            {
                SlotId = snap.SlotId,
                DisplayName = snap.DisplayName,
                AssignedAvatarKey = avatarKey,
                Status = avatarKey is null ? SlotStatus.Empty : snap.Status,
                StatusDetail = snap.StatusDetail,
                SettingValues = snap.SettingValues,
                InFlight = snap.InFlight,
            };
            Raise(StateChangeScope.Assignment);
        }

        public void ApplyStatus(string slotId, SlotStatus status, string? detail)
        {
            EnsureMainThread();
            if (!_slots.TryGetValue(slotId, out var snap))
            {
                OnDiagnosticWarning?.Invoke(slotId, "ApplyStatus", false);
                return;
            }
            if (snap.Status == status && string.Equals(snap.StatusDetail, detail, StringComparison.Ordinal)) return;
            _slots[slotId] = new SlotSnapshot
            {
                SlotId = snap.SlotId,
                DisplayName = snap.DisplayName,
                AssignedAvatarKey = snap.AssignedAvatarKey,
                Status = status,
                StatusDetail = detail,
                SettingValues = snap.SettingValues,
                InFlight = snap.InFlight,
            };
            Raise(StateChangeScope.SlotStatus);
        }

        public void ApplySettingValue(string slotId, string settingKey, SettingValue value, bool isFromRemote)
        {
            EnsureMainThread();
            if (!_slots.TryGetValue(slotId, out var snap))
            {
                OnDiagnosticWarning?.Invoke(slotId, "ApplySettingValue", false);
                return;
            }
            // Suppress remote echoes while the operator is interacting; buffer the
            // most recent remote value and apply on EndInteracting.
            if (isFromRemote && _interactingKeys.Contains((slotId, settingKey)))
            {
                _bufferedRemote[(slotId, settingKey)] = value;
                return;
            }
            var newValues = new Dictionary<string, SettingValue>(snap.SettingValues, StringComparer.Ordinal)
            {
                [settingKey] = value,
            };
            _slots[slotId] = new SlotSnapshot
            {
                SlotId = snap.SlotId,
                DisplayName = snap.DisplayName,
                AssignedAvatarKey = snap.AssignedAvatarKey,
                Status = snap.Status,
                StatusDetail = snap.StatusDetail,
                SettingValues = newValues,
                InFlight = snap.InFlight,
            };
            Raise(StateChangeScope.SettingValue);
        }

        public void ApplyError(string slotId, SlotErrorPayload error)
        {
            EnsureMainThread();
            if (!_slots.TryGetValue(slotId, out var snap))
            {
                OnDiagnosticWarning?.Invoke(slotId, "ApplyError", false);
                return;
            }
            _slots[slotId] = new SlotSnapshot
            {
                SlotId = snap.SlotId,
                DisplayName = snap.DisplayName,
                AssignedAvatarKey = snap.AssignedAvatarKey,
                Status = SlotStatus.Error,
                StatusDetail = error?.Detail,
                SettingValues = snap.SettingValues,
                InFlight = snap.InFlight,
            };
            Raise(StateChangeScope.SlotError | StateChangeScope.SlotStatus);
        }

        public void ClearError(string slotId)
        {
            EnsureMainThread();
            if (!_slots.TryGetValue(slotId, out var snap)) return;
            if (snap.Status != SlotStatus.Error) return;
            _slots[slotId] = new SlotSnapshot
            {
                SlotId = snap.SlotId,
                DisplayName = snap.DisplayName,
                AssignedAvatarKey = snap.AssignedAvatarKey,
                Status = snap.AssignedAvatarKey is null ? SlotStatus.Empty : SlotStatus.Assigned,
                StatusDetail = null,
                SettingValues = snap.SettingValues,
                InFlight = snap.InFlight,
            };
            Raise(StateChangeScope.SlotError | StateChangeScope.SlotStatus);
        }

        public bool TryBeginInFlight(string slotId, InFlightOperationKind kind, out InFlightToken token)
        {
            EnsureMainThread();
            token = default;
            if (!_slots.TryGetValue(slotId, out var snap)) return false;
            if (_inFlightKinds.ContainsKey(slotId)) return false;
            var id = Guid.NewGuid();
            _inFlightKinds[slotId] = kind;
            _inFlightIds[slotId] = id;
            token = new InFlightToken(slotId, kind, id);
            _slots[slotId] = WithInFlight(snap, kind);
            Raise(StateChangeScope.InFlight);
            return true;
        }

        public bool EndInFlight(InFlightToken token, InFlightOutcome outcome)
        {
            EnsureMainThread();
            if (!_inFlightIds.TryGetValue(token.SlotId, out var id)) return false;
            if (id != token.Id) return false;
            _inFlightKinds.Remove(token.SlotId);
            _inFlightIds.Remove(token.SlotId);
            if (_slots.TryGetValue(token.SlotId, out var snap))
            {
                _slots[token.SlotId] = WithInFlight(snap, null);
            }
            Raise(StateChangeScope.InFlight);
            return true;
        }

        public void SetActivePreset(string? presetId)
        {
            EnsureMainThread();
            if (string.Equals(ActivePresetId, presetId, StringComparison.Ordinal)) return;
            ActivePresetId = presetId;
            Raise(StateChangeScope.ActivePreset);
        }

        public void SetConnectionStatus(ConnectionStatusCode status)
        {
            EnsureMainThread();
            if (ConnectionStatus == status) return;
            ConnectionStatus = status;
            Raise(StateChangeScope.Connection);
        }

        public void SetSelectedSlot(string? slotId)
        {
            EnsureMainThread();
            if (string.Equals(SelectedSlotId, slotId, StringComparison.Ordinal)) return;
            SelectedSlotId = slotId;
            Raise(StateChangeScope.Assignment);
        }

        public void FlushBufferedSetting(string slotId, string settingKey)
        {
            EnsureMainThread();
            var key = (slotId, settingKey);
            _interactingKeys.Remove(key);
            if (_bufferedRemote.TryGetValue(key, out var v))
            {
                _bufferedRemote.Remove(key);
                ApplySettingValue(slotId, settingKey, v, isFromRemote: true);
            }
        }

        public void MarkInteracting(string slotId, string settingKey)
        {
            EnsureMainThread();
            _interactingKeys.Add((slotId, settingKey));
        }

        private static SlotSnapshot WithInFlight(SlotSnapshot snap, InFlightOperationKind? kind) =>
            new SlotSnapshot
            {
                SlotId = snap.SlotId,
                DisplayName = snap.DisplayName,
                AssignedAvatarKey = snap.AssignedAvatarKey,
                Status = snap.Status,
                StatusDetail = snap.StatusDetail,
                SettingValues = snap.SettingValues,
                InFlight = kind,
            };

        private static SlotSnapshot WithDisplayName(SlotSnapshot snap, string? name) =>
            new SlotSnapshot
            {
                SlotId = snap.SlotId,
                DisplayName = name,
                AssignedAvatarKey = snap.AssignedAvatarKey,
                Status = snap.Status,
                StatusDetail = snap.StatusDetail,
                SettingValues = snap.SettingValues,
                InFlight = snap.InFlight,
            };

        private void EnsureMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "CharacterTabStateStore must be accessed on the owning (main) thread.");
            }
        }

        private void Raise(StateChangeScope scope) => OnChanged?.Invoke(scope);
    }
}
