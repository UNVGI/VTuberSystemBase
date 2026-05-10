using System;
using System.Text;

namespace VTuberSystemBase.CharacterSelectionTab.Contracts
{
    /// <summary>
    /// Topic string constants and type-safe builder used by the character-selection tab UI
    /// and the main-output-side RAC adapter. Centralizes topic literals to prevent typos
    /// and to apply a single ASCII safety policy on dynamic segments (slotId / avatarKey /
    /// settingKey).
    /// </summary>
    public static class CharacterTopics
    {
        public const string SlotsCatalog = "slots/catalog";
        public const string AvatarsCatalog = "avatars/catalog";

        public static string SlotAssignment(string slotId) => $"slot/{Safe(slotId)}/assignment";
        public static string SlotStatus(string slotId) => $"slot/{Safe(slotId)}/status";
        public static string SlotSettingValue(string slotId, string settingKey)
            => $"slot/{Safe(slotId)}/settings/{Safe(settingKey)}";
        public static string SlotSettingsPrefix(string slotId) => $"slot/{Safe(slotId)}/settings/";
        public static string SlotCommand(string slotId) => $"slot/{Safe(slotId)}/command";
        public static string SlotError(string slotId) => $"slot/{Safe(slotId)}/error";
        public static string AvatarSchema(string avatarKey) => $"avatars/{Safe(avatarKey)}/schema";

        /// <summary>
        /// Percent-encodes any character outside ASCII alphanumerics and '-', '_', '.'.
        /// Idempotent for already-safe strings. Throws on null or empty input.
        /// </summary>
        public static string Safe(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length == 0) throw new ArgumentException("Topic segment must not be empty.", nameof(value));

            StringBuilder? builder = null;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (IsSafeChar(c))
                {
                    builder?.Append(c);
                    continue;
                }
                builder ??= new StringBuilder(value.Length + 8).Append(value, 0, i);
                AppendPercentEncoded(builder, c);
            }
            return builder?.ToString() ?? value;
        }

        private static bool IsSafeChar(char c)
        {
            return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_' or '.';
        }

        private static void AppendPercentEncoded(StringBuilder builder, char c)
        {
            var bytes = Encoding.UTF8.GetBytes(new[] { c });
            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append('%');
                builder.Append(HexUpper(bytes[i] >> 4));
                builder.Append(HexUpper(bytes[i] & 0xF));
            }
        }

        private static char HexUpper(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));
    }
}
