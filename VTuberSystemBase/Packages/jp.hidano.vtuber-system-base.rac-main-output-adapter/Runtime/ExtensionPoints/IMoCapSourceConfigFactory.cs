using RealtimeAvatarController.Core;

namespace VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints
{
    /// <summary>
    /// Slot 単位で RAC <see cref="MoCapSourceDescriptor"/> を構築する拡張点（Requirement 8.4 / RA-4）。
    /// </summary>
    /// <remarks>
    /// 既定実装（<c>StubMoCapSourceConfigFactory</c>）は no-op の Stub Source を返す。
    /// 利用者プロジェクトは VMC 等の具体ソースを差し込む際に本拡張点を実装する。
    /// </remarks>
    public interface IMoCapSourceConfigFactory
    {
        /// <summary><paramref name="slotId"/> 用の <see cref="MoCapSourceDescriptor"/> を構築する。</summary>
        MoCapSourceDescriptor Build(string slotId);
    }
}
