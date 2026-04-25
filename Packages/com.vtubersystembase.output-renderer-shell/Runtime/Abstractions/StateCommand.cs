#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// state 系コマンド（<see cref="OutputCommandKind.State"/>）として登録ハンドラへ届く受信メッセージ。
    /// </summary>
    /// <typeparam name="TPayload">ペイロードの C# 型。</typeparam>
    /// <remarks>
    /// <para>
    /// state はトピック単位で coalesce 対象（D-7）。同一 <see cref="Topic"/> へ短時間に複数値が届いた場合、
    /// 最新値のみがハンドラに渡る可能性がある（Last-write-wins, OR-2）。
    /// </para>
    /// <para>
    /// <strong>ハンドラ実装契約（Req 4.4）</strong>: state ハンドラは <em>冪等</em> でなければならない。
    /// 同一入力に対して同一結果を返し、副作用は累積しない（直接代入のみ、加算・カウンタ・キュー追加などは禁止）。
    /// 冪等性が満たされない場合、coalesce による中間値スキップで状態が破綻する。
    /// </para>
    /// <para>
    /// <strong>描画禁止契約（Req 5.6）</strong>: state ハンドラ内からメイン出力サーフェス（Display 2+）へ
    /// <c>OnGUI</c> / <c>IMGUI</c> / UI Toolkit 経路で GUI / テキスト / デバッグオーバーレイを描画してはならない。
    /// 診断表示は UI 側（Display 1）または Unity Console のみを利用する。
    /// </para>
    /// </remarks>
    public readonly record struct StateCommand<TPayload>
    {
        /// <summary>受信トピック。既定値時は <c>null</c>。</summary>
        public string? Topic { get; init; }

        /// <summary>ペイロード本体。<typeparamref name="TPayload"/> の既定値で初期化される。</summary>
        public TPayload? Payload { get; init; }

        /// <summary>受信時刻（<c>DateTime.UtcNow.Ticks</c> 相当、診断用途）。</summary>
        public long ReceivedAtTicks { get; init; }
    }
}
