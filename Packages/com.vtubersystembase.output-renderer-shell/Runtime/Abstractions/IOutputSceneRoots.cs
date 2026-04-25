#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// メイン出力シーンのルート GameObject 階層の参照取得 API（Service Locator）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 後続 spec（#4〜#6）は本インタフェース経由で各ルート Transform を取得し、
    /// 配下にキャラクター／ステージ／Light／Camera／Volume Override 等を追加する（Req 1.7 / 1.8）。
    /// </para>
    /// <para>
    /// 各ルート GameObject の命名は <see cref="OutputSceneRootNames"/> に従い、
    /// 後続 spec は本 API もしくは命名定数のいずれを使ってもルート参照を解決可能。
    /// </para>
    /// <para>
    /// 後続 spec はルート Transform 自体の <c>position</c> / <c>rotation</c> / <c>localScale</c>
    /// を変更してはならない（破壊的変更の禁止）。配下への子 GameObject の追加・削除は自由に行ってよい。
    /// </para>
    /// </remarks>
    public interface IOutputSceneRoots
    {
        /// <summary>ステージアセット（Prefab）配置用のルート Transform。</summary>
        Transform Stage { get; }

        /// <summary>キャラクター（アバター）配置用のルート Transform。</summary>
        Transform Characters { get; }

        /// <summary>Light 配置用のルート Transform（デフォルト Directional Light を配下に含む想定）。</summary>
        Transform Lights { get; }

        /// <summary>Camera 配置用のルート Transform（デフォルトカメラを配下に含む想定）。</summary>
        Transform Cameras { get; }

        /// <summary>Global Volume 配置用のルート Transform（空の Global Volume を配下に含む想定）。</summary>
        Transform Volumes { get; }

        /// <summary>
        /// 空の Global Volume が参照する <see cref="VolumeProfile"/>。
        /// </summary>
        /// <remarks>
        /// 後続 spec は <c>AddComponent&lt;T&gt;()</c> 経由で Override を追加する（Req 1.5 / 1.8）。
        /// 本 spec の task 2.4（GlobalVolumeFactory）が生成・割り当て後に non-null となる。
        /// </remarks>
        VolumeProfile? GlobalVolumeProfile { get; }

        /// <summary>
        /// メイン出力カメラ。<c>targetDisplay</c> は <see cref="IDisplayRoutingService"/> により設定される。
        /// </summary>
        /// <remarks>
        /// 本 spec の task 2.2（DefaultCameraFactory）が生成・配置後に non-null となる。
        /// </remarks>
        Camera? DefaultCamera { get; }
    }
}
