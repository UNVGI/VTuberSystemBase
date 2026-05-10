using System;

namespace VTuberSystemBase.RacMainOutputAdapter.Diagnostics
{
    /// <summary>
    /// 本アダプタ専用のログ出力抽象。<c>ui-toolkit-shell</c> の <c>IDiagnosticsLogger</c> とは独立。
    /// 既定実装（<see cref="UnityConsoleDiagnosticsLogger"/>）は Unity Console へ流す。
    /// テスト時は <c>FakeDiagnosticsLogger</c> 等で記録のみに差し替える。
    /// </summary>
    /// <remarks>
    /// <para>
    /// メイン出力サーフェス（Display 2+）に <c>OnGUI</c> / <c>UIDocument</c> 経由のログ描画を行ってはならない
    /// （Requirement 10.9）。実装はすべて Unity Console もしくはテスト記録経路のみを使う。
    /// </para>
    /// </remarks>
    public interface IDiagnosticsLogger
    {
        /// <summary>現在の最小ログレベル。これより低いレベルは出力しない（Requirement 10.8）。</summary>
        AdapterLogLevel MinimumLevel { get; set; }

        /// <summary>
        /// ログを 1 件出力する。<paramref name="category"/> は <see cref="AdapterLogCategories"/> の文字列定数。
        /// </summary>
        void Log(AdapterLogLevel level, string category, string message, Exception exception = null);
    }

    /// <summary>
    /// 重大度。<c>Trace &lt; Debug &lt; Info &lt; Warning &lt; Error</c>。
    /// </summary>
    public enum AdapterLogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
    }
}
