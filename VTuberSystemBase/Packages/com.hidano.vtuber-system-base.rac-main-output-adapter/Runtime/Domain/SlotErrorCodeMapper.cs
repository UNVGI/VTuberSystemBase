using System;
using RealtimeAvatarController.Core;

namespace VTuberSystemBase.RacMainOutputAdapter.Domain
{
    /// <summary>
    /// RAC <see cref="SlotErrorCategory"/> を <c>SlotErrorPayload.ErrorCode</c> 文字列に翻訳する純関数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// マッピング（design.md §Data Models / RA-8）:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="SlotErrorCategory.InitFailure"/> + 例外型に <c>Addressable</c> / <c>AvatarKey</c> / <c>KeyNotFound</c> を含む → <see cref="KeyNotFound"/></item>
    ///   <item><see cref="SlotErrorCategory.InitFailure"/> + 例外型に <c>MoCap</c> / <c>Source</c> を含む → <see cref="MotionPipelineInit"/></item>
    ///   <item><see cref="SlotErrorCategory.InitFailure"/> + その他 → <see cref="Unknown"/></item>
    ///   <item><see cref="SlotErrorCategory.ApplyFailure"/> → <see cref="ApplyFailed"/></item>
    ///   <item><see cref="SlotErrorCategory.RegistryConflict"/> → <see cref="Unknown"/>（Detail に "RegistryConflict"）</item>
    ///   <item><see cref="SlotErrorCategory.VmcReceive"/> → <see cref="Unknown"/></item>
    /// </list>
    /// </remarks>
    public static class SlotErrorCodeMapper
    {
        /// <summary>"KeyNotFound" 文字列定数。</summary>
        public const string KeyNotFound = "KeyNotFound";

        /// <summary>"MotionPipelineInit" 文字列定数。</summary>
        public const string MotionPipelineInit = "MotionPipelineInit";

        /// <summary>"ApplyFailed" 文字列定数。</summary>
        public const string ApplyFailed = "ApplyFailed";

        /// <summary>"Unknown" 文字列定数。</summary>
        public const string Unknown = "Unknown";

        /// <summary><see cref="SlotErrorCategory"/> と例外型から ErrorCode 文字列を導出する。</summary>
        public static string Map(SlotErrorCategory category, Exception exception)
        {
            switch (category)
            {
                case SlotErrorCategory.ApplyFailure:
                    return ApplyFailed;

                case SlotErrorCategory.InitFailure:
                    {
                        var hint = ExceptionHint(exception);
                        if (hint == null) return Unknown;
                        if (Contains(hint, "Addressable")
                            || Contains(hint, "AvatarKey")
                            || Contains(hint, "KeyNotFound"))
                        {
                            return KeyNotFound;
                        }
                        if (Contains(hint, "MoCap") || Contains(hint, "Source"))
                        {
                            return MotionPipelineInit;
                        }
                        return Unknown;
                    }

                case SlotErrorCategory.RegistryConflict:
                case SlotErrorCategory.VmcReceive:
                default:
                    return Unknown;
            }
        }

        private static string ExceptionHint(Exception exception)
        {
            if (exception == null) return null;
            // 型名 + メッセージの両方をパターンマッチ対象にする
            var typeName = exception.GetType().FullName ?? exception.GetType().Name;
            return typeName + ":" + (exception.Message ?? string.Empty);
        }

        private static bool Contains(string source, string token)
        {
            return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
