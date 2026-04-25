#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// request 系コマンド（<see cref="OutputCommandKind.Request"/>）として登録ハンドラへ届く受信メッセージ。
    /// </summary>
    /// <typeparam name="TRequest">リクエストペイロードの C# 型。</typeparam>
    /// <remarks>
    /// <para>
    /// request/response は <see cref="CorrelationId"/> によって 1:1 に対応付けられ、coalesce 対象外（D-10 / Req 4.7）。
    /// ハンドラは同期的に <c>TResponse</c> を返却し、ディスパッチャがそれを response エンベロープへ詰めて返信する。
    /// </para>
    /// <para>
    /// <strong>描画禁止契約（Req 5.6）</strong>: request ハンドラ内からメイン出力サーフェス（Display 2+）へ
    /// GUI / テキスト / デバッグオーバーレイを描画してはならない。
    /// </para>
    /// </remarks>
    public readonly record struct RequestCommand<TRequest>
    {
        /// <summary>受信トピック。既定値時は <c>null</c>。</summary>
        public string? Topic { get; init; }

        /// <summary>送信側が付与する相関 ID。response への一意紐付けに使用される。既定値時は <c>null</c>。</summary>
        public string? CorrelationId { get; init; }

        /// <summary>リクエストペイロード本体。<typeparamref name="TRequest"/> の既定値で初期化される。</summary>
        public TRequest? Payload { get; init; }

        /// <summary>受信時刻（<c>DateTime.UtcNow.Ticks</c> 相当、診断用途）。</summary>
        public long ReceivedAtTicks { get; init; }
    }
}
