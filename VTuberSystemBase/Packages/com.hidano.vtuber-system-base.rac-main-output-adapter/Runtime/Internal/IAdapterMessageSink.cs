using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.RacMainOutputAdapter.Internal
{
    /// <summary>
    /// 本アダプタの Senders（<c>SlotStatusPublisher</c> / <c>SlotErrorTranslator</c> /
    /// <c>SlotCatalogPublisher</c> / <c>AvatarCatalogPublisher</c>）から送信エンベロープを発出する抽象。
    /// </summary>
    /// <remarks>
    /// <para>
    /// design.md は「Senders → IOutputCommandDispatcher.PublishState」と表現していたが、実際の
    /// <see cref="OutputRendererShell.Abstractions.IOutputCommandDispatcher"/> は受信専用 API のみを公開する。
    /// state / event の送信は <see cref="ICoreIpcBus.PublishState{TPayload}"/> /
    /// <see cref="ICoreIpcBus.PublishEvent{TPayload}"/> 経由で行うのが実装上の経路となる。
    /// 本抽象は Tests から InMemoryDispatcher の <c>RecordSent</c> へ集約しつつ、本番では <c>CoreIpcBusMessageSink</c>
    /// が <see cref="ICoreIpcBus"/> をラップする中継ポイントとして機能する。
    /// </para>
    /// <para>
    /// Tests / 本番ともにメインスレッドで呼ばれる前提（D-3 継承）。実装は副作用のみで戻り値を持たない。
    /// </para>
    /// </remarks>
    public interface IAdapterMessageSink
    {
        /// <summary>state エンベロープを送信する。</summary>
        void PublishState<TPayload>(string topic, TPayload payload);

        /// <summary>event エンベロープを送信する。</summary>
        void PublishEvent<TPayload>(string topic, TPayload payload);
    }
}
