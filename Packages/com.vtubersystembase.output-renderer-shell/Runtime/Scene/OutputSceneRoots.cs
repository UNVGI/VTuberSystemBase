#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.OutputRendererShell.Scene
{
    /// <summary>
    /// メイン出力シーンのルート GameObject 階層を生成・所有する <see cref="IOutputSceneRoots"/> の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// コンストラクタ内で <see cref="OutputSceneRootNames"/> に従い 5 つのルート GameObject
    /// （StageRoot / CharactersRoot / LightsRoot / CamerasRoot / VolumeRoot）を生成する。
    /// </para>
    /// <para>
    /// シーン直下に同名のルート GameObject が既に存在する場合は新規生成せず再利用する（冪等性）。
    /// これは Editor でメイン出力シーンをプロトタイプとして保存し、再度 PlayMode を起動するシナリオに対応する。
    /// </para>
    /// <para>
    /// <see cref="DefaultCamera"/> および <see cref="GlobalVolumeProfile"/> は本クラス自体では生成せず、
    /// 後続の DefaultCameraFactory（task 2.2）／GlobalVolumeFactory（task 2.4）が
    /// <see cref="SetDefaultCamera"/> / <see cref="SetGlobalVolumeProfile"/> 経由で割り当てる。
    /// </para>
    /// </remarks>
    public sealed class OutputSceneRoots : IOutputSceneRoots
    {
        /// <inheritdoc />
        public Transform Stage { get; }

        /// <inheritdoc />
        public Transform Characters { get; }

        /// <inheritdoc />
        public Transform Lights { get; }

        /// <inheritdoc />
        public Transform Cameras { get; }

        /// <inheritdoc />
        public Transform Volumes { get; }

        /// <inheritdoc />
        public VolumeProfile? GlobalVolumeProfile { get; private set; }

        /// <inheritdoc />
        public Camera? DefaultCamera { get; private set; }

        /// <summary>
        /// アクティブシーン直下に 5 つのルート GameObject を生成または再利用して初期化する。
        /// </summary>
        public OutputSceneRoots()
        {
            Stage = ResolveOrCreateRoot(OutputSceneRootNames.Stage);
            Characters = ResolveOrCreateRoot(OutputSceneRootNames.Characters);
            Lights = ResolveOrCreateRoot(OutputSceneRootNames.Lights);
            Cameras = ResolveOrCreateRoot(OutputSceneRootNames.Cameras);
            Volumes = ResolveOrCreateRoot(OutputSceneRootNames.Volumes);
        }

        /// <summary>
        /// 後続 DefaultCameraFactory（task 2.2）からデフォルトカメラ参照を割り当てる内部 API。
        /// </summary>
        internal void SetDefaultCamera(Camera camera)
        {
            DefaultCamera = camera;
        }

        /// <summary>
        /// 後続 GlobalVolumeFactory（task 2.4）から空の VolumeProfile 参照を割り当てる内部 API。
        /// </summary>
        internal void SetGlobalVolumeProfile(VolumeProfile profile)
        {
            GlobalVolumeProfile = profile;
        }

        private static Transform ResolveOrCreateRoot(string rootName)
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                var roots = activeScene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] != null && roots[i].name == rootName)
                    {
                        return roots[i].transform;
                    }
                }
            }

            var go = new GameObject(rootName);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            return go.transform;
        }
    }
}
