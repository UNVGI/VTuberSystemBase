#nullable enable
using UnityEngine;

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// <see cref="IDisplayRoutingService"/> の Activate 呼び出しに渡す構成値。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定値はメイン出力を Display 2（インデックス 1）へ全画面ウィンドウ表示する構成（Req 2.2 / 2.3 / 2.7）。
    /// </para>
    /// <para>
    /// 本型は不変オブジェクトとして扱い、初期化後にプロパティを書き換えない（<c>init</c> アクセサ）。
    /// パラメータ無しコンストラクタ <c>new DisplayRoutingConfig()</c> は安全な既定値を返す。
    /// </para>
    /// </remarks>
    public sealed record DisplayRoutingConfig
    {
        /// <summary>
        /// アクティベート対象のディスプレイインデックス（0-based）。既定 1 = Display 2。
        /// 範囲外（<c>Display.displays.Length</c> 超過）の場合、暫定実装は Display 0 へフォールバックする（Req 2.4 / OR-1）。
        /// </summary>
        public int TargetDisplayIndex { get; init; } = 1;

        /// <summary>
        /// 全画面表示モード。既定 <see cref="FullScreenMode.FullScreenWindow"/>（Req 2.3）。
        /// Editor PlayMode では適用されない（Game View 制約, Req 6.8）。
        /// </summary>
        public FullScreenMode FullScreenMode { get; init; } = FullScreenMode.FullScreenWindow;

        /// <summary>
        /// Editor PlayMode 固有の警告ログを抑止する場合 <c>true</c>。既定 <c>false</c>（警告を出す）。
        /// </summary>
        public bool SuppressEditorWarning { get; init; }
    }
}
