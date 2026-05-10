using RealtimeAvatarController.Core;

namespace VTuberSystemBase.RacMainOutputAdapter.Domain
{
    /// <summary>
    /// RAC <see cref="SlotState"/> を <c>SlotStatusPayload.Status</c> 文字列に翻訳する純関数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// マッピング（design.md §Data Models / Domain Model 参照）:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="SlotState.Created"/> → <c>"Empty"</c>（初期化前、UI 上は空 Slot）</item>
    ///   <item><see cref="SlotState.Active"/> + <c>isAssigning=false</c> → <c>"Assigned"</c></item>
    ///   <item><see cref="SlotState.Active"/> + <c>isAssigning=true</c> → <c>"Assigning"</c></item>
    ///   <item><see cref="SlotState.Inactive"/> → <c>"Empty"</c>（将来予約、現状未使用）</item>
    ///   <item><see cref="SlotState.Disposed"/> → <c>"Empty"</c></item>
    /// </list>
    /// <para>
    /// <c>"Error"</c> 状態は本マッパーからは生成されず、<c>SlotErrorTranslator</c> が直接 publish する。
    /// </para>
    /// </remarks>
    public static class SlotStateMapper
    {
        /// <summary>"Empty" 文字列定数。</summary>
        public const string Empty = "Empty";

        /// <summary>"Assigning" 文字列定数。</summary>
        public const string Assigning = "Assigning";

        /// <summary>"Assigned" 文字列定数。</summary>
        public const string Assigned = "Assigned";

        /// <summary>"Error" 文字列定数。</summary>
        public const string Error = "Error";

        /// <summary>RAC <see cref="SlotState"/> を Status 文字列に翻訳する。</summary>
        /// <param name="state">対象 Slot の現在状態。</param>
        /// <param name="isAssigning">
        /// 当該 slot が <c>AddSlotAsync</c>/<c>RemoveSlotAsync</c> 進行中であれば true。
        /// true の場合 <see cref="SlotState.Active"/> でも <see cref="Assigning"/> を返す。
        /// </param>
        public static string Map(SlotState state, bool isAssigning)
        {
            if (isAssigning) return Assigning;
            return state switch
            {
                SlotState.Active => Assigned,
                SlotState.Created => Empty,
                SlotState.Inactive => Empty,
                SlotState.Disposed => Empty,
                _ => Empty,
            };
        }
    }
}
