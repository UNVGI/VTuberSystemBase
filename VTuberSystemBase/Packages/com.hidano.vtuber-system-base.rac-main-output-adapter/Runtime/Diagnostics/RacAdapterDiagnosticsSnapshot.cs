namespace VTuberSystemBase.RacMainOutputAdapter.Diagnostics
{
    /// <summary>
    /// 本アダプタの現在状況を 1 つのレコードに集約したスナップショット（Requirement 10.7）。
    /// </summary>
    /// <param name="RegisteredHandlerCount">本 spec が <c>IOutputCommandDispatcher</c> へ登録した handler 数（catalog の動的増減反映後）。</param>
    /// <param name="ActiveSlotCount">RAC <c>SlotManager</c> 上で <c>SlotState.Active</c> 状態の Slot 数。</param>
    /// <param name="ErrorSlotCount">最後に Error 状態を publish した Slot 数（モニタ用）。</param>
    /// <param name="LastErrorAtUnixMs">最終エラー発生時刻（UnixMs、未発生時 0）。</param>
    /// <param name="LastErrorMessage">最終エラーのメッセージ（未発生時は空）。</param>
    /// <param name="AvatarCatalogSize">最後に publish した <c>avatars/catalog</c> エントリ数。</param>
    /// <param name="PhaseName">現在のライフサイクルフェーズ（"Idle" / "Initializing" / "Ready" / "ShuttingDown" / "Shutdown"）。</param>
    public readonly record struct RacAdapterDiagnosticsSnapshot(
        int RegisteredHandlerCount,
        int ActiveSlotCount,
        int ErrorSlotCount,
        long LastErrorAtUnixMs,
        string LastErrorMessage,
        int AvatarCatalogSize,
        string PhaseName);
}
