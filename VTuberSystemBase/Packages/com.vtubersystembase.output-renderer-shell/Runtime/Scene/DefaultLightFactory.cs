#nullable enable
using UnityEngine;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.Scene
{
    /// <summary>
    /// メイン出力シーンに真っ黒画面を回避する既定 Directional Light を 1 基生成するファクトリ（Req 1.3）。
    /// </summary>
    /// <remarks>
    /// 後続 spec #5 (stage-lighting-volume-tab) が Light を追加・上書きする土台として、
    /// シーン直後でも輝度が 0 にならない最低限のライティングを提供する。
    /// </remarks>
    public static class DefaultLightFactory
    {
        /// <summary>
        /// <see cref="IOutputSceneRoots.Lights"/> 配下に Directional Light 1 基を生成する。
        /// </summary>
        /// <returns>生成された <see cref="Light"/>。</returns>
        public static Light Create(OutputSceneRoots roots)
        {
            var go = new GameObject("DefaultDirectionalLight");
            go.transform.SetParent(roots.Lights, worldPositionStays: false);
            // 上方やや前方から見下ろす標準的な「キーライト」相当の角度。
            go.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.Euler(50f, -30f, 0f));
            go.transform.localScale = Vector3.one;

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.0f;
            light.shadows = LightShadows.Soft;
            light.enabled = true;

            return light;
        }
    }
}
