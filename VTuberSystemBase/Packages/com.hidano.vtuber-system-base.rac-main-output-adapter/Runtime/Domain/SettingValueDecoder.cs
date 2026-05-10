using System;
using System.Globalization;
using System.Text.Json;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.RacMainOutputAdapter.Domain
{
    /// <summary>
    /// <see cref="JsonElement"/> を <see cref="SettingType"/> に応じた CLR 値型へ復号する純関数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 対応型（Requirement 3.5 / design.md §Data Models）:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="SettingType.Float"/> → <c>float</c></item>
    ///   <item><see cref="SettingType.Int"/> → <c>int</c></item>
    ///   <item><see cref="SettingType.Bool"/> → <c>bool</c></item>
    ///   <item><see cref="SettingType.Color"/> → <see cref="Color"/>（配列 [r,g,b,a] を受け付ける）</item>
    ///   <item><see cref="SettingType.Enum"/> → <c>string</c>（Enum 名）</item>
    ///   <item><see cref="SettingType.Vector3"/> → <see cref="Vector3"/>（配列 [x,y,z] を受け付ける）</item>
    /// </list>
    /// <para>
    /// JSON の値の型が <see cref="SettingType"/> と一致しない場合は <see cref="InvalidOperationException"/> をスローし、
    /// 呼び出し側は <see cref="ExtensionPoints.AdapterApplyResult.OutOfRange"/> 扱いに翻訳する（Requirement 3.6）。
    /// </para>
    /// <para>
    /// 未知の <see cref="SettingType"/> 列挙値（前方互換ケース）に対しては <c>null</c> を返し、呼び出し側で警告ログ + スキップする
    /// （Requirement 3.5）。
    /// </para>
    /// </remarks>
    public static class SettingValueDecoder
    {
        /// <summary>
        /// <paramref name="value"/> を <paramref name="type"/> に応じた CLR 値へ復号する。
        /// </summary>
        /// <returns>
        /// 復号成功時はボクシングされた値（<c>float</c>/<c>int</c>/<c>bool</c>/<see cref="Color"/>/<c>string</c>/<see cref="Vector3"/>）。
        /// 未知 <see cref="SettingType"/> の場合は <c>null</c>。
        /// </returns>
        /// <exception cref="InvalidOperationException">JSON の値の型が <paramref name="type"/> と矛盾する場合。</exception>
        public static object Decode(SettingType type, JsonElement value)
        {
            switch (type)
            {
                case SettingType.Float:
                    if (value.ValueKind != JsonValueKind.Number)
                        throw new InvalidOperationException($"SettingType.Float expected JSON number, got {value.ValueKind}.");
                    return (float)value.GetDouble();

                case SettingType.Int:
                    if (value.ValueKind != JsonValueKind.Number)
                        throw new InvalidOperationException($"SettingType.Int expected JSON number, got {value.ValueKind}.");
                    return value.GetInt32();

                case SettingType.Bool:
                    if (value.ValueKind == JsonValueKind.True) return true;
                    if (value.ValueKind == JsonValueKind.False) return false;
                    throw new InvalidOperationException($"SettingType.Bool expected JSON bool, got {value.ValueKind}.");

                case SettingType.Color:
                    return DecodeColor(value);

                case SettingType.Enum:
                    if (value.ValueKind != JsonValueKind.String)
                        throw new InvalidOperationException($"SettingType.Enum expected JSON string, got {value.ValueKind}.");
                    return value.GetString() ?? string.Empty;

                case SettingType.Vector3:
                    return DecodeVector3(value);

                default:
                    return null;
            }
        }

        private static Color DecodeColor(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"SettingType.Color expected JSON array [r,g,b,a], got {value.ValueKind}.");
            var len = value.GetArrayLength();
            if (len < 3 || len > 4)
                throw new InvalidOperationException($"SettingType.Color expected 3 or 4 elements, got {len}.");
            float r = (float)value[0].GetDouble();
            float g = (float)value[1].GetDouble();
            float b = (float)value[2].GetDouble();
            float a = len == 4 ? (float)value[3].GetDouble() : 1f;
            return new Color(r, g, b, a);
        }

        private static Vector3 DecodeVector3(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"SettingType.Vector3 expected JSON array [x,y,z], got {value.ValueKind}.");
            if (value.GetArrayLength() != 3)
                throw new InvalidOperationException($"SettingType.Vector3 expected 3 elements, got {value.GetArrayLength()}.");
            float x = (float)value[0].GetDouble();
            float y = (float)value[1].GetDouble();
            float z = (float)value[2].GetDouble();
            return new Vector3(x, y, z);
        }

        // CultureInfo.InvariantCulture を引数として受け取らないため、念のため参照を残す。
        private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
    }
}
