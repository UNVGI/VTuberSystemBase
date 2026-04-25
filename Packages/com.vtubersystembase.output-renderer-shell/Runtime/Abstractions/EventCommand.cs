#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// event 系コマンド（<see cref="OutputCommandKind.Event"/>）として登録ハンドラへ届く受信メッセージ。
    /// </summary>
    /// <typeparam name="TPayload">ペイロードの C# 型。</typeparam>
    /// <remarks>
    /// <para>
    /// event は到着順 FIFO で配信され、取りこぼしは発生しない（D-7 / Req 4.3 / 4.9）。
    /// 複数クライアントから同一トピックへ event が送られた場合も、到着順に全イベントが適用される（OR-2）。
    /// </para>
    /// <para>
    /// <strong>描画禁止契約（Req 5.6）</strong>: event ハンドラ内からメイン出力サーフェス（Display 2+）へ
    /// GUI / テキスト / デバッグオーバーレイを描画してはならない。
    /// </para>
    /// </remarks>
    public readonly record struct EventCommand<TPayload>
    {
        /// <summary>受信トピック。既定値時は <c>null</c>。</summary>
        public string? Topic { get; init; }

        /// <summary>ペイロード本体。<typeparamref name="TPayload"/> の既定値で初期化される。</summary>
        public TPayload? Payload { get; init; }

        /// <summary>受信時刻（<c>DateTime.UtcNow.Ticks</c> 相当、診断用途）。</summary>
        public long ReceivedAtTicks { get; init; }
    }
}
