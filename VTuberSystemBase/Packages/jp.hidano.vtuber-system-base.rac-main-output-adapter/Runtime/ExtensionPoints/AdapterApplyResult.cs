namespace VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints
{
    /// <summary>
    /// <see cref="IAvatarSettingsAdapter.Apply"/> の結果を表す列挙。
    /// 利用者プロジェクトの Adapter 実装は受信した settingKey を解釈し、
    /// 適用結果に応じてこの列挙のいずれかを返す。
    /// </summary>
    public enum AdapterApplyResult
    {
        /// <summary>適用成功。</summary>
        Applied = 0,

        /// <summary>未知の <c>settingKey</c>。警告ログを残し、UI 側にはエラー通知しない（縮退）。</summary>
        UnknownKey = 1,

        /// <summary>値が許容範囲外。警告ログを残し、UI 側にはエラー通知しない。</summary>
        OutOfRange = 2,

        /// <summary>適用処理が失敗（例外相当）。<c>slot/{id}/error{ApplyFailed}</c> を発行する。</summary>
        Failed = 3,
    }
}
