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
    /// Task 2.3: <see cref="DefaultLightFactory"/> が <see cref="IOutputSceneRoots.Lights"/> 配下に
    /// 有効な Directional Light を 1 基生成することを検証する（Req 1.3）。
    /// </summary>
    [TestFixture]
    public class DefaultLightFactoryTests
    {
        private readonly List<GameObject> _trackedRoots = new();

        [SetUp]
        public void SetUp()
        {
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
        [Description("Directional Light が LightsRoot 配下に 1 基生成され、type / enabled が正しく設定される（Req 1.3）")]
        public IEnumerator Create_PlacesDirectionalLightUnderLightsRoot_AndEnabled()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            var light = DefaultLightFactory.Create(roots);
            yield return null;

            Assert.IsNotNull(light, "Create が non-null Light を返すこと");
            Assert.AreSame(roots.Lights, light!.transform.parent,
                "DefaultDirectionalLight は LightsRoot 配下に置かれること");
            Assert.AreEqual(LightType.Directional, light.type,
                "type は Directional であること");
            Assert.IsTrue(light.enabled, "Light は有効化されていること");
        }

        [UnityTest]
        [Description("生成直後の輝度が 0 を超え、真っ黒画面回避という設計目的を満たす（Req 1.3）")]
        public IEnumerator Create_HasPositiveIntensity_AvoidingPitchBlackOutput()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            var light = DefaultLightFactory.Create(roots);
            yield return null;

            Assert.Greater(light.intensity, 0f, "intensity > 0 で真っ黒画面を回避できること");
            Assert.AreNotEqual(default(Color), light.color,
                "color が default(Color) (0,0,0,0) ではないこと");
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
