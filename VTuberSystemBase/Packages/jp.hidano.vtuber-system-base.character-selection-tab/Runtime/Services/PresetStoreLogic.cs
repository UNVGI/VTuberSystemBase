#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    public enum PresetOperationErrorCode
    {
        DuplicateName,
        NotFound,
        StorageFailure,
        InvalidName,
        CannotDeleteActive,
    }

    public readonly struct PresetOperationResult
    {
        public bool Success { get; }
        public PresetOperationErrorCode? Error { get; }
        public string? Detail { get; }
        public string? PresetId { get; }

        public PresetOperationResult(bool success, PresetOperationErrorCode? error, string? detail, string? presetId)
        {
            Success = success;
            Error = error;
            Detail = detail;
            PresetId = presetId;
        }

        public static PresetOperationResult Ok(string? presetId = null) => new PresetOperationResult(true, null, null, presetId);
        public static PresetOperationResult Fail(PresetOperationErrorCode code, string? detail = null) => new PresetOperationResult(false, code, detail, null);
    }

    public sealed class PresetSavedEvent
    {
        public string PresetId { get; init; } = "";
        public bool Success { get; init; }
        public DateTimeOffset At { get; init; }
        public string? Detail { get; init; }
    }

    public sealed class PresetLoadEvent
    {
        public int LoadedCount { get; init; }
        public int CorruptedCount { get; init; }
    }

    public interface IPresetStoreLogic : IDisposable
    {
        IReadOnlyList<PresetHeader> ListPresets();
        PresetRecord? GetActivePreset();
        string? ActivePresetId { get; }
        DateTimeOffset? LastSavedAt { get; }

        Task InitializeAsync(CancellationToken cancellationToken);
        Task<PresetOperationResult> CreateAsync(string name);
        Task<PresetOperationResult> RenameAsync(string presetId, string newName);
        Task<PresetOperationResult> DuplicateAsync(string presetId, string newName);
        Task<PresetOperationResult> DeleteAsync(string presetId);
        Task<PresetOperationResult> SetActiveAsync(string presetId);

        void MarkSlotAssignmentChanged(string slotId, string? avatarKey);
        void MarkSettingValueChanged(string slotId, string settingKey, SettingValue value);

        Task FlushPendingAsync();
        event Action<PresetSavedEvent> OnSaved;
        event Action<PresetLoadEvent> OnLoaded;
    }

    /// <summary>
    /// Production <see cref="IPresetStoreLogic"/>. Manages CRUD, debounced
    /// auto-save and corruption fallback. Persistence is delegated to
    /// <see cref="IPresetStorage"/>; <see cref="IClock"/> drives the debounce.
    /// (task 2.6.)
    /// </summary>
    public sealed class PresetStoreLogic : IPresetStoreLogic
    {
        private readonly IPresetStorage _storage;
        private readonly IClock _clock;
        private readonly TimeSpan _debounce;
        private readonly IDiagnosticsLogger? _log;

        private readonly Dictionary<string, PresetRecord> _records =
            new Dictionary<string, PresetRecord>(StringComparer.Ordinal);
        private readonly object _lock = new object();
        private DateTimeOffset? _dirtyMarkedAt;
        private bool _flushing;

        public PresetStoreLogic(IPresetStorage storage, IClock clock, TimeSpan debounce, IDiagnosticsLogger? logger = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (debounce <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(debounce));
            _debounce = debounce;
            _log = logger;
            _clock.OnTick += OnClockTick;
        }

        public string? ActivePresetId { get; private set; }
        public DateTimeOffset? LastSavedAt { get; private set; }

        public event Action<PresetSavedEvent>? OnSaved;
        public event Action<PresetLoadEvent>? OnLoaded;

        public IReadOnlyList<PresetHeader> ListPresets()
        {
            lock (_lock)
            {
                return _records.Values.Select(r => r.Header)
                    .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        public PresetRecord? GetActivePreset()
        {
            lock (_lock)
            {
                if (ActivePresetId is null) return null;
                return _records.TryGetValue(ActivePresetId, out var r) ? r : null;
            }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var loaded = await _storage.LoadAllAsync(cancellationToken);
            var report = await _storage.CheckHealthAsync(cancellationToken);
            var active = await _storage.LoadActivePresetIdAsync(cancellationToken);
            lock (_lock)
            {
                _records.Clear();
                foreach (var r in loaded) _records[r.Header.PresetId] = r;
                ActivePresetId = active is not null && _records.ContainsKey(active) ? active : null;
            }
            OnLoaded?.Invoke(new PresetLoadEvent
            {
                LoadedCount = report.LoadedCount,
                CorruptedCount = report.CorruptedCount,
            });
        }

        public async Task<PresetOperationResult> CreateAsync(string name)
        {
            var validation = ValidateName(name, requireUnique: true);
            if (validation.Error.HasValue) return validation;
            var id = Guid.NewGuid().ToString("N");
            var record = new PresetRecord
            {
                Header = new PresetHeader { PresetId = id, Name = name.Trim(), LastModifiedAt = _clock.UtcNow },
                Assignments = new Dictionary<string, string?>(),
                Settings = new Dictionary<string, IReadOnlyDictionary<string, SettingValue>>(),
            };
            try
            {
                await _storage.SaveAsync(record, default);
                lock (_lock) _records[id] = record;
                LastSavedAt = _clock.UtcNow;
                OnSaved?.Invoke(new PresetSavedEvent { PresetId = id, Success = true, At = LastSavedAt.Value });
                return PresetOperationResult.Ok(id);
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec, $"Preset.Save failed: {ex.Message}");
                OnSaved?.Invoke(new PresetSavedEvent { PresetId = id, Success = false, At = _clock.UtcNow, Detail = ex.Message });
                return PresetOperationResult.Fail(PresetOperationErrorCode.StorageFailure, ex.Message);
            }
        }

        public async Task<PresetOperationResult> RenameAsync(string presetId, string newName)
        {
            var validation = ValidateName(newName, requireUnique: true, exclude: presetId);
            if (validation.Error.HasValue) return validation;
            PresetRecord updated;
            lock (_lock)
            {
                if (!_records.TryGetValue(presetId, out var existing))
                    return PresetOperationResult.Fail(PresetOperationErrorCode.NotFound);
                updated = new PresetRecord
                {
                    Header = new PresetHeader
                    {
                        PresetId = existing.Header.PresetId,
                        Name = newName.Trim(),
                        LastModifiedAt = _clock.UtcNow,
                    },
                    Assignments = existing.Assignments,
                    Settings = existing.Settings,
                };
                _records[presetId] = updated;
            }
            try
            {
                await _storage.SaveAsync(updated, default);
                LastSavedAt = _clock.UtcNow;
                OnSaved?.Invoke(new PresetSavedEvent { PresetId = presetId, Success = true, At = LastSavedAt.Value });
                return PresetOperationResult.Ok(presetId);
            }
            catch (Exception ex)
            {
                OnSaved?.Invoke(new PresetSavedEvent { PresetId = presetId, Success = false, At = _clock.UtcNow, Detail = ex.Message });
                return PresetOperationResult.Fail(PresetOperationErrorCode.StorageFailure, ex.Message);
            }
        }

        public async Task<PresetOperationResult> DuplicateAsync(string presetId, string newName)
        {
            var validation = ValidateName(newName, requireUnique: true);
            if (validation.Error.HasValue) return validation;
            PresetRecord src;
            lock (_lock)
            {
                if (!_records.TryGetValue(presetId, out var s))
                    return PresetOperationResult.Fail(PresetOperationErrorCode.NotFound);
                src = s;
            }
            var newId = Guid.NewGuid().ToString("N");
            var copy = new PresetRecord
            {
                Header = new PresetHeader { PresetId = newId, Name = newName.Trim(), LastModifiedAt = _clock.UtcNow },
                Assignments = new Dictionary<string, string?>(src.Assignments, StringComparer.Ordinal),
                Settings = new Dictionary<string, IReadOnlyDictionary<string, SettingValue>>(src.Settings, StringComparer.Ordinal),
            };
            try
            {
                await _storage.SaveAsync(copy, default);
                lock (_lock) _records[newId] = copy;
                LastSavedAt = _clock.UtcNow;
                OnSaved?.Invoke(new PresetSavedEvent { PresetId = newId, Success = true, At = LastSavedAt.Value });
                return PresetOperationResult.Ok(newId);
            }
            catch (Exception ex)
            {
                return PresetOperationResult.Fail(PresetOperationErrorCode.StorageFailure, ex.Message);
            }
        }

        public async Task<PresetOperationResult> DeleteAsync(string presetId)
        {
            lock (_lock)
            {
                if (!_records.ContainsKey(presetId))
                    return PresetOperationResult.Fail(PresetOperationErrorCode.NotFound);
                if (string.Equals(ActivePresetId, presetId, StringComparison.Ordinal))
                    return PresetOperationResult.Fail(PresetOperationErrorCode.CannotDeleteActive);
            }
            try
            {
                await _storage.DeleteAsync(presetId, default);
                lock (_lock) _records.Remove(presetId);
                return PresetOperationResult.Ok(presetId);
            }
            catch (Exception ex)
            {
                return PresetOperationResult.Fail(PresetOperationErrorCode.StorageFailure, ex.Message);
            }
        }

        public async Task<PresetOperationResult> SetActiveAsync(string presetId)
        {
            lock (_lock)
            {
                if (!_records.ContainsKey(presetId))
                    return PresetOperationResult.Fail(PresetOperationErrorCode.NotFound);
                ActivePresetId = presetId;
            }
            try
            {
                await _storage.SetActiveAsync(presetId, default);
                return PresetOperationResult.Ok(presetId);
            }
            catch (Exception ex)
            {
                return PresetOperationResult.Fail(PresetOperationErrorCode.StorageFailure, ex.Message);
            }
        }

        public void MarkSlotAssignmentChanged(string slotId, string? avatarKey)
        {
            lock (_lock)
            {
                if (ActivePresetId is null) return;
                if (!_records.TryGetValue(ActivePresetId, out var rec)) return;
                var assigns = new Dictionary<string, string?>(rec.Assignments, StringComparer.Ordinal)
                {
                    [slotId] = avatarKey,
                };
                _records[ActivePresetId] = new PresetRecord
                {
                    Header = rec.Header,
                    Assignments = assigns,
                    Settings = rec.Settings,
                };
                _dirtyMarkedAt = _clock.UtcNow;
            }
        }

        public void MarkSettingValueChanged(string slotId, string settingKey, SettingValue value)
        {
            lock (_lock)
            {
                if (ActivePresetId is null) return;
                if (!_records.TryGetValue(ActivePresetId, out var rec)) return;
                var slotSettings = rec.Settings.TryGetValue(slotId, out var existing)
                    ? new Dictionary<string, SettingValue>(existing, StringComparer.Ordinal)
                    : new Dictionary<string, SettingValue>(StringComparer.Ordinal);
                slotSettings[settingKey] = value;
                var settings = new Dictionary<string, IReadOnlyDictionary<string, SettingValue>>(rec.Settings, StringComparer.Ordinal)
                {
                    [slotId] = slotSettings,
                };
                _records[ActivePresetId] = new PresetRecord
                {
                    Header = rec.Header,
                    Assignments = rec.Assignments,
                    Settings = settings,
                };
                _dirtyMarkedAt = _clock.UtcNow;
            }
        }

        public async Task FlushPendingAsync()
        {
            PresetRecord? toSave;
            lock (_lock)
            {
                if (_dirtyMarkedAt is null || ActivePresetId is null || _flushing) return;
                if (!_records.TryGetValue(ActivePresetId, out var r)) return;
                toSave = new PresetRecord
                {
                    Header = new PresetHeader
                    {
                        PresetId = r.Header.PresetId,
                        Name = r.Header.Name,
                        LastModifiedAt = _clock.UtcNow,
                    },
                    Assignments = r.Assignments,
                    Settings = r.Settings,
                };
                _flushing = true;
                _dirtyMarkedAt = null;
            }
            try
            {
                await _storage.SaveAsync(toSave!, default);
                lock (_lock)
                {
                    if (_records.ContainsKey(toSave!.Header.PresetId))
                        _records[toSave.Header.PresetId] = toSave;
                }
                LastSavedAt = _clock.UtcNow;
                OnSaved?.Invoke(new PresetSavedEvent { PresetId = toSave!.Header.PresetId, Success = true, At = LastSavedAt.Value });
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec, $"Preset.Save failed: {ex.Message}");
                OnSaved?.Invoke(new PresetSavedEvent { PresetId = toSave!.Header.PresetId, Success = false, At = _clock.UtcNow, Detail = ex.Message });
                lock (_lock) { _dirtyMarkedAt = _clock.UtcNow; }
            }
            finally
            {
                lock (_lock) _flushing = false;
            }
        }

        public void Dispose()
        {
            _clock.OnTick -= OnClockTick;
        }

        private void OnClockTick(DateTimeOffset now)
        {
            DateTimeOffset? markedAt;
            lock (_lock) { markedAt = _dirtyMarkedAt; }
            if (markedAt is null) return;
            if (now - markedAt.Value < _debounce) return;
            // Fire-and-forget; flush is idempotent.
            _ = FlushPendingAsync();
        }

        private PresetOperationResult ValidateName(string name, bool requireUnique, string? exclude = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return PresetOperationResult.Fail(PresetOperationErrorCode.InvalidName, "name must not be empty");
            var trimmed = name.Trim();
            if (requireUnique)
            {
                lock (_lock)
                {
                    foreach (var r in _records.Values)
                    {
                        if (exclude is not null && string.Equals(r.Header.PresetId, exclude, StringComparison.Ordinal)) continue;
                        if (string.Equals(r.Header.Name, trimmed, StringComparison.Ordinal))
                            return PresetOperationResult.Fail(PresetOperationErrorCode.DuplicateName);
                    }
                }
            }
            return PresetOperationResult.Ok();
        }
    }
}
