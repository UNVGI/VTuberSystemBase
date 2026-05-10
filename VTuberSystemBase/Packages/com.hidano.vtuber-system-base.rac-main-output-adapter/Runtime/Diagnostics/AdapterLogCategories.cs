namespace VTuberSystemBase.RacMainOutputAdapter.Diagnostics
{
    /// <summary>
    /// 本アダプタが <see cref="IDiagnosticsLogger.Log"/> 呼出時に渡すカテゴリ文字列定数群（Requirement 10.1〜10.6）。
    /// </summary>
    public static class AdapterLogCategories
    {
        /// <summary>初期化・解放・依存配線。</summary>
        public const string Bootstrap = "Bootstrap";

        /// <summary><c>slot/{id}/assignment</c> 受信〜RAC 駆動。</summary>
        public const string Assignment = "Assignment";

        /// <summary><c>slot/{id}/settings/{key}</c> 受信〜<c>IAvatarSettingsAdapter.Apply</c>。</summary>
        public const string Settings = "Settings";

        /// <summary><c>slot/{id}/command</c> 受信〜Reset/Reload/PresetApply。</summary>
        public const string Command = "Command";

        /// <summary><c>avatars/{key}/schema</c> 同期解決と Slow/Fallback/Failed 判定。</summary>
        public const string SchemaProvider = "SchemaProvider";

        /// <summary><c>slots/catalog</c> / <c>avatars/catalog</c> publish。</summary>
        public const string Catalog = "Catalog";

        /// <summary><c>slot/{id}/error</c> 翻訳と publish。</summary>
        public const string Error = "Error";

        /// <summary>PlayMode 開始/停止、購読開始/解除。</summary>
        public const string Lifecycle = "Lifecycle";

        /// <summary>本 spec の汎用 Adapter ログ（<c>IAvatarSettingsAdapter</c> 由来の警告等）。</summary>
        public const string Adapter = "Adapter";
    }
}
