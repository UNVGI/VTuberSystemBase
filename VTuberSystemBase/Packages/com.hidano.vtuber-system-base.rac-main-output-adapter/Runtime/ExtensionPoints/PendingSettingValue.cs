using System.Text.Json;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints
{
    /// <summary>
    /// 非 Active な Slot に届いた <c>slot/{id}/settings/{key}</c> の値を保留するためのバッファエントリ。
    /// Slot が Active に遷移したタイミングで <see cref="IAvatarSettingsAdapter.Apply"/> へ転送される。
    /// </summary>
    /// <remarks>
    /// <para>
    /// バッファのキーは <c>(slotId, avatarKey, settingKey)</c> の三つ組とし、
    /// 同一キーへの連続受信は last-write-wins（最新値のみ保持、Requirement 3.3）。
    /// </para>
    /// <para>
    /// アバター差替時（Requirement 2.4）は旧 <c>(slotId, avatarKey)</c> のバッファ全体が破棄される
    /// （Requirement 3.8）。
    /// </para>
    /// </remarks>
    public readonly record struct PendingSettingValue(
        string SettingKey,
        SettingType Type,
        JsonElement Value,
        long ReceivedAtUnixMs);
}
