#nullable enable
using System;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// メイン出力シェルが受信した IPC コマンドを <c>(topic, kind)</c> 別ハンドラへ振り分ける受け口（Req 3.2 / 3.3 / 3.5 / 3.6 / 3.8 / 4.1〜4.9）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 各タブ spec は <see cref="RegisterStateHandler{TPayload}"/> / <see cref="RegisterEventHandler{TPayload}"/> /
    /// <see cref="RegisterRequestHandler{TRequest, TResponse}"/> のいずれかを呼び出してハンドラを登録し、
    /// 戻り値の <see cref="OutputCommandHandlerRegistration"/> を保持する。タブ非アクティブ化時にトークンを
    /// <see cref="IDisposable.Dispose"/> することで登録解除する（Req 3.3 / 4.5）。
    /// </para>
    /// <para>
    /// <strong>kind 別の配信規律（Req 4.1〜4.9）</strong> は上流 <c>core-ipc-foundation</c>（D-7 / D-10）から継承する。
    /// 本ディスパッチャは独自にキューイング・coalesce・FIFO 並べ替え・相関 ID 解決のいずれも実装しない：
    /// state は同一トピックで coalesce（最新値のみ反映、ハンドラは冪等前提）、event は到着順 FIFO、
    /// request/response は <c>correlationId</c> による 1:1 対応。
    /// </para>
    /// <para>
    /// <strong>例外捕捉契約（Req 3.6 / 5.5 / 9.5）</strong>: ハンドラ実行中の例外はディスパッチャ内部で
    /// <c>try/catch</c> され、診断ログに <c>topic / kind / correlationId / 例外</c> が記録される。
    /// ディスパッチャ自身と描画ループは継続する。
    /// </para>
    /// <para>
    /// <strong>kind 二重検証（Req 4.6）</strong>: 登録時点で <c>(topic, kind)</c> をレジストリへ刻み、受信時にも
    /// 受信エンベロープの <c>kind</c> と登録済みエントリの <c>kind</c> を照合する。kind 不一致のコマンドは
    /// 警告ログを残して破棄する。未登録 <c>topic</c> のコマンドも警告ログを残して破棄する（Req 3.5 / 9.4）。
    /// </para>
    /// <para>
    /// <strong>描画禁止契約（Req 5.6）</strong>: 登録するハンドラはメイン出力サーフェス（Display 2+）へ
    /// <c>OnGUI</c> / <c>IMGUI</c> / UI Toolkit（<c>UIDocument</c> / <c>PanelSettings</c>）経由で
    /// GUI / テキスト / デバッグオーバーレイを描画してはならない。診断は UI 側（spec #3）または Unity Console を利用する。
    /// </para>
    /// </remarks>
    public interface IOutputCommandDispatcher : IDisposable
    {
        /// <summary>
        /// state 系ハンドラを登録する。<paramref name="topic"/> へ届く <see cref="MessageKind.State"/> エンベロープを
        /// <see cref="StateCommand{TPayload}"/> へ詰めて <paramref name="handler"/> を呼び出す。
        /// </summary>
        /// <remarks>
        /// 同一 <c>(topic, State)</c> への重複登録は <see cref="InvalidOperationException"/> を送出する Fail-Fast 動作（Req 4.5）。
        /// 異なる <c>kind</c>（Event / Request）であれば同一 <paramref name="topic"/> に対して別ハンドラを登録できる。
        /// state ハンドラは coalesce による中間値スキップを前提に、<em>冪等</em>に実装すること（Req 4.4）。
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="topic"/> が null または空。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> が null。</exception>
        /// <exception cref="InvalidOperationException">同一 <c>(topic, State)</c> が既登録、またはディスパッチャが Dispose 済み。</exception>
        OutputCommandHandlerRegistration RegisterStateHandler<TPayload>(string topic, Action<StateCommand<TPayload>> handler);

        /// <summary>
        /// event 系ハンドラを登録する。<paramref name="topic"/> へ届く <see cref="MessageKind.Event"/> エンベロープを
        /// <see cref="EventCommand{TPayload}"/> へ詰めて <paramref name="handler"/> を呼び出す。
        /// </summary>
        /// <remarks>
        /// event は到着順 FIFO・取りこぼしゼロが要求される（Req 4.3 / 4.9）。複数クライアントから同時に届いた場合も
        /// 上流が FIFO 化したうえでディスパッチャへ届ける（D-7 継承）。
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="topic"/> が null または空。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> が null。</exception>
        /// <exception cref="InvalidOperationException">同一 <c>(topic, Event)</c> が既登録、またはディスパッチャが Dispose 済み。</exception>
        OutputCommandHandlerRegistration RegisterEventHandler<TPayload>(string topic, Action<EventCommand<TPayload>> handler);

        /// <summary>
        /// request 系ハンドラを登録する。<paramref name="topic"/> へ届く <see cref="MessageKind.Request"/> エンベロープを
        /// <see cref="RequestCommand{TRequest}"/> へ詰めて <paramref name="handler"/> を呼び出し、戻り値 <typeparamref name="TResponse"/> を
        /// 同一 <c>correlationId</c> の <see cref="MessageKind.Response"/> エンベロープとして返信する（Req 3.8 / 4.7）。
        /// </summary>
        /// <remarks>
        /// 応答送信は本 spec が直接トランスポートを叩くのではなく、<c>OutputCommandDispatcher</c> のコンストラクタへ注入された
        /// 応答シンク（<c>Action&lt;MessageEnvelope&gt;</c>）経由で行う。応答シンクが未注入の場合は警告ログを残して送信を抑止する。
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="topic"/> が null または空。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> が null。</exception>
        /// <exception cref="InvalidOperationException">同一 <c>(topic, Request)</c> が既登録、またはディスパッチャが Dispose 済み。</exception>
        OutputCommandHandlerRegistration RegisterRequestHandler<TRequest, TResponse>(string topic, Func<RequestCommand<TRequest>, TResponse> handler);

        /// <summary>
        /// 現在登録されているハンドラ件数。診断 API（Req 9.8）から参照される。
        /// </summary>
        int RegisteredHandlerCount { get; }
    }
}
