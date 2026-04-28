#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// メイン出力シェルが受け取る IPC コマンドの配信規律種別。
    /// </summary>
    /// <remarks>
    /// 上流 <c>core-ipc-foundation</c>（D-10）の送信側分離（State / Event / Request / Response）に対応する。
    /// 受信側ディスパッチャは登録時の <see cref="OutputCommandKind"/> と
    /// 受信エンベロープの <c>kind</c> を二重検証し、不整合のコマンドを破棄する（Req 4.6）。
    /// </remarks>
    public enum OutputCommandKind
    {
        /// <summary>同一トピックで coalesce（最新値のみ反映）が許容される状態系コマンド。</summary>
        State = 0,

        /// <summary>到着順 FIFO・取りこぼしゼロが要求されるイベント系コマンド。</summary>
        Event = 1,

        /// <summary>相関 ID で 1:1 に応答が紐づく要求系コマンド。</summary>
        Request = 2,

        /// <summary>Request に紐づく応答系コマンド。</summary>
        Response = 3,
    }
}
