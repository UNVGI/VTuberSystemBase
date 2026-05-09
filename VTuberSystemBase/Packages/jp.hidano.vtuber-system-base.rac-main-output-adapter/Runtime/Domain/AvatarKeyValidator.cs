namespace VTuberSystemBase.RacMainOutputAdapter.Domain
{
    /// <summary>
    /// <c>SlotAssignmentPayload.AvatarKey</c> の文字種を <c>CharacterTopics.Safe</c> 互換ルールで検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 許容文字: ASCII 英数 + <c>-</c> / <c>_</c> / <c>.</c>。
    /// その他は不正値として <c>slot/{id}/error{KeyNotFound}</c> + 破棄する（Requirement 2.9）。
    /// </para>
    /// <para>
    /// 空文字 / null は false を返す。null は呼び出し側で「Slot 解除」として扱われ、本検証の前段でフィルタされる想定。
    /// </para>
    /// </remarks>
    public static class AvatarKeyValidator
    {
        /// <summary>
        /// <paramref name="avatarKey"/> が許容文字種であれば true。null / 空文字 / 不正文字を含む場合は false。
        /// </summary>
        public static bool Validate(string avatarKey)
        {
            if (string.IsNullOrEmpty(avatarKey)) return false;
            for (int i = 0; i < avatarKey.Length; i++)
            {
                if (!IsSafeChar(avatarKey[i])) return false;
            }
            return true;
        }

        private static bool IsSafeChar(char c)
        {
            return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '-' or '_' or '.';
        }
    }
}
