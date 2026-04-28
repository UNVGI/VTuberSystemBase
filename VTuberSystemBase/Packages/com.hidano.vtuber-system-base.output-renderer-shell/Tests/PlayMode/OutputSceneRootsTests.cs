#nullable enable
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Scene;

namespace VTuberSystemBase.OutputRendererShell.PlayModeTests
{
    /// <summary>
    /// Task 2.1: <see cref="OutputSceneRoots"/> によるルート GameObject 階層生成と
    /// <see cref="IOutputSceneRoots"/> サービスロケータ契約の PlayMode 検証。
    /// </summary>
    [TestFixture]
    public class OutputSceneRootsTests
    {
        private readonly List<GameObject> _trackedRoots = new();

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
        [Description("Awake 直後（初期化直後）に 5 つのルートがすべて生成され、IOutputSceneRoots プロパティが non-null を返すこと（Req 1.1 / 1.7）")]
        public IEnumerator OutputSceneRoots_Initialize_AllFiveRootsExist()
        {
            DestroyExistingRootsByName();

            IOutputSceneRoots roots = new OutputSceneRoots();
            yield return null; // Awake 直後相当のフレーム待機

            Track(roots);

            Assert.IsNotNull(roots.Stage, "Stage ルートが non-null であること");
            Assert.IsNotNull(roots.Characters, "Characters ルートが non-null であること");
            Assert.IsNotNull(roots.Lights, "Lights ルートが non-null であること");
            Assert.IsNotNull(roots.Cameras, "Cameras ルートが non-null であること");
            Assert.IsNotNull(roots.Volumes, "Volumes ルートが non-null であること");

            Assert.AreEqual(OutputSceneRootNames.Stage, roots.Stage.name);
            Assert.AreEqual(OutputSceneRootNames.Characters, roots.Characters.name);
            Assert.AreEqual(OutputSceneRootNames.Lights, roots.Lights.name);
            Assert.AreEqual(OutputSceneRootNames.Cameras, roots.Cameras.name);
            Assert.AreEqual(OutputSceneRootNames.Volumes, roots.Volumes.name);
        }

        [UnityTest]
        [Description("生成された 5 ルートはすべてアクティブシーン直下の GameObject であること（Req 1.1）")]
        public IEnumerator OutputSceneRoots_Initialize_RootsAreSceneTopLevel()
        {
            DestroyExistingRootsByName();

            var roots = new OutputSceneRoots();
            yield return null;
            Track(roots);

            Assert.IsNull(roots.Stage.parent, "Stage はシーン直下に置かれていること");
            Assert.IsNull(roots.Characters.parent);
            Assert.IsNull(roots.Lights.parent);
            Assert.IsNull(roots.Cameras.parent);
            Assert.IsNull(roots.Volumes.parent);

            var sceneRootNames = new HashSet<string>();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                sceneRootNames.Add(go.name);
            }
            Assert.IsTrue(sceneRootNames.Contains(OutputSceneRootNames.Stage));
            Assert.IsTrue(sceneRootNames.Contains(OutputSceneRootNames.Characters));
            Assert.IsTrue(sceneRootNames.Contains(OutputSceneRootNames.Lights));
            Assert.IsTrue(sceneRootNames.Contains(OutputSceneRootNames.Cameras));
            Assert.IsTrue(sceneRootNames.Contains(OutputSceneRootNames.Volumes));
        }

        [UnityTest]
        [Description("シーンに同名ルートが既に存在する場合、再生成せず再利用すること（冪等性 / Editor プロトタイプ保存対応）")]
        public IEnumerator OutputSceneRoots_Idempotent_ReusesExistingRoots()
        {
            DestroyExistingRootsByName();

            var preExistingStage = new GameObject(OutputSceneRootNames.Stage);
            _trackedRoots.Add(preExistingStage);
            yield return null;

            var roots = new OutputSceneRoots();
            yield return null;
            Track(roots);

            Assert.AreSame(preExistingStage.transform, roots.Stage,
                "既存 StageRoot を再利用しているはず");

            int stageCount = 0;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == OutputSceneRootNames.Stage) stageCount++;
            }
            Assert.AreEqual(1, stageCount, "StageRoot は 1 つだけ存在すること（重複生成しない）");
        }

        [UnityTest]
        [Description("Stage 配下に子 GameObject を追加しても、他 4 ルートは破壊されないこと（Req 1.8）")]
        public IEnumerator OutputSceneRoots_ChildAddedToStage_OtherRootsRemain()
        {
            DestroyExistingRootsByName();

            var roots = new OutputSceneRoots();
            yield return null;
            Track(roots);

            var stageChild = new GameObject("StageChild_Sample");
            stageChild.transform.SetParent(roots.Stage, worldPositionStays: false);
            yield return null;

            Assert.IsNotNull(roots.Characters);
            Assert.IsNotNull(roots.Lights);
            Assert.IsNotNull(roots.Cameras);
            Assert.IsNotNull(roots.Volumes);
            Assert.IsTrue(roots.Stage.childCount >= 1, "Stage 配下に子が追加されている");
            Assert.AreEqual(roots.Stage, stageChild.transform.parent);
        }

        [UnityTest]
        [Description("DefaultCamera / GlobalVolumeProfile は task 2.1 段階では未割り当て (null) で良いこと（後続 task 2.2 / 2.4 で設定）")]
        public IEnumerator OutputSceneRoots_DefaultCameraAndVolumeProfile_NullUntilFactoriesAssign()
        {
            DestroyExistingRootsByName();

            IOutputSceneRoots roots = new OutputSceneRoots();
            yield return null;
            Track(roots);

            Assert.IsNull(roots.DefaultCamera, "task 2.1 単独では DefaultCamera は未設定");
            Assert.IsNull(roots.GlobalVolumeProfile, "task 2.1 単独では GlobalVolumeProfile は未設定");
        }

        private void Track(IOutputSceneRoots roots)
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
