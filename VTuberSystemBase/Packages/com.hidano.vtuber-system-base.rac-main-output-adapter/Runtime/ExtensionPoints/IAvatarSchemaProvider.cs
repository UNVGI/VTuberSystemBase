using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints
{
    /// <summary>
    /// <c>avatars/{key}/schema</c> request に対して <see cref="AvatarSettingsSchemaPayload"/> を解決する拡張点
    /// （Requirement 8.2 / RA-7）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>同期実行が前提</strong>。<c>IOutputCommandDispatcher.RegisterRequestHandler</c> は同期戻り値型を要求するため、
    /// 実装は同期 API で 5 秒以内に完了させること。重い実装は事前ロード戦略を README で促す（Requirement 5.4 / RA-10）。
    /// </para>
    /// <para>
    /// 解決失敗時は <c>null</c> を返す（呼出側で空スキーマ <see cref="AvatarSettingsSchemaPayload"/> にフォールバック）。
    /// </para>
    /// </remarks>
    public interface IAvatarSchemaProvider
    {
        /// <summary>
        /// <paramref name="avatarKey"/> に対するスキーマを返す。未解決時は <c>null</c>（空スキーマフォールバックは呼出側）。
        /// </summary>
        AvatarSettingsSchemaPayload Resolve(string avatarKey);
    }
}
