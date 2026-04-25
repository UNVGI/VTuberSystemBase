#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public sealed record CoreIpcOptions
    {
        public string Host { get; init; } = "127.0.0.1";

        public int Port { get; init; } = 61874;

        public TimeSpan DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan ReconnectInitialDelay { get; init; } = TimeSpan.FromMilliseconds(250);

        public double ReconnectMultiplier { get; init; } = 2.0;

        public TimeSpan ReconnectMaxDelay { get; init; } = TimeSpan.FromSeconds(5);

        public int ReconnectMaxAttempts { get; init; } = 20;

        public long MaxMessageSizeBytes { get; init; } = 1_048_576;

        public int EventQueueWarningThresholdPerTopic { get; init; } = 1000;

        public LogLevel LogLevel { get; init; } = LogLevel.Info;
    }

    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
    }
}
