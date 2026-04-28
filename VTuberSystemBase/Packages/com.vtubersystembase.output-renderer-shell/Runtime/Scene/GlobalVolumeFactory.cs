#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.Scene
{
    /// <summary>
    /// メイン出力シーンに空の Global <see cref="Volume"/> と空の <see cref="VolumeProfile"/> を 1 件生成し、
    /// <see cref="OutputSceneRoots.SetGlobalVolumeProfile"/> 経由でサービスロケータに登録するファクトリ（Req 1.5 / 1.8）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 後続 spec #5（stage-lighting-volume-tab）は <see cref="IOutputSceneRoots.GlobalVolumeProfile"/> を取得し、
    /// <c>profile.Add&lt;T&gt;()</c> で各種 Override（Bloom / Tonemapping 等）を追加する。
    /// 本ファクトリは初期状態で components = empty の VolumeProfile を提供し、Override の差し込みは行わない。
    /// </para>
    /// <para>
    /// 生成される <see cref="VolumeProfile"/> はランタイム ScriptableObject であり、
    /// PlayMode 終了時には <see cref="DestroyVolume"/> 経由で明示的に Destroy する必要がある（Editor のリーク回避）。
    /// </para>
    /// </remarks>
    public static class GlobalVolumeFactory
    {
        /// <summary>
        /// <see cref="IOutputSceneRoots.Volumes"/> 配下に空の Global Volume を 1 件生成する。
        /// </summary>
        /// <returns>生成された <see cref="Volume"/>。</returns>
        public static Volume Create(OutputSceneRoots roots)
        {
            var go = new GameObject("DefaultGlobalVolume");
            go.transform.SetParent(roots.Volumes, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "DefaultGlobalVolumeProfile";
            volume.sharedProfile = profile;

            roots.SetGlobalVolumeProfile(profile);
            return volume;
        }

        /// <summary>
        /// <see cref="Create"/> 由来の Volume / VolumeProfile を一括して破棄するヘルパー。
        /// </summary>
        /// <remarks>
        /// PlayMode 終了時／spec 全体シャットダウン時に呼ぶことで、ランタイム生成 ScriptableObject のリークを防ぐ。
        /// volume が <c>null</c> の場合は何もしない（idempotent）。
        /// </remarks>
        public static void DestroyVolume(Volume? volume)
        {
            if (volume == null) return;
            var profile = volume.sharedProfile;
            volume.sharedProfile = null;
            if (profile != null)
            {
                Object.Destroy(profile);
            }
            Object.Destroy(volume.gameObject);
        }
    }
}
