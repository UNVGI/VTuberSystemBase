#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.StageLightingVolumeTab.Contracts;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// File-system <see cref="IPresetStorage"/> for the stage-lighting-volume tab. Persists
    /// the entire <see cref="PresetFileRoot"/> as a single JSON document via atomic
    /// write (temp file + <see cref="File.Move(string, string, bool)"/>) and quarantines
    /// corrupted files to <c>{path}.corrupted-{unixMs}</c> on parse failure.
    /// See design.md §Services §JsonPresetStorage (Requirements 8.1, 8.3, 8.4, 8.5, 8.7,
    /// 8.9, 8.10, 10.5).
    /// </summary>
    public sealed class JsonPresetStorage : IPresetStorage
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string _filePath;
        private readonly IDiagnosticsLogger? _log;
        private readonly SemaphoreSlim _ioGate = new SemaphoreSlim(1, 1);

        public JsonPresetStorage(string filePath, IDiagnosticsLogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath required", nameof(filePath));
            _filePath = filePath;
            _log = logger;
        }

        public string FilePath => _filePath;

        public async Task<PresetLoadResult> LoadAsync(CancellationToken ct = default)
        {
            await _ioGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new PresetLoadResult { Success = true, Data = null };
                }

                string text;
                try
                {
                    text = await ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                        $"Preset.Load read I/O error path={_filePath} : {ex.Message}",
                        new { file = _filePath });
                    return new PresetLoadResult
                    {
                        Success = false,
                        Error = PresetLoadError.IOError,
                    };
                }

                try
                {
                    var data = JsonSerializer.Deserialize<PresetFileRoot>(text, JsonOpts);
                    if (data is null)
                    {
                        // null root is treated as parse error to avoid silently losing data.
                        var backup = QuarantineCorruptedFile(new InvalidDataException("null root"));
                        return new PresetLoadResult
                        {
                            Success = true,
                            Data = null,
                            CorruptedBackupPath = backup,
                        };
                    }
                    return new PresetLoadResult { Success = true, Data = data };
                }
                catch (JsonException ex)
                {
                    var backup = QuarantineCorruptedFile(ex);
                    return new PresetLoadResult
                    {
                        Success = true,
                        Data = null,
                        CorruptedBackupPath = backup,
                    };
                }
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public async Task<SaveResult> SaveAsync(PresetFileRoot root, CancellationToken ct = default)
        {
            if (root is null) throw new ArgumentNullException(nameof(root));

            await _ioGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await SaveLockedAsync(root, ct).ConfigureAwait(false);
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public async Task<SaveResult> FlushAsync(CancellationToken ct = default)
        {
            // Acquire and release the gate; this drains any in-flight save and proves
            // no other writer is mid-write. We do not start an additional save here -
            // pending data is held by the ViewModel layer through DebounceFlusher.
            await _ioGate.WaitAsync(ct).ConfigureAwait(false);
            _ioGate.Release();
            return SaveResult.Ok();
        }

        // --------------------------------------------------------------------

        private async Task<SaveResult> SaveLockedAsync(PresetFileRoot root, CancellationToken ct)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(root, JsonOpts);
                var tempPath = _filePath + ".tmp";

                // Best-effort cleanup of stale temp file from an earlier crashed write.
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // If we cannot delete the stale file we let the write below surface the
                    // error.
                }

                await WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);

                // Best-effort atomic swap: delete existing then move. On the same
                // volume File.Move is atomic; the brief window between Delete and
                // Move is acceptable as the temp file is the only writer.
                if (File.Exists(_filePath)) File.Delete(_filePath);
                File.Move(tempPath, _filePath);

                return SaveResult.Ok();
            }
            catch (UnauthorizedAccessException ex)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"Preset.Save permission denied path={_filePath} : {ex.Message}",
                    new { file = _filePath });
                return SaveResult.Fail(PresetSaveError.PermissionDenied);
            }
            catch (IOException ex) when (IsDiskFullException(ex))
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"Preset.Save disk full path={_filePath} : {ex.Message}",
                    new { file = _filePath });
                return SaveResult.Fail(PresetSaveError.DiskFull);
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"Preset.Save IO error path={_filePath} : {ex.Message}",
                    new { file = _filePath });
                return SaveResult.Fail(PresetSaveError.IOError);
            }
        }

        private string? QuarantineCorruptedFile(Exception cause)
        {
            try
            {
                var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var backup = _filePath + ".corrupted-" + unixMs;
                File.Move(_filePath, backup);
                _log?.Log(LogLevel.Warning, LogCategory.TabSpec,
                    $"Preset.Load corrupted file quarantined path={_filePath} -> {backup} : {cause.Message}",
                    new { file = _filePath, backup });
                return backup;
            }
            catch (Exception ex)
            {
                _log?.Log(LogLevel.Error, LogCategory.TabSpec,
                    $"Preset.Load corrupt-rename failed path={_filePath} : {ex.Message}",
                    new { file = _filePath });
                return null;
            }
        }

        private static async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
        {
            using var reader = new StreamReader(path);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        private static async Task WriteAllTextAsync(string path, string text, CancellationToken ct)
        {
            using var writer = new StreamWriter(path, append: false);
            await writer.WriteAsync(text).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }

        private static bool IsDiskFullException(IOException ex)
        {
            // Windows ERROR_DISK_FULL (0x70) and ERROR_HANDLE_DISK_FULL (0x27) live in
            // the lower 16 bits of HResult.
            const int ErrorDiskFull = 0x70;
            const int ErrorHandleDiskFull = 0x27;
            int code = ex.HResult & 0xFFFF;
            return code == ErrorDiskFull || code == ErrorHandleDiskFull;
        }
    }
}
