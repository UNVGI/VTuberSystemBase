#nullable enable
using System;
using System.IO;
using System.Text.Json;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Configuration
{
    public static class CoreIpcConfigLoader
    {
        public const string DefaultResourceName = "CoreIpcConfig";

        public const string DefaultStreamingAssetsFileName = "core-ipc-config.json";

        public const string DefaultAppDataSubdirectory = "VTuberSystemBase";

        public const string DefaultAppDataFileName = "core-ipc-config.json";

        public sealed class LoadContext
        {
            public Func<CoreIpcConfigAsset?>? ResourceLoader { get; init; }

            public Func<string?>? StreamingAssetsJsonProvider { get; init; }

            public Func<string?>? AppDataJsonProvider { get; init; }
        }

        public static CoreIpcOptions Load() => Load(CreateDefaultContext());

        public static CoreIpcOptions Load(LoadContext context)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));

            var options = new CoreIpcOptions();

            var asset = SafeInvoke(context.ResourceLoader);
            if (asset != null)
            {
                options = asset.ToOptions();
            }

            var streamingJson = SafeInvoke(context.StreamingAssetsJsonProvider);
            if (!string.IsNullOrWhiteSpace(streamingJson))
            {
                options = ApplyJson(options, streamingJson!, "StreamingAssets");
            }

            var appDataJson = SafeInvoke(context.AppDataJsonProvider);
            if (!string.IsNullOrWhiteSpace(appDataJson))
            {
                options = ApplyJson(options, appDataJson!, "%AppData%");
            }

            return options;
        }

        public static string GetDefaultStreamingAssetsPath() =>
            Path.Combine(Application.streamingAssetsPath, DefaultStreamingAssetsFileName);

        public static string GetDefaultAppDataPath()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(baseDir, DefaultAppDataSubdirectory, DefaultAppDataFileName);
        }

        private static LoadContext CreateDefaultContext() => new LoadContext
        {
            ResourceLoader = () => Resources.Load<CoreIpcConfigAsset>(DefaultResourceName),
            StreamingAssetsJsonProvider = ReadFileIfExists(GetDefaultStreamingAssetsPath()),
            AppDataJsonProvider = ReadFileIfExists(GetDefaultAppDataPath()),
        };

        private static Func<string?> ReadFileIfExists(string path) => () =>
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CoreIpcConfigLoader] Failed to read config '{path}': {ex.Message}");
                return null;
            }
        };

        private static T? SafeInvoke<T>(Func<T?>? provider) where T : class
        {
            if (provider is null) return null;
            try
            {
                return provider();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CoreIpcConfigLoader] Provider threw: {ex.Message}");
                return null;
            }
        }

        private static CoreIpcOptions ApplyJson(CoreIpcOptions baseline, string json, string source)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    Debug.LogWarning(
                        $"[CoreIpcConfigLoader] Config from {source} is not a JSON object; ignored.");
                    return baseline;
                }

                var result = baseline;

                if (TryGetCaseInsensitive(root, "host", out var hostElement)
                    && hostElement.ValueKind == JsonValueKind.String)
                {
                    var v = hostElement.GetString();
                    if (!string.IsNullOrEmpty(v))
                    {
                        result = result with { Host = v! };
                    }
                }

                if (TryGetCaseInsensitive(root, "port", out var portElement)
                    && portElement.ValueKind == JsonValueKind.Number
                    && portElement.TryGetInt32(out var portValue))
                {
                    result = result with { Port = portValue };
                }

                if (TryGetSeconds(root, "defaultRequestTimeoutSeconds", out var dt))
                {
                    result = result with { DefaultRequestTimeout = TimeSpan.FromSeconds(dt) };
                }

                if (TryGetSeconds(root, "reconnectInitialDelaySeconds", out var rid))
                {
                    result = result with { ReconnectInitialDelay = TimeSpan.FromSeconds(rid) };
                }

                if (TryGetCaseInsensitive(root, "reconnectMultiplier", out var multElement)
                    && multElement.ValueKind == JsonValueKind.Number
                    && multElement.TryGetDouble(out var multValue))
                {
                    result = result with { ReconnectMultiplier = multValue };
                }

                if (TryGetSeconds(root, "reconnectMaxDelaySeconds", out var rmd))
                {
                    result = result with { ReconnectMaxDelay = TimeSpan.FromSeconds(rmd) };
                }

                if (TryGetCaseInsensitive(root, "reconnectMaxAttempts", out var attemptsElement)
                    && attemptsElement.ValueKind == JsonValueKind.Number
                    && attemptsElement.TryGetInt32(out var attemptsValue))
                {
                    result = result with { ReconnectMaxAttempts = attemptsValue };
                }

                if (TryGetCaseInsensitive(root, "maxMessageSizeBytes", out var sizeElement)
                    && sizeElement.ValueKind == JsonValueKind.Number
                    && sizeElement.TryGetInt64(out var sizeValue))
                {
                    result = result with { MaxMessageSizeBytes = sizeValue };
                }

                if (TryGetCaseInsensitive(root, "eventQueueWarningThresholdPerTopic", out var thrElement)
                    && thrElement.ValueKind == JsonValueKind.Number
                    && thrElement.TryGetInt32(out var thrValue))
                {
                    result = result with { EventQueueWarningThresholdPerTopic = thrValue };
                }

                if (TryGetCaseInsensitive(root, "logLevel", out var logElement))
                {
                    if (logElement.ValueKind == JsonValueKind.String
                        && Enum.TryParse<LogLevel>(logElement.GetString(), ignoreCase: true, out var parsed))
                    {
                        result = result with { LogLevel = parsed };
                    }
                    else if (logElement.ValueKind == JsonValueKind.Number
                        && logElement.TryGetInt32(out var levelInt)
                        && Enum.IsDefined(typeof(LogLevel), levelInt))
                    {
                        result = result with { LogLevel = (LogLevel)levelInt };
                    }
                }

                return result;
            }
            catch (JsonException ex)
            {
                Debug.LogWarning(
                    $"[CoreIpcConfigLoader] Invalid JSON from {source}: {ex.Message}");
                return baseline;
            }
        }

        private static bool TryGetSeconds(in JsonElement root, string name, out double value)
        {
            value = 0;
            if (!TryGetCaseInsensitive(root, name, out var element)) return false;
            if (element.ValueKind != JsonValueKind.Number) return false;
            if (!element.TryGetDouble(out var v)) return false;
            value = v;
            return true;
        }

        private static bool TryGetCaseInsensitive(in JsonElement root, string name, out JsonElement value)
        {
            if (root.TryGetProperty(name, out value))
            {
                return true;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
