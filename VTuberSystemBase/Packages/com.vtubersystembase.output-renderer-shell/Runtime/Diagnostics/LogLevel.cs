#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Diagnostics
{
    /// <summary>
    /// <see cref="OutputShellLogger"/> の最小レベル設定で使用するログレベル。
    /// </summary>
    /// <remarks>
    /// Req 9.7（ログレベル外部切替）に対応する。値の昇順は重要度の昇順を表し、
    /// 設定された最小レベルより低いログ呼び出しは抑制される。
    /// 既定値は <see cref="Verbose"/>（=0）であり、明示的な指定なしでフィールドが
    /// 初期化された場合はすべてのログが出力される。
    /// </remarks>
    public enum LogLevel
    {
        /// <summary>詳細トレース。シーン初期化各段階の進捗ログなど（Req 9.1）。</summary>
        Verbose = 0,

        /// <summary>通常運用での情報通知（接続イベント・ディスプレイ切替結果など）。</summary>
        Info = 1,

        /// <summary>運用継続は可能だが注意が必要な事象（フォールバック発動・未登録コマンド受信など）。</summary>
        Warning = 2,

        /// <summary>ハンドラ例外・初期化失敗など、診断対象とすべき重大事象。</summary>
        Error = 3,
    }
}
