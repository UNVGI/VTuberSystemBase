#nullable enable
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Scene;

namespace VTuberSystemBase.OutputRendererShell.PlayModeTests
{
    /// <summary>
    /// Task 2.4: <see cref="GlobalVolumeFactory"/> が空の Global Volume と空の VolumeProfile を
    /// 生成し、サービスロケータ経由で取得可能であること、PlayMode 終了時にリークしないことを検証する。
    /// </summary>
    [TestFixture]
    public class GlobalVolumeFactoryTests
    {
        private readonly List<GameObject> _trackedRoots = new();
        private Volume? _createdVolume;

        [SetUp]
        public void SetUp()
        {
            DestroyExistingRootsByName();
            _createdVolume = null;
        }

        [TearDown]
        public void TearDown()
        {
            // 個別テストで明示的に DestroyVolume を呼ばないケースに備えた防御的解放。
            GlobalVolumeFactory.DestroyVolume(_createdVolume);
            _createdVolume = null;

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
        [Description("空の Global Volume が VolumeRoot 配下に生成され、isGlobal=true / priority=0 / 空 VolumeProfile を持つ（Req 1.5）")]
        public IEnumerator Create_CreatesEmptyGlobalVolume_WithDefaultsAndProfileExposed()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            _createdVolume = GlobalVolumeFactory.Create(roots);
            yield return null;

            Assert.IsNotNull(_createdVolume, "Volume が生成されること");
            Assert.AreSame(roots.Volumes, _createdVolume!.transform.parent,
                "DefaultGlobalVolume は VolumeRoot 配下に置かれること");
            Assert.IsTrue(_createdVolume.isGlobal, "isGlobal = true");
            Assert.AreEqual(0f, _createdVolume.priority, "priority = 0");
            Assert.IsNotNull(_createdVolume.sharedProfile, "sharedProfile が割り当てられている");
            Assert.AreEqual(0, _createdVolume.sharedProfile.components.Count,
                "生成直後の VolumeProfile.components は空配列であること");

            IOutputSceneRoots iface = roots;
            Assert.AreSame(_createdVolume.sharedProfile, iface.GlobalVolumeProfile,
                "サービスロケータから同じ VolumeProfile が取得できること");
        }

        [UnityTest]
        [Description("後続 spec が profile.Add<T>() で Override を追加可能であること（Req 1.8）")]
        public IEnumerator GlobalVolumeProfile_SupportsAddingComponents_FromDownstreamSpecs()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            _createdVolume = GlobalVolumeFactory.Create(roots);
            yield return null;

            var profile = ((IOutputSceneRoots)roots).GlobalVolumeProfile!;
            // 後続 spec の使い方を再現： profile.Add<T>() でオーバーライドを追加
            var added = profile.Add<UnityEngine.Rendering.VolumeComponent>(overrides: false);
            try
            {
                Assert.IsNotNull(added, "Add<T>() で VolumeComponent を追加できること");
                Assert.AreEqual(1, profile.components.Count,
                    "追加後 components が 1 件になること");
            }
            finally
            {
                if (added != null)
                {
                    profile.Remove<UnityEngine.Rendering.VolumeComponent>();
                }
            }
        }

        [UnityTest]
        [Description("DestroyVolume で Volume と VolumeProfile が両方破棄され、ScriptableObject がリークしないこと")]
        public IEnumerator DestroyVolume_DisposesVolumeAndProfile_NoLeak()
        {
            var roots = new OutputSceneRoots();
            yield return null;
            TrackRoots(roots);

            _createdVolume = GlobalVolumeFactory.Create(roots);
            yield return null;
            var profile = _createdVolume!.sharedProfile;
            var volumeGo = _createdVolume.gameObject;

            GlobalVolumeFactory.DestroyVolume(_createdVolume);
            _createdVolume = null;
            yield return null;

            // Unity の "Object overload of ==" によって Destroy 済みは null 扱いになる。
            Assert.IsTrue(profile == null,
                "VolumeProfile が破棄されていること（== null として観測される）");
            Assert.IsTrue(volumeGo == null,
                "Volume の GameObject が破棄されていること");
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
