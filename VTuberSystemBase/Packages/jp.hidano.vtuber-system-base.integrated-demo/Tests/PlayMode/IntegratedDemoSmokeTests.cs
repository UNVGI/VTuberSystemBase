#nullable enable
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VTuberSystemBase.IntegratedDemo;
using VTuberSystemBase.OutputRendererShell.Scene;

namespace VTuberSystemBase.IntegratedDemo.Tests
{
    /// <summary>
    /// IntegratedDemoBootstrap が Awake で例外を投げず最小限の結線が成立するかを検証する PlayMode スモークテスト。
    /// SkinProfile は assign しないので UI 側は起動しないが、メイン出力側 Bootstrap と 3 アダプタ Host の生成までは確認できる。
    /// </summary>
    public sealed class IntegratedDemoSmokeTests
    {
        private GameObject? _hostGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
            try { IntegratedDemoUiShellHost.Reset(); } catch { /* ignored */ }
        }

        [UnityTest]
        public IEnumerator Awake_DoesNotThrow_WhenSkinProfileIsNull()
        {
            _hostGameObject = new GameObject("IntegratedDemoBootstrapTest");
            // Awake が走る順序を制御するため inactive で生成 → コンポーネント追加後 active に。
            _hostGameObject.SetActive(false);
            var bootstrap = _hostGameObject.AddComponent<IntegratedDemoBootstrap>();
            _hostGameObject.SetActive(true);

            // 1 frame 待ち、Awake / Start が一巡することを確認。
            yield return null;

            Assert.That(bootstrap.OutputScene, Is.Not.Null,
                "IntegratedDemoBootstrap should have created (or found) an OutputSceneBootstrapper.");
            Assert.That(bootstrap.BusProvider, Is.Not.Null,
                "IntegratedDemoBootstrap should have created a CoreIpcBusProvider.");
            // SkinProfile が null なので UI shell は起動しないはず。
            Assert.That(VTuberSystemBase.UiToolkitShell.Bootstrap.UiShellLifecycleDriver.IsRunning, Is.False,
                "UI shell should not start when SkinProfile is null.");
        }

        [UnityTest]
        public IEnumerator AdapterHosts_AreAttached()
        {
            _hostGameObject = new GameObject("IntegratedDemoBootstrapTest");
            _hostGameObject.SetActive(false);
            _hostGameObject.AddComponent<IntegratedDemoBootstrap>();
            _hostGameObject.SetActive(true);

            yield return null;

            // メイン GameObject に乗るコンポーネント。
            Assert.That(_hostGameObject.GetComponent<OutputSceneBootstrapper>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<CoreIpcBusProvider>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<VTuberSystemBase.RacMainOutputAdapter.Bootstrapper.RacMainOutputAdapterHost>(), Is.Not.Null);
            Assert.That(_hostGameObject.GetComponent<VTuberSystemBase.StageLightingVolumeOutputAdapter.Bootstrap.StageLightingVolumeOutputAdapterBootstrapper>(), Is.Not.Null);
            // Camera adapter は OutputSceneBootstrapper.Start の Dispatcher 作成完了後に child GameObject として作られるため、
            // ここでは数フレーム余分に待ってから子 GO の存在のみ確認する。
            for (int i = 0; i < 30; i++) yield return null;
            var camHost = _hostGameObject.GetComponentInChildren<VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime.CameraSwitcherOutputAdapterBootstrapper>(includeInactive: true);
            Assert.That(camHost, Is.Not.Null,
                "Camera adapter Bootstrapper should be attached as a child GameObject after OutputSceneBootstrapper completes initialization.");
        }
    }
}
