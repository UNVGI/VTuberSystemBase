using System;
using UnityEngine;

namespace VTuberSystemBase.RacMainOutputAdapter.Diagnostics
{
    /// <summary>
    /// <see cref="IDiagnosticsLogger"/> の既定実装。<see cref="UnityEngine.Debug"/> へ出力する。
    /// </summary>
    public sealed class UnityConsoleDiagnosticsLogger : IDiagnosticsLogger
    {
        /// <inheritdoc/>
        public AdapterLogLevel MinimumLevel { get; set; } = AdapterLogLevel.Info;

        /// <inheritdoc/>
        public void Log(AdapterLogLevel level, string category, string message, Exception exception = null)
        {
            if (level < MinimumLevel) return;

            var prefix = $"[RacMainOutputAdapter/{category}]";
            var fullMessage = exception != null ? $"{prefix} {message} ({exception})" : $"{prefix} {message}";

            switch (level)
            {
                case AdapterLogLevel.Error:
                    UnityEngine.Debug.LogError(fullMessage);
                    break;
                case AdapterLogLevel.Warning:
                    UnityEngine.Debug.LogWarning(fullMessage);
                    break;
                default:
                    UnityEngine.Debug.Log(fullMessage);
                    break;
            }
        }
    }
}
