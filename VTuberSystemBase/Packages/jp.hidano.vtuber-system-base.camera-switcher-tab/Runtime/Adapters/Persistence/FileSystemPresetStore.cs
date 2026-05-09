#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VTuberSystemBase.CameraSwitcherTab.Contracts;
using VTuberSystemBase.CameraSwitcherTab.Contracts.Results;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CameraSwitcherTab.Adapters.Persistence
{
    /// <summary>
    /// Default <see cref="IPresetStore"/>: JSON-on-disk under
    /// <c>{Application.persistentDataPath}/camera-switcher-presets.json</c>.
    /// Atomic writes via temp-file + rename, corruption-quarantine via
    /// <c>{path}.bak.{unixMs}</c> rename, and structured failure results so the
    /// caller never sees a thrown exception (Requirement 11.7 / 11.9 / 11.10).
    /// </summary>
    public sealed class FileSystemPresetStore : IPresetStore
    {
        public const int CurrentSchemaVersion = 1;
        public const string DefaultFileName = "camera-switcher-presets.json";

        private readonly string _filePath;
        private readonly IDiagnosticsLogger? _log;
        private readonly object _ioLock = new object();

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public FileSystemPresetStore(string? filePath = null, IDiagnosticsLogger? logger = null)
        {
            _filePath = string.IsNullOrEmpty(filePath) ? DefaultFilePath() : filePath!;
            _log = logger;
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                try { Directory.CreateDirectory(dir!); } catch { /* defer to first write */ }
            }
        }

        public string FilePath => _filePath;

        public static string DefaultFilePath()
        {
            return Path.Combine(Application.persistentDataPath, DefaultFileName);
        }

        public Task<PresetLoadOutcome> LoadAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    if (!File.Exists(_filePath))
                    {
                        return new PresetLoadOutcome
                        {
                            Result = PresetIoResult.Fail(PresetIoFailureKind.FileNotFound,
                                $"file missing: {_filePath}"),
                            Presets = Array.Empty<PresetPayload>(),
                            ActivePresetName = null,
                        };
                    }

                    string text;
                    try
                    {
                        text = File.ReadAllText(_filePath);
                    }
                    catch (Exception ex)
                    {
                        _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                            $"Preset.Load read failed: {ex.Message}");
                        return new PresetLoadOutcome
                        {
                            Result = PresetIoResult.Fail(PresetIoFailureKind.ReadFailed, ex.Message, ex),
                            Presets = Array.Empty<PresetPayload>(),
                            ActivePresetName = null,
                        };
                    }

                    PresetFileDto? dto;
                    try
                    {
                        dto = JsonSerializer.Deserialize<PresetFileDto>(text, JsonOpts);
                        if (dto is null) throw new InvalidDataException("null DTO");
                    }
                    catch (Exception ex)
                    {
                        var bak = QuarantineCorrupted(ex);
                        return new PresetLoadOutcome
                        {
                            Result = PresetIoResult.Fail(PresetIoFailureKind.Corrupted, ex.Message, ex),
                            Presets = Array.Empty<PresetPayload>(),
                            ActivePresetName = null,
                            BackupPath = bak,
                        };
                    }

                    var presets = new List<PresetPayload>();
                    if (dto.presets != null)
                    {
                        foreach (var dp in dto.presets) presets.Add(ToPayload(dp));
                    }
                    return new PresetLoadOutcome
                    {
                        Result = PresetIoResult.Ok(),
                        Presets = presets,
                        ActivePresetName = dto.activePresetName,
                    };
                }
            }, cancellationToken);
        }

        public Task<PresetIoResult> SaveAllAsync(
            IReadOnlyList<PresetPayload> presets,
            string? activePresetName,
            CancellationToken cancellationToken = default)
        {
            if (presets is null) throw new ArgumentNullException(nameof(presets));
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_ioLock)
                {
                    string json;
                    try
                    {
                        var dto = new PresetFileDto
                        {
                            schemaVersion = CurrentSchemaVersion,
                            activePresetName = activePresetName,
                            presets = new List<PresetDto>(presets.Count),
                        };
                        foreach (var p in presets) dto.presets.Add(ToDto(p));
                        json = JsonSerializer.Serialize(dto, JsonOpts);
                    }
                    catch (Exception ex)
                    {
                        return PresetIoResult.Fail(PresetIoFailureKind.SerializationFailed, ex.Message, ex);
                    }

                    try
                    {
                        var dir = Path.GetDirectoryName(_filePath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
                        var tmp = _filePath + ".tmp";
                        File.WriteAllText(tmp, json);
                        if (File.Exists(_filePath)) File.Delete(_filePath);
                        File.Move(tmp, _filePath);
                        return PresetIoResult.Ok();
                    }
                    catch (Exception ex)
                    {
                        _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                            $"Preset.Save write failed: {ex.Message}");
                        return PresetIoResult.Fail(PresetIoFailureKind.WriteFailed, ex.Message, ex);
                    }
                }
            }, cancellationToken);
        }

        private string? QuarantineCorrupted(Exception ex)
        {
            var bak = $"{_filePath}.bak.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            try
            {
                File.Move(_filePath, bak);
            }
            catch (Exception moveEx)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"Preset.Load corrupted but backup-rename failed: {moveEx.Message}");
                return null;
            }
            _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                $"Preset.Load corrupted file={_filePath} -> {bak} : {ex.Message}");
            return bak;
        }

        // ---- DTO mapping ----

        private static PresetPayload ToPayload(PresetDto dto)
        {
            var cameras = new List<PresetCameraEntry>(dto.cameras?.Count ?? 0);
            if (dto.cameras != null)
            {
                foreach (var c in dto.cameras)
                {
                    cameras.Add(new PresetCameraEntry
                    {
                        LogicalId = c.logicalId ?? string.Empty,
                        DisplayName = c.displayName ?? string.Empty,
                        Type = CameraTypeNames.Parse(c.type),
                        DefaultTransform = new CameraDefaultTransform
                        {
                            Position = c.position ?? new float[] { 0, 0, 0 },
                            Rotation = c.rotation ?? new float[] { 0, 0, 0, 1 },
                            FocalLengthMm = c.focalLengthMm,
                        },
                    });
                }
            }
            var volumeConfigs = new Dictionary<string, VolumeConfig>(StringComparer.Ordinal);
            if (dto.volumeConfigs != null)
            {
                foreach (var kv in dto.volumeConfigs)
                {
                    var overrides = new List<VolumeOverride>();
                    if (kv.Value.overrides != null)
                    {
                        foreach (var ov in kv.Value.overrides)
                        {
                            var paramValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                            if (ov.paramValues != null)
                            {
                                foreach (var pv in ov.paramValues) paramValues[pv.Key] = pv.Value;
                            }
                            overrides.Add(new VolumeOverride
                            {
                                Type = ov.type ?? string.Empty,
                                Enabled = ov.enabled,
                                ParamValues = paramValues,
                            });
                        }
                    }
                    volumeConfigs[kv.Key] = new VolumeConfig
                    {
                        Enabled = kv.Value.enabled,
                        Overrides = overrides,
                    };
                }
            }
            return new PresetPayload
            {
                Name = dto.name ?? string.Empty,
                Cameras = cameras,
                VolumeConfigs = volumeConfigs,
                ActiveCameraLogicalId = dto.activeCameraLogicalId,
            };
        }

        private static PresetDto ToDto(PresetPayload payload)
        {
            var cameras = new List<PresetCameraDto>(payload.Cameras.Count);
            foreach (var c in payload.Cameras)
            {
                cameras.Add(new PresetCameraDto
                {
                    logicalId = c.LogicalId,
                    displayName = c.DisplayName,
                    type = CameraTypeNames.ToWire(c.Type) ?? string.Empty,
                    position = c.DefaultTransform.Position,
                    rotation = c.DefaultTransform.Rotation,
                    focalLengthMm = c.DefaultTransform.FocalLengthMm,
                });
            }
            var volumeConfigs = new Dictionary<string, VolumeConfigDto>(StringComparer.Ordinal);
            foreach (var kv in payload.VolumeConfigs)
            {
                var overrides = new List<VolumeOverrideDto>();
                foreach (var ov in kv.Value.Overrides)
                {
                    var paramValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var pv in ov.ParamValues) paramValues[pv.Key] = pv.Value;
                    overrides.Add(new VolumeOverrideDto
                    {
                        type = ov.Type,
                        enabled = ov.Enabled,
                        paramValues = paramValues,
                    });
                }
                volumeConfigs[kv.Key] = new VolumeConfigDto
                {
                    enabled = kv.Value.Enabled,
                    overrides = overrides,
                };
            }
            return new PresetDto
            {
                name = payload.Name,
                cameras = cameras,
                volumeConfigs = volumeConfigs,
                activeCameraLogicalId = payload.ActiveCameraLogicalId,
            };
        }

        // ---- File-format DTOs ----

        private sealed class PresetFileDto
        {
            public int schemaVersion { get; set; } = CurrentSchemaVersion;
            public string? activePresetName { get; set; }
            public List<PresetDto>? presets { get; set; }
        }

        private sealed class PresetDto
        {
            public string? name { get; set; }
            public List<PresetCameraDto>? cameras { get; set; }
            public Dictionary<string, VolumeConfigDto>? volumeConfigs { get; set; }
            public string? activeCameraLogicalId { get; set; }
        }

        private sealed class PresetCameraDto
        {
            public string? logicalId { get; set; }
            public string? displayName { get; set; }
            public string? type { get; set; }
            public float[]? position { get; set; }
            public float[]? rotation { get; set; }
            public float focalLengthMm { get; set; }
        }

        private sealed class VolumeConfigDto
        {
            public bool enabled { get; set; } = true;
            public List<VolumeOverrideDto>? overrides { get; set; }
        }

        private sealed class VolumeOverrideDto
        {
            public string? type { get; set; }
            public bool enabled { get; set; } = true;
            public Dictionary<string, JsonElement>? paramValues { get; set; }
        }
    }
}
