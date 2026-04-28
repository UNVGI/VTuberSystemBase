#nullable enable
using System;
using System.Text;
using UnityEngine;

namespace VTuberSystemBase.OutputRendererShell.Diagnostics
{
    /// <summary>
    /// メイン出力シェル全体で利用する、ログレベル切替対応のログ薄ラッパ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 出力先は <c>UnityEngine.Debug.Log*</c> のみに限定される（Req 5.3 / 5.7 / 9.6）。
    /// 本クラスは <c>OnGUI</c> / <c>IMGUI</c> / UI Toolkit（<c>UIDocument</c> / <c>PanelSettings</c>）への
    /// 出力経路を一切持たず、メイン出力サーフェス（Display 2+）への描画は構造的に発生しない。
    /// </para>
    /// <para>
    /// <see cref="MinLevel"/> を切り替えることで下位レベルのログを抑制できる（Req 9.7）。
    /// 既定値は <see cref="LogLevel.Info"/> で、本番運用時のログ密度を抑える設計。
    /// </para>
    /// <para>
    /// 各メソッドは <c>component</c> / <c>topic</c> / <c>correlationId</c> を構造化情報として受け取り、
    /// メッセージ本文と組み合わせて Unity Console へ出力する（Req 9.1 / 9.2 / 9.3 / 9.5）。
    /// </para>
    /// </remarks>
    public sealed class OutputShellLogger
    {
        /// <summary>現在の最小ログレベル。下位レベルの呼び出しは抑制される（Req 9.7）。</summary>
        public LogLevel MinLevel { get; set; }

        /// <summary>
        /// 既定の最小レベルで <see cref="OutputShellLogger"/> を生成する。
        /// </summary>
        /// <param name="minLevel">出力対象とする最小レベル。既定は <see cref="LogLevel.Info"/>。</param>
        public OutputShellLogger(LogLevel minLevel = LogLevel.Info)
        {
            MinLevel = minLevel;
        }

        /// <summary>詳細トレースログ（Req 9.1 / 9.3）。<c>Debug.Log</c> へ出力される。</summary>
        public void Verbose(string message, string? component = null, string? topic = null, string? correlationId = null)
        {
            if (!IsEnabled(LogLevel.Verbose)) return;
            UnityEngine.Debug.Log(Format(LogLevel.Verbose, message, component, topic, correlationId, exception: null));
        }

        /// <summary>情報ログ（Req 9.2 / 9.3）。<c>Debug.Log</c> へ出力される。</summary>
        public void Info(string message, string? component = null, string? topic = null, string? correlationId = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            UnityEngine.Debug.Log(Format(LogLevel.Info, message, component, topic, correlationId, exception: null));
        }

        /// <summary>警告ログ（Req 9.4 / 接続断通知 7.5 など）。<c>Debug.LogWarning</c> へ出力される。</summary>
        public void Warning(string message, string? component = null, string? topic = null, string? correlationId = null)
        {
            if (!IsEnabled(LogLevel.Warning)) return;
            UnityEngine.Debug.LogWarning(Format(LogLevel.Warning, message, component, topic, correlationId, exception: null));
        }

        /// <summary>
        /// エラーログ（Req 9.5 / ハンドラ例外）。<c>Debug.LogError</c> へ出力される。
        /// 例外オブジェクトを与えた場合はその型名・メッセージ・スタックトレースを含めて整形する。
        /// </summary>
        public void Error(string message, Exception? exception = null, string? component = null, string? topic = null, string? correlationId = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            UnityEngine.Debug.LogError(Format(LogLevel.Error, message, component, topic, correlationId, exception));
        }

        private bool IsEnabled(LogLevel level) => (int)level >= (int)MinLevel;

        private static string Format(LogLevel level, string message, string? component, string? topic, string? correlationId, Exception? exception)
        {
            var sb = new StringBuilder(64);
            sb.Append('[').Append(level.ToString()).Append("][OutputShell]");
            if (!string.IsNullOrEmpty(component))
            {
                sb.Append('[').Append(component).Append(']');
            }
            if (!string.IsNullOrEmpty(topic))
            {
                sb.Append("[topic=").Append(topic).Append(']');
            }
            if (!string.IsNullOrEmpty(correlationId))
            {
                sb.Append("[corr=").Append(correlationId).Append(']');
            }
            sb.Append(' ').Append(message ?? string.Empty);
            if (exception != null)
            {
                sb.Append(" | ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);
                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    sb.Append('\n').Append(exception.StackTrace);
                }
            }
            return sb.ToString();
        }
    }
}
