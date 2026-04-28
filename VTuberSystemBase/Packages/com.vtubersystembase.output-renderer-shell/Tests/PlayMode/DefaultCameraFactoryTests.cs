#nullable enable
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Diagnostics;
using VTuberSystemBase.OutputRendererShell.Scene;

namespace VTuberSystemBase.OutputRendererShell.PlayModeTests
{
    /// <summary>
    /// Task 2.2: <see cref="DefaultCameraFactory"/> による URP 対応デフォルトカメラ生成と
    /// <see cref="IOutputSceneRoots.DefaultCamera"/> への登録を検証する。
    /// </summary>
    [TestFixture]
    public class DefaultCameraFactoryTests
    {
        private readonly List<GameObject> _trackedRoots = new();
        private OutputShellLogger _logger = null!;

        [SetUp]
        public void SetUp()
        {
            _logger = new OutputShellLogger(LogLevel.Verbose);
            DestroyExistingRootsByName();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _trackedRoots)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            _trackedRoots.Clear();
            DestroyExistingRootsByName();
        }

        [UnityTest]
        [Description("生成されたカメラは CamerasRoot 配下に置かれ、IOutputSceneRoots.DefaultCamera に登録される（Req 1.2）")]
        public IEnumerator Create_PlacesCameraUnderCamerasRoot_AndRegistersToServiceLocator()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            var camera = DefaultCameraFactory.Create(roots, _logger);
            yield return null;

            Assert.IsNotNull(camera, "Create が non-null Camera を返すこと");
            Assert.AreSame(roots.Cameras, camera!.transform.parent,
                "DefaultMainOutputCamera は CamerasRoot 配下に置かれること");
            Assert.AreSame(camera, ((IOutputSceneRoots)roots).DefaultCamera,
                "サービスロケータの DefaultCamera が設定されること");
        }

        [UnityTest]
        [Description("UniversalAdditionalCameraData が付与され、targetDisplay は既定値（0）のまま委譲先 (IDisplayRoutingService) に任される（Req 1.4 / 2.x 境界）")]
        public IEnumerator Create_AttachesUniversalAdditionalCameraData_AndLeavesTargetDisplayUnset()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            var camera = DefaultCameraFactory.Create(roots, _logger);
            yield return null;

            Assert.IsNotNull(camera!.GetComponent<UniversalAdditionalCameraData>(),
                "URP 用 UniversalAdditionalCameraData が付与されていること");
            Assert.AreEqual(0, camera.targetDisplay,
                "targetDisplay は本ファクトリでは触らず IDisplayRoutingService に委譲する契約");
        }

        [UnityTest]
        [Description("OperatorUI レイヤーがプロジェクトに定義されている場合はカリングマスクから除外される（Req 5.1）")]
        public IEnumerator Create_ExcludesOperatorUiLayerFromCullingMask_WhenLayerDefined()
        {
            int operatorUiLayer = LayerMask.NameToLayer(DefaultCameraFactory.OperatorUiLayerName);

            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            var camera = DefaultCameraFactory.Create(roots, _logger);
            yield return null;

            if (operatorUiLayer >= 0)
            {
                int bit = 1 << operatorUiLayer;
                Assert.AreEqual(0, camera.cullingMask & bit,
                    "OperatorUI レイヤーがカリングマスクから除外されていること");
            }
            else
            {
                // レイヤー未定義時は、Verbose ログだけ出してマスク不変。例外なく返ること自体を確認。
                Assert.Pass($"レイヤー '{DefaultCameraFactory.OperatorUiLayerName}' 未定義のため除外検証スキップ。Create は正常終了。");
            }
        }

        [UnityTest]
        [Description("カメラ生成後に Render() を 1 回明示呼び出しても例外にならず、レンダリング経路が成立する（Req 1.4 / 5.1：真っ黒・描画停止にならないこと）")]
        public IEnumerator Create_CameraRender_DoesNotThrow()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            var camera = DefaultCameraFactory.Create(roots, _logger);
            yield return null;

            // Camera.Render は既定 RenderTexture (null = backbuffer) で呼べる。例外が出ないこと、
            // 1 フレーム経過後にも camera.enabled が true のままであることを確認。
            Assert.DoesNotThrow(() => camera.Render(),
                "Camera.Render() が例外を投げないこと（描画経路が成立）");
            Assert.IsTrue(camera.enabled,
                "1 回の Render 後もカメラが有効であること");
            yield return null;
        }

        private void TrackRoots(OutputSceneRoots roots)
        {
            if (roots.Stage != null) _trackedRoots.Add(roots.Stage.gameObject);
            if (roots.Characters != null) _trackedRoots.Add(roots.Characters.gameObject);
            if (roots.Lights != null) _trackedRoots.Add(roots.Lights.gameObject);
            if (roots.Cameras != null) _trackedRoots.Add(roots.Cameras.gameObject);
            if (roots.Volumes != null) _trackedRoots.Add(roots.Volumes.gameObject);
        }

        private static void DestroyExistingRootsByName()
        {
            string[] names =
            {
                OutputSceneRootNames.Stage,
                OutputSceneRootNames.Characters,
                OutputSceneRootNames.Lights,
                OutputSceneRootNames.Cameras,
                OutputSceneRootNames.Volumes,
            };

            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (go != null && go.name == names[i])
                    {
                        Object.DestroyImmediate(go);
                        break;
                    }
                }
            }
        }
    }
}
