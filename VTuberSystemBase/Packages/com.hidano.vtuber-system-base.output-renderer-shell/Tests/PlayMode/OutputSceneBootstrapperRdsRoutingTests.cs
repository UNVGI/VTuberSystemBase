#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Scene;

namespace VTuberSystemBase.OutputRendererShell.PlayModeTests
{
    /// <summary>
    /// Wave 3e: <see cref="OutputSceneBootstrapper"/> の <see cref="DisplayRoutingProvider"/> 切替動作および
    /// <see cref="DisplayRoutingConfig.SpoutSenderName"/> 反映を PlayMode で検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// テストでは <c>OverrideServices</c> による <see cref="IDisplayRoutingService"/> 注入が
    /// <c>_routingProvider</c> 設定より優先されることを利用し、Inspector フィールドが
    /// <see cref="DisplayRoutingProvider.RuntimeDisplaySelector"/> でも安全にテストダブル経由で起動可能であることを示す。
    /// 加えて、<c>_spoutSenderName</c> Inspector フィールドが <see cref="DisplayRoutingConfig.SpoutSenderName"/> へ
    /// 正しく転送されることを検証する。
    /// </para>
    /// </remarks>
    [TestFixture]
    public class OutputSceneBootstrapperRdsRoutingTests
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
            DestroyExistingScene();
        }

        [UnityTest]
        [Description("RoutingProvider=RuntimeDisplaySelector でも OverrideServices 注入が優先され、テストダブルで Complete に到達する")]
        public IEnumerator RuntimeDisplaySelectorProvider_WithOverride_ReachesComplete()
        {
            DestroyExistingScene();
            var fakeRouting = new RecordingFakeRoutingService();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            SetPrivateField(boot, "_routingProvider", DisplayRoutingProvider.RuntimeDisplaySelector);
            SetPrivateField(boot, "_spoutSenderName", "TestSpoutSender");
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase,
                "Override 注入時は RoutingProvider の値に関係なくテストダブルで起動できる");
            Assert.AreEqual(DisplayRoutingProvider.RuntimeDisplaySelector, boot.RoutingProvider);
        }

        [UnityTest]
        [Description("Inspector で設定した _spoutSenderName が DisplayRoutingConfig.SpoutSenderName として Activate に渡る")]
        public IEnumerator SpoutSenderName_PropagatesToActivateConfig()
        {
            DestroyExistingScene();
            var fakeRouting = new RecordingFakeRoutingService();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            SetPrivateField(boot, "_routingProvider", DisplayRoutingProvider.RuntimeDisplaySelector);
            SetPrivateField(boot, "_spoutSenderName", "VTuberMainOutput");
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Assert.AreEqual(1, fakeRouting.Calls.Count);
            Assert.AreEqual("VTuberMainOutput", fakeRouting.Calls[0].Config.SpoutSenderName,
                "DisplayRoutingConfig 経由で Spout 名が IDisplayRoutingService に伝播すること");
        }

        [UnityTest]
        [Description("_spoutSenderName が空文字列のとき、SpoutSenderName=null として伝播する（物理ディスプレイ経路のみ）")]
        public IEnumerator EmptySpoutSenderName_PropagatesAsNull()
        {
            DestroyExistingScene();
            var fakeRouting = new RecordingFakeRoutingService();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            SetPrivateField(boot, "_routingProvider", DisplayRoutingProvider.RuntimeDisplaySelector);
            SetPrivateField(boot, "_spoutSenderName", string.Empty);
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Assert.AreEqual(1, fakeRouting.Calls.Count);
            Assert.IsNull(fakeRouting.Calls[0].Config.SpoutSenderName,
                "空文字列の Spout 名は null に正規化されること");
        }

        [UnityTest]
        [Description("RoutingProvider 既定値は BuiltIn（後方互換）")]
        public IEnumerator RoutingProviderDefault_IsBuiltIn()
        {
            DestroyExistingScene();
            var fakeRouting = new RecordingFakeRoutingService();

            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            // _routingProvider は明示的に設定せず既定値（BuiltIn）を確認
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Assert.AreEqual(DisplayRoutingProvider.BuiltIn, boot.RoutingProvider,
                "RoutingProvider の既定値は BuiltIn であり、後方互換が保たれること");
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field '{fieldName}' must exist on {target.GetType().Name}");
            field!.SetValue(target, value);
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

        private sealed class RecordingFakeRoutingService : IDisplayRoutingService
        {
            public readonly struct ActivateCall
            {
                public Camera Camera { get; }
                public DisplayRoutingConfig Config { get; }
                public ActivateCall(Camera camera, DisplayRoutingConfig config)
                {
                    Camera = camera;
                    Config = config;
                }
            }

            private readonly List<ActivateCall> _calls = new();
            private DisplayAssignmentInfo _lastAssignment;

            public IReadOnlyList<ActivateCall> Calls => _calls;
            public bool IsFallbackActive => _lastAssignment.IsFallbackActive;

            public DisplayAssignmentInfo Activate(Camera camera, DisplayRoutingConfig config)
            {
                _calls.Add(new ActivateCall(camera, config));
                _lastAssignment = new DisplayAssignmentInfo
                {
                    RequestedDisplayIndex = config.TargetDisplayIndex,
                    EffectiveDisplayIndex = config.TargetDisplayIndex,
                    IsFallbackActive = false,
                    IsEditorLimitedMode = false,
                    DiagnosticMessage = null,
                };
                return _lastAssignment;
            }

            public DisplayAssignmentInfo GetAssignment() => _lastAssignment;

            public void Dispose()
            {
            }
        }
    }
}
</content>
</invoke>
