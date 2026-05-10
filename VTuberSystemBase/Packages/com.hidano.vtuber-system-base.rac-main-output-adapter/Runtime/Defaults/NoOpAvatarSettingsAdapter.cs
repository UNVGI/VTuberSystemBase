using System.Text.Json;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Defaults
{
    /// <summary>
    /// <see cref="IAvatarSettingsAdapter"/> の既定実装。任意入力に対して
    /// <see cref="AdapterApplyResult.UnknownKey"/> を返し、警告ログ + 無視のフォールバックを成立させる
    /// （Requirement 8.3, 3.4）。
    /// </summary>
    /// <remarks>
    /// 本実装は「利用者プロジェクトが必ず差し替えるべきサイン」として機能する。差し替えなければアバター個別設定は
    /// 一切反映されない（UI には設定スライダー等は表示できるが、Apply 結果は常に UnknownKey 警告のみ）。
    /// </remarks>
    public sealed class NoOpAvatarSettingsAdapter : IAvatarSettingsAdapter
    {
        /// <inheritdoc/>
        public AdapterApplyResult Apply(GameObject avatar, string settingKey, SettingType type, JsonElement value)
        {
            return AdapterApplyResult.UnknownKey;
        }
    }
}
