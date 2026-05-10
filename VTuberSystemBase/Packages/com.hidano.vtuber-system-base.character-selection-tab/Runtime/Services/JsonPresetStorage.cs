#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.CharacterSelectionTab.State;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CharacterSelectionTab.Services
{
    /// <summary>
    /// File-system <see cref="IPresetStorage"/>. Layout:
    /// <c>{baseDirectory}/{presetId}.json</c> + <c>_active.json</c>.
    /// Atomic writes via temp-file + <c>File.Move</c>; corrupted files are
    /// renamed to <c>{file}.bak.{unixms}</c>. Recorded in
    /// <see cref="StorageHealthReport"/>. (task 2.5.)
    /// </summary>
    public sealed class JsonPresetStorage : IPresetStorage
    {
        private readonly string _baseDir;
        private readonly IDiagnosticsLogger? _log;
        private readonly object _ioLock = new object();
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public JsonPresetStorage(string? baseDirectory = null, IDiagnosticsLogger? logger = null)
        {
            _baseDir = baseDirectory ?? DefaultBaseDirectory();
            _log = logger;
            Directory.CreateDirectory(_baseDir);
        }

        public static string DefaultBaseDirectory()
            => Path.Combine(Application.persistentDataPath, "character-selection-tab", "presets");

        public string BaseDirectory => _baseDir;

        public Task<IReadOnlyList<PresetRecord>> LoadAllAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    var list = new List<PresetRecord>();
                    if (!Directory.Exists(_baseDir)) return (IReadOnlyList<PresetRecord>)list;
                    foreach (var path in Directory.EnumerateFiles(_baseDir, "*.json"))
                    {
                        var name = Path.GetFileName(path);
                        if (string.Equals(name, "_active.json", StringComparison.Ordinal)) continue;
                        try
                        {
                            var text = File.ReadAllText(path);
                            var dto = JsonSerializer.Deserialize<PresetFileDto>(text, JsonOpts);
                            if (dto is null) throw new InvalidDataException("null DTO");
                            list.Add(ToRecord(dto));
                        }
                        catch (Exception ex)
                        {
                            QuarantineCorrupted(path, ex);
                        }
                    }
                    return (IReadOnlyList<PresetRecord>)list;
                }
            }, cancellationToken);
        }

        public Task<string?> LoadActivePresetIdAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    var path = ActivePath();
                    if (!File.Exists(path)) return (string?)null;
                    try
                    {
                        var text = File.ReadAllText(path);
                        var dto = JsonSerializer.Deserialize<ActiveFileDto>(text, JsonOpts);
                        return (string?)dto?.activePresetId;
                    }
                    catch
                    {
                        return (string?)null;
                    }
                }
            }, cancellationToken);
        }

        public Task SaveAsync(PresetRecord record, CancellationToken cancellationToken)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrEmpty(record.Header.PresetId))
                throw new ArgumentException("PresetId required", nameof(record));
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    Directory.CreateDirectory(_baseDir);
                    var dto = ToDto(record);
                    var json = JsonSerializer.Serialize(dto, JsonOpts);
                    var finalPath = PresetPath(record.Header.PresetId);
                    var tmpPath = finalPath + ".tmp";
                    File.WriteAllText(tmpPath, json);
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(tmpPath, finalPath);
                }
            }, cancellationToken);
        }

        public Task DeleteAsync(string presetId, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    var path = PresetPath(presetId);
                    if (File.Exists(path)) File.Delete(path);
                }
            }, cancellationToken);
        }

        public Task SetActiveAsync(string? presetId, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    Directory.CreateDirectory(_baseDir);
                    var dto = new ActiveFileDto { activePresetId = presetId };
                    var json = JsonSerializer.Serialize(dto, JsonOpts);
                    var finalPath = ActivePath();
                    var tmpPath = finalPath + ".tmp";
                    File.WriteAllText(tmpPath, json);
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(tmpPath, finalPath);
                }
            }, cancellationToken);
        }

        public Task<StorageHealthReport> CheckHealthAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    int loaded = 0;
                    var backups = new List<string>();
                    if (Directory.Exists(_baseDir))
                    {
                        foreach (var path in Directory.EnumerateFiles(_baseDir, "*.json"))
                        {
                            var name = Path.GetFileName(path);
                            if (string.Equals(name, "_active.json", StringComparison.Ordinal)) continue;
                            try
                            {
                                var dto = JsonSerializer.Deserialize<PresetFileDto>(File.ReadAllText(path), JsonOpts);
                                if (dto is not null) loaded++;
                            }
                            catch (Exception ex)
                            {
                                backups.Add(QuarantineCorrupted(path, ex));
                            }
                        }
                    }
                    return new StorageHealthReport
                    {
                        LoadedCount = loaded,
                        CorruptedCount = backups.Count,
                        BackedUpFiles = backups.ToArray(),
                    };
                }
            }, cancellationToken);
        }

        private string PresetPath(string presetId) => Path.Combine(_baseDir, $"{presetId}.json");
        private string ActivePath() => Path.Combine(_baseDir, "_active.json");

        private string QuarantineCorrupted(string path, Exception ex)
        {
            var bak = $"{path}.bak.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            try
            {
                File.Move(path, bak);
            }
            catch
            {
                // If even the rename fails, leave the file in place; we still report it.
            }
            _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                $"Preset.Load corrupted file={path} -> {bak} : {ex.Message}",
                new { file = path, bak });
            return bak;
        }

        // ---------- DTO mapping ----------

        private static PresetRecord ToRecord(PresetFileDto dto)
        {
            var assigns = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (dto.assignments is not null)
            {
                foreach (var kv in dto.assignments) assigns[kv.Key] = kv.Value;
            }
            var settings = new Dictionary<string, IReadOnlyDictionary<string, SettingValue>>(StringComparer.Ordinal);
            if (dto.settings is not null)
            {
                foreach (var slot in dto.settings)
                {
                    var inner = new Dictionary<string, SettingValue>(StringComparer.Ordinal);
                    foreach (var kv in slot.Value)
                    {
                        if (Enum.TryParse<SettingType>(kv.Value.type, ignoreCase: true, out var t))
                        {
                            inner[kv.Key] = SettingValue.FromJson(t, kv.Value.value);
                        }
                    }
                    settings[slot.Key] = inner;
                }
            }
            return new PresetRecord
            {
                Header = new PresetHeader
                {
                    PresetId = dto.presetId,
                    Name = dto.name,
                    LastModifiedAt = DateTimeOffset.TryParse(dto.lastModifiedAt, out var ts)
                        ? ts
                        : DateTimeOffset.MinValue,
                },
                Assignments = assigns,
                Settings = settings,
            };
        }

        private static PresetFileDto ToDto(PresetRecord record)
        {
            var assigns = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var kv in record.Assignments) assigns[kv.Key] = kv.Value;
            var settings = new Dictionary<string, Dictionary<string, SettingValueDto>>(StringComparer.Ordinal);
            foreach (var slot in record.Settings)
            {
                var inner = new Dictionary<string, SettingValueDto>(StringComparer.Ordinal);
                foreach (var kv in slot.Value)
                {
                    inner[kv.Key] = new SettingValueDto
                    {
                        type = kv.Value.Type.ToString(),
                        value = kv.Value.ToJson(),
                    };
                }
                settings[slot.Key] = inner;
            }
            return new PresetFileDto
            {
                version = 1,
                presetId = record.Header.PresetId,
                name = record.Header.Name,
                lastModifiedAt = record.Header.LastModifiedAt.UtcDateTime.ToString("O"),
                assignments = assigns,
                settings = settings,
            };
        }

        // ---------- internal DTOs (file format) ----------

        private sealed class PresetFileDto
        {
            public int version { get; set; } = 1;
            public string presetId { get; set; } = "";
            public string name { get; set; } = "";
            public string? lastModifiedAt { get; set; }
            public Dictionary<string, string?>? assignments { get; set; }
            public Dictionary<string, Dictionary<string, SettingValueDto>>? settings { get; set; }
        }

        private sealed class SettingValueDto
        {
            public string type { get; set; } = "Float";
            public JsonElement value { get; set; }
        }

        private sealed class ActiveFileDto
        {
            public string? activePresetId { get; set; }
        }
    }
}
