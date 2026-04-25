#nullable enable
using System;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Configuration
{
    [CreateAssetMenu(
        fileName = "CoreIpcConfig",
        menuName = "VTuberSystemBase/Core IPC Config",
        order = 1000)]
    public sealed class CoreIpcConfigAsset : ScriptableObject
    {
        [SerializeField] private string host = "127.0.0.1";

        [SerializeField] private int port = 61874;

        [SerializeField, Min(0.0f)] private double defaultRequestTimeoutSeconds = 5.0;

        [SerializeField, Min(0.0f)] private double reconnectInitialDelaySeconds = 0.25;

        [SerializeField, Min(0.0f)] private double reconnectMultiplier = 2.0;

        [SerializeField, Min(0.0f)] private double reconnectMaxDelaySeconds = 5.0;

        [SerializeField, Min(0)] private int reconnectMaxAttempts = 20;

        [SerializeField, Min(1)] private long maxMessageSizeBytes = 1_048_576;

        [SerializeField, Min(1)] private int eventQueueWarningThresholdPerTopic = 1000;

        [SerializeField] private LogLevel logLevel = LogLevel.Info;

        public string Host => host;

        public int Port => port;

        public double DefaultRequestTimeoutSeconds => defaultRequestTimeoutSeconds;

        public double ReconnectInitialDelaySeconds => reconnectInitialDelaySeconds;

        public double ReconnectMultiplier => reconnectMultiplier;

        public double ReconnectMaxDelaySeconds => reconnectMaxDelaySeconds;

        public int ReconnectMaxAttempts => reconnectMaxAttempts;

        public long MaxMessageSizeBytes => maxMessageSizeBytes;

        public int EventQueueWarningThresholdPerTopic => eventQueueWarningThresholdPerTopic;

        public LogLevel LogLevel => logLevel;

        public CoreIpcOptions ToOptions() => new CoreIpcOptions
        {
            Host = string.IsNullOrEmpty(host) ? "127.0.0.1" : host,
            Port = port,
            DefaultRequestTimeout = TimeSpan.FromSeconds(defaultRequestTimeoutSeconds),
            ReconnectInitialDelay = TimeSpan.FromSeconds(reconnectInitialDelaySeconds),
            ReconnectMultiplier = reconnectMultiplier,
            ReconnectMaxDelay = TimeSpan.FromSeconds(reconnectMaxDelaySeconds),
            ReconnectMaxAttempts = reconnectMaxAttempts,
            MaxMessageSizeBytes = maxMessageSizeBytes,
            EventQueueWarningThresholdPerTopic = eventQueueWarningThresholdPerTopic,
            LogLevel = logLevel,
        };

        public static CoreIpcConfigAsset Create(CoreIpcOptions options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));

            var asset = ScriptableObject.CreateInstance<CoreIpcConfigAsset>();
            asset.host = options.Host;
            asset.port = options.Port;
            asset.defaultRequestTimeoutSeconds = options.DefaultRequestTimeout.TotalSeconds;
            asset.reconnectInitialDelaySeconds = options.ReconnectInitialDelay.TotalSeconds;
            asset.reconnectMultiplier = options.ReconnectMultiplier;
            asset.reconnectMaxDelaySeconds = options.ReconnectMaxDelay.TotalSeconds;
            asset.reconnectMaxAttempts = options.ReconnectMaxAttempts;
            asset.maxMessageSizeBytes = options.MaxMessageSizeBytes;
            asset.eventQueueWarningThresholdPerTopic = options.EventQueueWarningThresholdPerTopic;
            asset.logLevel = options.LogLevel;
            return asset;
        }
    }
}
