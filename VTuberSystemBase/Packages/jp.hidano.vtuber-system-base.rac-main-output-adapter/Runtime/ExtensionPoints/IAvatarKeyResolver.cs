using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RealtimeAvatarController.Core;
using VTuberSystemBase.CharacterSelectionTab.Contracts;

namespace VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints
{
    /// <summary>
    /// <c>AvatarKey</c>（UI が使う一級識別子、CS-4）から RAC の <see cref="AvatarProviderDescriptor"/> へ翻訳し、
    /// 利用可能なアバター一覧を <c>avatars/catalog</c> 用に列挙する拡張点（Requirement 8.1 / RA-3）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 既定実装は Addressables の <c>{avatarKey}</c> を <see cref="UnityEngine.GameObject"/> として解決し、
    /// <c>BuiltinAvatarProviderConfig</c> を動的に組み立てた <see cref="AvatarProviderDescriptor"/> を返す。
    /// 利用者プロジェクトはこのインタフェースを差し替えて Addressables 以外の解決機構（プロジェクト独自カタログ等）を導入できる。
    /// </para>
    /// </remarks>
    public interface IAvatarKeyResolver
    {
        /// <summary>
        /// <paramref name="avatarKey"/> を <see cref="AvatarProviderDescriptor"/> に解決する。
        /// 未解決時は <c>null</c> を返し、呼出側で <c>slot/{id}/error{KeyNotFound}</c> 翻訳に進む。
        /// </summary>
        AvatarProviderDescriptor Resolve(string avatarKey);

        /// <summary>
        /// 現在解決可能な avatar 一覧。<c>avatars/catalog</c> 構築に使う。
        /// </summary>
        IReadOnlyList<AvatarCatalogEntry> AvatarKeys { get; }

        /// <summary>
        /// 利用者プロジェクトが Addressables カタログを更新したタイミング等に呼び出す。
        /// 完了で <see cref="AvatarKeys"/> が最新化され、必要なら <see cref="OnAvatarKeysChanged"/> を発火する。
        /// </summary>
        UniTask Refresh();

        /// <summary><see cref="AvatarKeys"/> に変化があった通知。<c>AvatarCatalogPublisher</c> が購読する。</summary>
        event Action OnAvatarKeysChanged;
    }
}
