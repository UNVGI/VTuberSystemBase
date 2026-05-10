#nullable enable
using System;
using System.Globalization;
using System.Text.Json;
using UnityEngine;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.CharacterSelectionTab.State
{
    /// <summary>
    /// Discriminated-union-style value carried by per-slot per-key settings.
    /// Mirrors <see cref="SettingType"/> for the wire-side discriminator.
    /// Roundtrips through <see cref="ToJson"/> / <see cref="FromJson"/> (task 1.2).
    /// </summary>
    public readonly struct SettingValue : IEquatable<SettingValue>
    {
        private readonly float _f;
        private readonly int _i;
        private readonly bool _b;
        private readonly Color _c;
        private readonly string? _e;
        private readonly Vector3 _v;

        public SettingType Type { get; }

        public float FloatValue => _f;
        public int IntValue => _i;
        public bool BoolValue => _b;
        public Color ColorValue => _c;
        public string? EnumValue => _e;
        public Vector3 Vector3Value => _v;

        private SettingValue(SettingType type, float f, int i, bool b, Color c, string? e, Vector3 v)
        {
            Type = type;
            _f = f;
            _i = i;
            _b = b;
            _c = c;
            _e = e;
            _v = v;
        }

        public static SettingValue Float(float v) => new SettingValue(SettingType.Float, v, 0, false, default, null, default);
        public static SettingValue Int(int v) => new SettingValue(SettingType.Int, 0, v, false, default, null, default);
        public static SettingValue Bool(bool v) => new SettingValue(SettingType.Bool, 0, 0, v, default, null, default);
        public static SettingValue Color(Color v) => new SettingValue(SettingType.Color, 0, 0, false, v, null, default);
        public static SettingValue Enum(string v) => new SettingValue(SettingType.Enum, 0, 0, false, default, v, default);
        public static SettingValue Vector3(Vector3 v) => new SettingValue(SettingType.Vector3, 0, 0, false, default, null, v);

        /// <summary>
        /// Serialise the value to a <see cref="JsonElement"/> shape consumable by
        /// <c>SlotSettingValuePayload.Value</c>. Idempotent with <see cref="FromJson"/>.
        /// </summary>
        public JsonElement ToJson()
        {
            var stream = new System.IO.MemoryStream();
            try
            {
                var writer = new Utf8JsonWriter(stream);
                try
                {
                    WriteJson(writer);
                }
                finally
                {
                    writer.Dispose();
                }
                stream.Position = 0;
                var doc = JsonDocument.Parse(stream);
                try
                {
                    return doc.RootElement.Clone();
                }
                finally
                {
                    doc.Dispose();
                }
            }
            finally
            {
                stream.Dispose();
            }
        }

        private void WriteJson(Utf8JsonWriter writer)
        {
            switch (Type)
            {
                case SettingType.Float:
                    writer.WriteNumberValue(_f);
                    break;
                case SettingType.Int:
                    writer.WriteNumberValue(_i);
                    break;
                case SettingType.Bool:
                    writer.WriteBooleanValue(_b);
                    break;
                case SettingType.Color:
                    writer.WriteStartObject();
                    writer.WriteNumber("r", _c.r);
                    writer.WriteNumber("g", _c.g);
                    writer.WriteNumber("b", _c.b);
                    writer.WriteNumber("a", _c.a);
                    writer.WriteEndObject();
                    break;
                case SettingType.Enum:
                    writer.WriteStringValue(_e ?? string.Empty);
                    break;
                case SettingType.Vector3:
                    writer.WriteStartObject();
                    writer.WriteNumber("x", _v.x);
                    writer.WriteNumber("y", _v.y);
                    writer.WriteNumber("z", _v.z);
                    writer.WriteEndObject();
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        /// <summary>
        /// Reconstruct a <see cref="SettingValue"/> from a <see cref="JsonElement"/>
        /// previously produced by <see cref="ToJson"/>. Throws on shape mismatches —
        /// callers are expected to have validated <paramref name="type"/> against the
        /// schema before calling.
        /// </summary>
        public static SettingValue FromJson(SettingType type, JsonElement element)
        {
            switch (type)
            {
                case SettingType.Float:
                    return Float(element.ValueKind == JsonValueKind.Number ? element.GetSingle() : 0f);
                case SettingType.Int:
                    return Int(element.ValueKind == JsonValueKind.Number ? element.GetInt32() : 0);
                case SettingType.Bool:
                    return Bool(element.ValueKind == JsonValueKind.True);
                case SettingType.Color:
                {
                    var r = ReadFloatField(element, "r");
                    var g = ReadFloatField(element, "g");
                    var b = ReadFloatField(element, "b");
                    var a = ReadFloatField(element, "a", 1f);
                    return Color(new Color(r, g, b, a));
                }
                case SettingType.Enum:
                    return Enum(element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty);
                case SettingType.Vector3:
                {
                    var x = ReadFloatField(element, "x");
                    var y = ReadFloatField(element, "y");
                    var z = ReadFloatField(element, "z");
                    return Vector3(new Vector3(x, y, z));
                }
                default:
                    return default;
            }
        }

        private static float ReadFloatField(JsonElement element, string name, float fallback = 0f)
        {
            if (element.ValueKind != JsonValueKind.Object) return fallback;
            return element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
                ? prop.GetSingle()
                : fallback;
        }

        public bool Equals(SettingValue other)
        {
            if (Type != other.Type) return false;
            switch (Type)
            {
                case SettingType.Float:
                    return _f.Equals(other._f);
                case SettingType.Int:
                    return _i == other._i;
                case SettingType.Bool:
                    return _b == other._b;
                case SettingType.Color:
                    return _c == other._c;
                case SettingType.Enum:
                    return string.Equals(_e, other._e, StringComparison.Ordinal);
                case SettingType.Vector3:
                    return _v == other._v;
                default:
                    return true;
            }
        }

        public override bool Equals(object? obj) => obj is SettingValue other && Equals(other);

        public override int GetHashCode() => Type switch
        {
            SettingType.Float => HashCode.Combine((int)Type, _f),
            SettingType.Int => HashCode.Combine((int)Type, _i),
            SettingType.Bool => HashCode.Combine((int)Type, _b),
            SettingType.Color => HashCode.Combine((int)Type, _c),
            SettingType.Enum => HashCode.Combine((int)Type, _e),
            SettingType.Vector3 => HashCode.Combine((int)Type, _v),
            _ => (int)Type,
        };

        public override string ToString() => Type switch
        {
            SettingType.Float => _f.ToString(CultureInfo.InvariantCulture),
            SettingType.Int => _i.ToString(CultureInfo.InvariantCulture),
            SettingType.Bool => _b ? "true" : "false",
            SettingType.Color => $"rgba({_c.r},{_c.g},{_c.b},{_c.a})",
            SettingType.Enum => _e ?? "",
            SettingType.Vector3 => $"({_v.x},{_v.y},{_v.z})",
            _ => "",
        };

        public static bool operator ==(SettingValue a, SettingValue b) => a.Equals(b);
        public static bool operator !=(SettingValue a, SettingValue b) => !a.Equals(b);
    }
}
