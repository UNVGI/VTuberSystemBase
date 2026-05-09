namespace VTuberSystemBase.RacMainOutputAdapter.Diagnostics
{
    /// <summary>
    /// 本アダプタが提供する診断スナップショット API（Requirement 10.7）。
    /// </summary>
    /// <remarks>
    /// <c>output-renderer-shell</c> の <c>IOutputDiagnostics</c> を補完する形で外部から状態を読み取れるよう公開する。
    /// </remarks>
    public interface IRacMainOutputAdapterDiagnostics
    {
        /// <summary>現在の本アダプタの状態を 1 つのスナップショットに集約して返す。</summary>
        RacAdapterDiagnosticsSnapshot Capture();
    }
}
