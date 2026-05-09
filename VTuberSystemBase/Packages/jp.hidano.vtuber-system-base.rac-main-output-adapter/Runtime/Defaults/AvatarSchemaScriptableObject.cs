using System;
using System.Collections.Generic;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.RacMainOutputAdapter.Defaults
{
    /// <summary>
    /// 既定 <see cref="AddressablesAvatarSchemaProvider"/> が読み込む ScriptableObject 型。
    /// 利用者プロジェクトはアバター毎に <c>{avatarKey}.schema</c> アドレスでこの ScriptableObject を Addressables 登録する。
    /// </summary>
    /// <remarks>
    /// JSON 値（<see cref="JsonElement"/>）を Inspector で扱うのは煩雑なため、本 SO は文字列で値を保持する設計。
    /// <see cref="AddressablesAvatarSchemaProvider"/> がランタイムに <see cref="System.Text.Json.JsonDocument"/> でパースして
    /// <see cref="SettingSchemaEntry"/> に詰める。
    /// </remarks>
    [CreateAssetMenu(menuName = "VTuberSystemBase/Avatar Schema", fileName = "AvatarSchema")]
    public sealed class AvatarSchemaScriptableObject : ScriptableObject
    {
        [Tooltip("各設定項目の宣言。")]
        public List<Entry> entries = new();

        [Serializable]
        public sealed class Entry
        {
            public string key = string.Empty;
            public string label = string.Empty;
            public SettingType type = SettingType.Float;

            [Tooltip("既定値。空文字なら未指定。JSON リテラル形式（例: 1.5 / true / [1,0,0,1] / \"Smile\"）。")]
            public string defaultJson = string.Empty;

            [Tooltip("最小値。空文字なら未指定。JSON リテラル形式。")]
            public string minJson = string.Empty;

            [Tooltip("最大値。空文字なら未指定。JSON リテラル形式。")]
            public string maxJson = string.Empty;

            public string unit = string.Empty;

            [Tooltip("Enum 候補 / 列挙挙動向け。")]
            public List<string> options = new();

            [Tooltip("'command' を指定すると Slot Command イベントとしてレンダリングされる。空文字なら通常 state。")]
            public string kind = string.Empty;

            [Tooltip("スライダーステップ。0 以下なら未指定。")]
            public float step;
        }
    }
}
