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
    /// Task 6.3: <see cref="OutputSceneBootstrapper.OnDestroy"/> による逆順 Shutdown と PlayMode 反復時の
    /// クリーンアップを PlayMode で検証する（Req 6.3 / 6.4 / 6.6）。
    /// PlayMode 開始→停止のフルサイクルはテストハーネス上で完全には再現できないため、
    /// 同一 PlayMode セッション内で生成→破棄を 10 回反復し、リソースが累積しないことを確認する。
    /// </summary>
    [TestFixture]
    public class OutputSceneBootstrapperReinitTests
    {
        [TearDown]
        public void TearDown()
        {
            DestroyExistingScene();
        }

        [UnityTest]
        [Description("生成→破棄を 10 回反復しても、シーン上のルート GameObject が累積しないこと（Req 6.3 / 6.4）")]
        public IEnumerator RepeatedSpawnAndDestroy_DoesNotAccumulateRoots()
        {
            DestroyExistingScene();
            int sceneRootCountBefore = SceneManager.GetActiveScene().GetRootGameObjects().Length;

            for (int i = 0; i < 10; i++)
            {
                var fakeRouting = new ReinitFakeDisplayRoutingService();

                var go = new GameObject($"Bootstrapper#{i}");
                go.SetActive(false);
                var boot = go.AddComponent<OutputSceneBootstrapper>();
                boot.OverrideServices(routing: fakeRouting, ipcBus: null);
                go.SetActive(true);
                yield return null;

                Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase,
                    $"iteration {i}: should reach Complete");

                Object.Destroy(go);
                yield return null;
            }

            // 最終的に独自 root（Stage/Characters/Lights/Cameras/Volume）の累積が無いこと
            int extraStageRoots = 0;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == OutputSceneRootNames.Stage ||
                    root.name == OutputSceneRootNames.Characters ||
                    root.name == OutputSceneRootNames.Lights ||
                    root.name == OutputSceneRootNames.Cameras ||
                    root.name == OutputSceneRootNames.Volumes)
                {
                    extraStageRoots++;
                }
            }
            Assert.AreEqual(0, extraStageRoots, "10 反復後に独自ルートが累積していないこと");
        }

        [UnityTest]
        [Description("OnDestroy 後 Diagnostics は Reset 済み・Roots 参照は解除されていること（次回 PlayMode のクリーン再初期化, Req 6.4）")]
        public IEnumerator AfterDestroy_DiagnosticsResetAndRootsCleared()
        {
            DestroyExistingScene();
            var fakeRouting = new ReinitFakeDisplayRoutingService();

            var go = new GameObject("Bootstrapper");
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase);
            Assert.IsNotNull(boot.Roots);

            Object.DestroyImmediate(go);

            // OnDestroy で _roots / _diagnostics はクリアされる
            Assert.IsNull(boot.Diagnostics, "OnDestroy 後は Diagnostics 参照解除");
            Assert.IsNull(boot.Roots, "OnDestroy 後は Roots 参照解除");
            Assert.IsNull(boot.Dispatcher, "OnDestroy 後は Dispatcher 参照解除");
        }

        [UnityTest]
        [Description("OnDestroy がディスパッチャを Dispose し、登録済みハンドラを解除すること")]
        public IEnumerator OnDestroy_DisposesDispatcherAndClearsHandlers()
        {
            DestroyExistingScene();
            var fakeRouting = new ReinitFakeDisplayRoutingService();

            var go = new GameObject("Bootstrapper");
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            var dispatcher = boot.Dispatcher!;
            using var token = dispatcher.RegisterStateHandler<int>("topic.x", _ => { });
            Assert.AreEqual(1, dispatcher.RegisteredHandlerCount);

            Object.DestroyImmediate(go);

            // dispatcher は Dispose されており、新規登録は InvalidOperationException
            Assert.Throws<System.InvalidOperationException>(() =>
                dispatcher.RegisterStateHandler<int>("topic.y", _ => { }));
        }

        [UnityTest]
        [Description("OnDestroy で IDisplayRoutingService が Dispose されること（_routingOwnedByThis=false で注入された場合は所有者責任で Dispose しない）")]
        public IEnumerator OnDestroy_DoesNotDisposeInjectedRoutingService()
        {
            DestroyExistingScene();
            var fakeRouting = new ReinitFakeDisplayRoutingService();

            var go = new GameObject("Bootstrapper");
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Object.DestroyImmediate(go);

            Assert.IsFalse(fakeRouting.IsDisposed,
                "テスト注入された routing は本 Bootstrapper が所有していないため Dispose しないこと");
        }

        private static void DestroyExistingScene()
        {
#if UNITY_2022_2_OR_NEWER
            foreach (var b in Object.FindObjectsByType<OutputSceneBootstrapper>(FindObjectsSortMode.None))
            {
                if (b != null) Object.DestroyImmediate(b.gameObject);
            }
#else
            foreach (var b in Object.FindObjectsOfType<OutputSceneBootstrapper>())
            {
                if (b != null) Object.DestroyImmediate(b.gameObject);
            }
#endif
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

        private sealed class ReinitFakeDisplayRoutingService : IDisplayRoutingService
        {
            public bool IsDisposed { get; private set; }
            public bool IsFallbackActive => false;
            private DisplayAssignmentInfo _last;

            public DisplayAssignmentInfo Activate(Camera camera, DisplayRoutingConfig config)
            {
                _last = new DisplayAssignmentInfo
                {
                    RequestedDisplayIndex = config.TargetDisplayIndex,
                    EffectiveDisplayIndex = config.TargetDisplayIndex,
                };
                return _last;
            }

            public DisplayAssignmentInfo GetAssignment() => _last;
            public void Dispose() => IsDisposed = true;
        }
    }
}
