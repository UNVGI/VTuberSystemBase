#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// <see cref="IDisplayRoutingService"/> がメイン出力カメラへ割り当てたディスプレイの状態。
    /// 既定値（<c>default</c>）でも NPE を起こさない不変な値オブジェクト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IsFallbackActive"/> は OR-1（要求された Display N が存在せず Display 0 へフォールバック）
    /// が発生した状態を表し、UI 側／運用ツールが「誤配信リスクあり」を検出するための判定値となる
    /// （Req 2.4 / 2.4a）。
    /// </para>
    /// <para>
    /// <see cref="IsEditorLimitedMode"/> は Unity Editor PlayMode における
    /// <c>Display.displays[n].Activate()</c> 制限（Req 6.8）の発生を示す。
    /// スタンドアロンビルドでは常に <c>false</c>。
    /// </para>
    /// </remarks>
    public readonly record struct DisplayAssignmentInfo
    {
        /// <summary>呼び出し元から要求されたディスプレイインデックス（0-based、既定 1 = Display 2）。</summary>
        public int RequestedDisplayIndex { get; init; }

        /// <summary>実際に割り当てられたディスプレイインデックス（フォールバック発生時は 0）。</summary>
        public int EffectiveDisplayIndex { get; init; }

        /// <summary>
        /// 要求ディスプレイ不在で Display 0 へフォールバックした場合に <c>true</c>（OR-1 / Req 2.4）。
        /// </summary>
        public bool IsFallbackActive { get; init; }

        /// <summary>
        /// Unity Editor PlayMode 固有の Display.Activate 制限が適用された場合に <c>true</c>（Req 6.8）。
        /// </summary>
        public bool IsEditorLimitedMode { get; init; }

        /// <summary>診断ログ／UI 表示用の補足メッセージ。null の場合あり（既定値時）。</summary>
        public string? DiagnosticMessage { get; init; }
    }
}
