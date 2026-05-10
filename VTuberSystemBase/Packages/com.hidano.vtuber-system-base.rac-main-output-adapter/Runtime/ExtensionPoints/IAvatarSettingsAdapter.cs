using System.Text.Json;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints
{
    /// <summary>
    /// アバター GameObject に対する個別設定の適用ロジックを利用者プロジェクトに移譲する拡張点
    /// （Requirement 8.3 / RA-6）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定実装（<c>NoOpAvatarSettingsAdapter</c>）は全キー <see cref="AdapterApplyResult.UnknownKey"/> を返す。
    /// 利用者プロジェクトは VRM Humanoid・BlendShape・Animator 等に応じた具体実装を差し込み、
    /// 受信した <see cref="SettingType"/> と <see cref="JsonElement"/> を解釈する。
    /// </para>
    /// <para>
    /// 実装は冪等であること（state 受信は coalesce 対象）。例外をスローした場合、呼出側は <see cref="AdapterApplyResult.Failed"/> 相当として
    /// <c>slot/{id}/error{ApplyFailed}</c> を発行する。
    /// </para>
    /// </remarks>
    public interface IAvatarSettingsAdapter
    {
        /// <summary>
        /// <paramref name="avatar"/> 上で <paramref name="settingKey"/> に応じた処理を実行し、
        /// 結果を <see cref="AdapterApplyResult"/> で返す。
        /// </summary>
        AdapterApplyResult Apply(GameObject avatar, string settingKey, SettingType type, JsonElement value);
    }
}
