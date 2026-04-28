#nullable enable
using System;
using System.Text;
using System.Threading;
using VTuberSystemBase.CoreIpc.Abstractions;
using UnityDebug = UnityEngine.Debug;

namespace VTuberSystemBase.CoreIpc.Core.Diagnostics
{
    public sealed class CoreIpcLogger
    {
        public const string LogTagPrefix = "[CoreIpc]";

        private readonly Action<LogLevel, string> _sink;
        private int _minimumLevel;

        public CoreIpcLogger()
            : this(LogLevel.Info, DefaultSink)
        {
        }

        public CoreIpcLogger(LogLevel minimumLevel)
            : this(minimumLevel, DefaultSink)
        {
        }

        public CoreIpcLogger(LogLevel minimumLevel, Action<LogLevel, string> sink)
        {
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            _sink = sink;
            _minimumLevel = (int)minimumLevel;
        }

        public LogLevel MinimumLevel
        {
            get => (LogLevel)Volatile.Read(ref _minimumLevel);
            set => Volatile.Write(ref _minimumLevel, (int)value);
        }

        public bool IsEnabled(LogLevel level) => (int)level >= Volatile.Read(ref _minimumLevel);

        public void Trace(string message) => Emit(LogLevel.Trace, message);

        public void Debug(string message) => Emit(LogLevel.Debug, message);

        public void Info(string message) => Emit(LogLevel.Info, message);

        public void Warning(string message) => Emit(LogLevel.Warning, message);

        public void Error(string message) => Emit(LogLevel.Error, message);

        public void Log(LogLevel level, string message) => Emit(level, message);

        public void LogConnectionEvent(
            LogLevel level,
            string eventName,
            MessageKind? kind = null,
            string? topic = null,
            string? correlationId = null,
            string? detail = null)
        {
            if (!IsEnabled(level)) return;
            _sink(level, FormatConnectionEvent(eventName, kind, topic, correlationId, detail));
        }

        public void LogConnectionStarting(string? detail = null)
            => LogConnectionEvent(LogLevel.Info, "connection.starting", detail: detail);

        public void LogConnectionEstablished(string? detail = null)
            => LogConnectionEvent(LogLevel.Info, "connection.established", detail: detail);

        public void LogConnectionClosed(string? detail = null)
            => LogConnectionEvent(LogLevel.Info, "connection.closed", detail: detail);

        public void LogConnectionReconnecting(int attempt, string? detail = null)
        {
            string composed = detail is null
                ? $"attempt={attempt}"
                : $"attempt={attempt} {detail}";
            LogConnectionEvent(LogLevel.Warning, "connection.reconnecting", detail: composed);
        }

        public static string FormatConnectionEvent(
            string eventName,
            MessageKind? kind,
            string? topic,
            string? correlationId,
            string? detail)
        {
            var sb = new StringBuilder();
            sb.Append(LogTagPrefix).Append(' ').Append(eventName ?? string.Empty);
            if (kind.HasValue) sb.Append(" kind=").Append(kind.Value);
            if (!string.IsNullOrEmpty(topic)) sb.Append(" topic=").Append(topic);
            if (!string.IsNullOrEmpty(correlationId)) sb.Append(" correlationId=").Append(correlationId);
            if (!string.IsNullOrEmpty(detail)) sb.Append(' ').Append(detail);
            return sb.ToString();
        }

        private void Emit(LogLevel level, string message)
        {
            if (!IsEnabled(level)) return;
            _sink(level, message ?? string.Empty);
        }

        private static void DefaultSink(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Error:
                    UnityDebug.LogError(message);
                    break;
                case LogLevel.Warning:
                    UnityDebug.LogWarning(message);
                    break;
                default:
                    UnityDebug.Log(message);
                    break;
            }
        }
    }
}
