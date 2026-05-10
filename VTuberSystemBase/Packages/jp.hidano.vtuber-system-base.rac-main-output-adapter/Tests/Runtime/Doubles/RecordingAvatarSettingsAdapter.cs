using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;

namespace VTuberSystemBase.RacMainOutputAdapter.Tests.Doubles
{
    /// <summary>
    /// <see cref="IAvatarSettingsAdapter"/> の記録ダブル。<c>Apply</c> 呼出を内部リストに記録し、
    /// 既定で <see cref="AdapterApplyResult.Applied"/> を返す。settingKey 単位で結果を上書き設定可能。
    /// </summary>
    public sealed class RecordingAvatarSettingsAdapter : IAvatarSettingsAdapter
    {
        private readonly List<Call> _calls = new();
        private readonly Dictionary<string, AdapterApplyResult> _resultByKey = new();
        private AdapterApplyResult _defaultResult = AdapterApplyResult.Applied;

        /// <summary>呼出履歴。</summary>
        public IReadOnlyList<Call> Calls => _calls;

        /// <summary>settingKey 単位で返却結果を設定する。</summary>
        public void SetResult(string settingKey, AdapterApplyResult result) => _resultByKey[settingKey] = result;

        /// <summary>既定返却結果を設定する。</summary>
        public void SetDefaultResult(AdapterApplyResult result) => _defaultResult = result;

        /// <summary>履歴をクリアする。</summary>
        public void Clear() => _calls.Clear();

        /// <inheritdoc/>
        public AdapterApplyResult Apply(GameObject avatar, string settingKey, SettingType type, JsonElement value)
        {
            var result = _resultByKey.TryGetValue(settingKey, out var r) ? r : _defaultResult;
            _calls.Add(new Call(avatar, settingKey, type, value, result));
            return result;
        }

        /// <summary>呼出履歴の 1 エントリ。</summary>
        public readonly record struct Call(
            GameObject Avatar,
            string SettingKey,
            SettingType Type,
            JsonElement Value,
            AdapterApplyResult Result);
    }
}
