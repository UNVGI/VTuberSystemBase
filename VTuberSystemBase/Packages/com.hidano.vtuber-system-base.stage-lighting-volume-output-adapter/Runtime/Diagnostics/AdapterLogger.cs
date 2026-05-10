#nullable enable
using System;
using UnityEngine;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Diagnostics
{
    /// <summary>
    /// Severity levels handled by <see cref="AdapterLogger"/>. Ordering matters:
    /// <see cref="Verbose"/> &lt; <see cref="Info"/> &lt; <see cref="Warning"/> &lt; <see cref="Error"/>.
    /// </summary>
    internal enum AdapterLogLevel
    {
        Verbose = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>
    /// Mutable configuration for <see cref="AdapterLogger"/>. Threading: writes are atomic
    /// reference assignments to <see cref="MinLevel"/> only. Production callers should treat
    /// this as set-once at startup.
    /// </summary>
    internal sealed class AdapterLoggerConfig
    {
        public AdapterLogLevel MinLevel { get; set; } = AdapterLogLevel.Info;
    }

    /// <summary>
    /// Thin wrapper over <see cref="UnityEngine.Debug"/> that emits structured single-line
    /// log entries in the form
    /// <c>[StageLightingVolumeOutputAdapter] {component}.{event}: {context}</c>.
    /// Optional structured fields (topic, lightId, typeFullName, paramName, exception)
    /// are appended only when non-null. Output goes exclusively to the Unity Console
    /// (never to a render surface).
    /// </summary>
    internal sealed class AdapterLogger
    {
        private const string Prefix = "[StageLightingVolumeOutputAdapter] ";
        private readonly AdapterLoggerConfig _config;

        public AdapterLogger() : this(new AdapterLoggerConfig()) { }
        public AdapterLogger(AdapterLoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public AdapterLoggerConfig Config => _config;

        public bool IsEnabled(AdapterLogLevel level) => level >= _config.MinLevel;

        public void Verbose(string component, string evt, string? context = null,
            string? topic = null, string? lightId = null, string? typeFullName = null,
            string? paramName = null, Exception? exception = null)
            => Emit(AdapterLogLevel.Verbose, component, evt, context, topic, lightId, typeFullName, paramName, exception);

        public void Info(string component, string evt, string? context = null,
            string? topic = null, string? lightId = null, string? typeFullName = null,
            string? paramName = null, Exception? exception = null)
            => Emit(AdapterLogLevel.Info, component, evt, context, topic, lightId, typeFullName, paramName, exception);

        public void Warning(string component, string evt, string? context = null,
            string? topic = null, string? lightId = null, string? typeFullName = null,
            string? paramName = null, Exception? exception = null)
            => Emit(AdapterLogLevel.Warning, component, evt, context, topic, lightId, typeFullName, paramName, exception);

        public void Error(string component, string evt, string? context = null,
            string? topic = null, string? lightId = null, string? typeFullName = null,
            string? paramName = null, Exception? exception = null)
            => Emit(AdapterLogLevel.Error, component, evt, context, topic, lightId, typeFullName, paramName, exception);

        private void Emit(AdapterLogLevel level, string component, string evt, string? context,
            string? topic, string? lightId, string? typeFullName, string? paramName, Exception? exception)
        {
            if (!IsEnabled(level)) return;

            var sb = new System.Text.StringBuilder(128);
            sb.Append(Prefix).Append(component ?? "?").Append('.').Append(evt ?? "?");
            if (!string.IsNullOrEmpty(context))
            {
                sb.Append(": ").Append(context);
            }
            AppendField(sb, "topic", topic);
            AppendField(sb, "lightId", lightId);
            AppendField(sb, "type", typeFullName);
            AppendField(sb, "param", paramName);
            if (exception != null)
            {
                sb.Append(" exception=").Append(exception.GetType().Name).Append(":").Append(exception.Message);
            }

            var line = sb.ToString();
            switch (level)
            {
                case AdapterLogLevel.Error:
                    Debug.LogError(line);
                    break;
                case AdapterLogLevel.Warning:
                    Debug.LogWarning(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }

        private static void AppendField(System.Text.StringBuilder sb, string name, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.Append(' ').Append(name).Append('=').Append(value);
        }
    }
}
