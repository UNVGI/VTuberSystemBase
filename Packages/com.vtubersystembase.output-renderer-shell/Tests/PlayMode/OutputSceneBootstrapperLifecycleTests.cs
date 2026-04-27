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
    /// Task 6.1: <see cref="OutputSceneBootstrapper"/> 骨格部分（SerializeField 構成・OverrideServices 注入・
    /// 重複配置検出）の PlayMode 検証。Req 5.2 / 6.1 / 6.5。
    /// 完全な起動シーケンスは Task 6.2 で別途検証する。
    /// </summary>
    [TestFixture]
    public class OutputSceneBootstrapperLifecycleTests
    {
        private readonly List<GameObject> _trackedGameObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _trackedGameObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _trackedGameObjects.Clear();
            DestroyExistingRoots();
        }

        [UnityTest]
        [Description("単一の OutputSceneBootstrapper はそのまま生き残ること（重複検出ヒットしない）")]
        public IEnumerator SingleBootstrapper_Survives()
        {
            DestroyExistingRoots();

            var go = new GameObject("Bootstrapper#1");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            go.SetActive(true);

            yield return null;

            Assert.IsFalse(boot.IsSelfDestroyed, "単一 Bootstrapper は自己破棄されないこと");
            Assert.IsTrue(boot != null, "Component が破棄されていないこと");
        }

        [UnityTest]
        [Description("2 つ目の OutputSceneBootstrapper を追加すると、後発のみが警告ログ付きで自己破棄されること（Req 6.1）")]
        public IEnumerator DuplicateBootstrapper_SecondInstanceSelfDestroys()
        {
            DestroyExistingRoots();

            var go1 = new GameObject("Bootstrapper#1");
            _trackedGameObjects.Add(go1);
            go1.SetActive(false);
            var boot1 = go1.AddComponent<OutputSceneBootstrapper>();
            go1.SetActive(true);
            yield return null;

            Assert.IsFalse(boot1.IsSelfDestroyed, "1 つ目は活動継続");

            var go2 = new GameObject("Bootstrapper#2");
            _trackedGameObjects.Add(go2);
            go2.SetActive(false);
            var boot2 = go2.AddComponent<OutputSceneBootstrapper>();
            // 重複検出時の警告ログを期待
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"duplicate OutputSceneBootstrapper detected"));
            go2.SetActive(true);
            yield return null;

            Assert.IsTrue(boot2 == null || boot2.IsSelfDestroyed, "2 つ目は自己破棄されること");
            Assert.IsFalse(boot1.IsSelfDestroyed, "1 つ目は引き続き活動すること");
        }

        [UnityTest]
        [Description("OverrideServices は Awake 前（GameObject 非アクティブ状態）で呼び出せること")]
        public IEnumerator OverrideServices_BeforeAwake_DoesNotThrow()
        {
            DestroyExistingRoots();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();

            Assert.DoesNotThrow(() => boot.OverrideServices(routing: null, ipcBus: null));

            go.SetActive(true);
            yield return null;

            Assert.IsFalse(boot.IsSelfDestroyed);
        }

        [UnityTest]
        [Description("BuildRoutingConfig は Inspector の SerializeField から DisplayRoutingConfig を構築できること（既定値）")]
        public IEnumerator BuildRoutingConfig_ReturnsDefaultsFromInspectorFields()
        {
            DestroyExistingRoots();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            go.SetActive(true);
            yield return null;

            var config = boot.BuildRoutingConfig();
            Assert.AreEqual(1, config.TargetDisplayIndex,
                "既定 TargetDisplayIndex は 1（Display 2）");
            Assert.AreEqual(FullScreenMode.FullScreenWindow, config.FullScreenMode);
            Assert.IsFalse(config.SuppressEditorWarning);
        }

        [UnityTest]
        [Description("AutoStart 既定値は true（Inspector で OFF にしない限り Start 内で起動シーケンスが回る）")]
        public IEnumerator AutoStart_DefaultIsTrue()
        {
            DestroyExistingRoots();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            go.SetActive(true);
            yield return null;

            Assert.IsTrue(boot.AutoStart);
        }

        [UnityTest]
        [Description("Edit モード相当のコンポーネント生成では Awake が起動シーケンスを走らせないこと（PlayMode のみ活動、Req 6.5）")]
        public IEnumerator Awake_InPlayMode_DoesNotErrorOut()
        {
            DestroyExistingRoots();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            go.SetActive(true);
            yield return null;

            Assert.IsFalse(boot.IsSelfDestroyed);
            // Awake が例外を投げない（Task 6.1 では他フェーズは何もしない）
        }

        private static void DestroyExistingRoots()
        {
#if UNITY_2022_2_OR_NEWER
            var existing = Object.FindObjectsByType<OutputSceneBootstrapper>(FindObjectsSortMode.None);
#else
            var existing = Object.FindObjectsOfType<OutputSceneBootstrapper>();
#endif
            foreach (var b in existing)
            {
                if (b != null) Object.DestroyImmediate(b.gameObject);
            }

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
