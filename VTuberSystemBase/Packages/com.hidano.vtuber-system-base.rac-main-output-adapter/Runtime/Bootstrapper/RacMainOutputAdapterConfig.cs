namespace VTuberSystemBase.RacMainOutputAdapter.Bootstrapper
{
    /// <summary>
    /// 本アダプタの実行時設定値。<see cref="RacMainOutputAdapterBootstrapper"/> のコンストラクタで受け取る。
    /// 既定値は本 spec の design.md（Performance &amp; Scalability）と整合する保守的な値。
    /// </summary>
    public sealed class RacMainOutputAdapterConfig
    {
        /// <summary>
        /// <see cref="ExtensionPoints.IAvatarSchemaProvider.Resolve"/> の同期実行が
        /// この閾値（ms）を超えた場合に <c>SchemaProvider.Slow</c> 診断ログを残す（Requirement 5.4）。
        /// 上流タイムアウトは 5 秒のため、4 秒（4000ms）を既定値とする。
        /// </summary>
        public int SchemaProviderSlowThresholdMs { get; init; } = 4000;

        /// <summary>
        /// <c>slot/{id}/error</c> の <c>Detail</c> フィールドの最大文字数（Requirement 7.3）。
        /// 既定 512。
        /// </summary>
        public int MaxErrorDetailLength { get; init; } = 512;

        /// <summary>
        /// IPC 受信開始前の publish を保留する <c>PendingPublishQueue</c> の容量。
        /// 既定 16。容量超過時は古い順に破棄し警告ログを残す。
        /// </summary>
        public int PendingPublishQueueCapacity { get; init; } = 16;
    }
}
