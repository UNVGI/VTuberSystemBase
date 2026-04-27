#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// メイン出力シェルの最小状態（初期化フェーズ・ディスプレイ割当・登録ハンドラ数・直近エラー）を
    /// 外部から取得可能な読み取り専用 API（Req 2.4a / 9.8）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本インタフェースは <em>読み取り専用</em>。書き込みは本 spec 内コンポーネント
    /// （<c>OutputSceneBootstrapper</c> / <c>BuiltInDisplayRoutingService</c> /
    /// <c>OutputCommandDispatcher</c>）からのみ行われる。
    /// </para>
    /// <para>
    /// 各プロパティは任意スレッドから安全に取得できる（実装は <c>volatile</c> / <c>lock</c> で保護）。
    /// 取得が描画ループに影響しないよう、いずれも単純な getter で副作用を持たない。
    /// </para>
    /// <para>
    /// UI 側 spec（spec #3）が接続確立直後にスナップショットを取得し、運用画面に静的に表示する想定（Req 5.7）。
    /// メイン出力サーフェス（Display 2+）への描画は本インタフェースを経由しても発生しない（Req 5.3 / 9.6）。
    /// </para>
    /// </remarks>
    public interface IOutputDiagnostics
    {
        /// <summary>
        /// 現在の初期化フェーズ。<see cref="OutputSceneInitPhase.Uninitialized"/> から
        /// <see cref="OutputSceneInitPhase.Complete"/> へ単調遷移するか、
        /// 任意フェーズから <see cref="OutputSceneInitPhase.Failed"/> へ脱出する。
        /// </summary>
        OutputSceneInitPhase CurrentPhase { get; }

        /// <summary>
        /// 現在のメイン出力カメラのディスプレイ割当（フォールバック有無を含む）。
        /// 未割当時は <see cref="DisplayAssignmentInfo"/> の既定値（<c>default</c>）。
        /// </summary>
        DisplayAssignmentInfo CurrentDisplayAssignment { get; }

        /// <summary>
        /// 現在 <see cref="IOutputCommandDispatcher"/> に登録されているハンドラ件数。
        /// 未注入時は 0。
        /// </summary>
        int RegisteredHandlerCount { get; }

        /// <summary>
        /// 直近に記録された重大エラーのメッセージ（<see cref="OutputSceneInitPhase.Failed"/> 遷移時に更新）。
        /// 未発生時は <c>null</c>。
        /// </summary>
        string? LastErrorMessage { get; }

        /// <summary>
        /// <see cref="LastErrorMessage"/> 記録時の Unix エポックミリ秒。未発生時は 0。
        /// </summary>
        long LastErrorAtUnixMs { get; }
    }
}
