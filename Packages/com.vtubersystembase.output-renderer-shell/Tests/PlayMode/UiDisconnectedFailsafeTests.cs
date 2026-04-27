#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Scene;
using Object = UnityEngine.Object;

namespace VTuberSystemBase.OutputRendererShell.PlayModeTests
{
    /// <summary>
    /// Task 7.1: UI 未接続／接続断フェイルセーフの検証（Req 7.1〜7.7 / 5.3 / 5.5）。
    /// (a) クライアント未接続のまま <see cref="OutputSceneInitPhase.Complete"/> 到達 /
    /// (b) 未接続状態で複数フレーム描画ループが継続 / (c) 後続接続後にハンドラ通常 invoke /
    /// (d) 例外・クラッシュが発生しない。
    /// </summary>
    [TestFixture]
    public class UiDisconnectedFailsafeTests
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
        [Description("(a) ICoreIpcBus 未注入（クライアント未接続相当）でも Complete 到達 / Camera 描画可能（Req 7.1 / 7.2 / 7.6）")]
        public IEnumerator NoIpcBus_ReachesCompleteAndCameraIsRenderable()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();
            var boot = SpawnBootstrapper(fakeRouting, ipcBus: null);
            yield return null;

            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase,
                "未接続状態でも Complete に到達すること");
            Assert.IsNull(boot.Diagnostics.LastErrorMessage, "未接続のみでは Failed にならない");
            Assert.IsNotNull(boot.Roots!.DefaultCamera);
            Assert.DoesNotThrow(() => boot.Roots.DefaultCamera!.Render(),
                "未接続でもメイン出力カメラの Render は継続できること");
        }

        [UnityTest]
        [Description("(b) 未接続状態で 30 フレーム以上描画ループが継続して問題が発生しないこと（Req 7.2）")]
        public IEnumerator NoIpcBus_RendersForMultipleFrames_NoErrors()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();
            var boot = SpawnBootstrapper(fakeRouting, ipcBus: null);
            yield return null;

            for (int i = 0; i < 30; i++)
            {
                Assert.IsNotNull(boot, $"frame {i}: bootstrapper still alive");
                Assert.IsNotNull(boot.Roots!.DefaultCamera);
                yield return null;
            }

            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase,
                "30 フレーム経過後も Complete のままでフェーズ後退しないこと");
        }

        [UnityTest]
        [Description("(c) Complete 到達後にハンドラ登録すると、その後の受信シミュレーションで通常 invoke されること（Req 7.3）")]
        public IEnumerator AfterComplete_LateHandlerRegistration_IsInvokedOnReceive()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();
            var boot = SpawnBootstrapper(fakeRouting, ipcBus: null);
            yield return null;
            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase);

            int captured = 0;
            using var token = boot.Dispatcher!.RegisterStateHandler<int>("late/topic", cmd => captured = cmd.Payload);

            InvokeOnEnvelopeReceived(boot.Dispatcher!, MessageKind.State, "late/topic", 99);
            yield return null;

            Assert.AreEqual(99, captured,
                "後続接続後にハンドラが正常に invoke されること");
        }

        [UnityTest]
        [Description("(d) 起動シーケンスのいずれの段階でも未接続が原因の Error ログ・例外は発生しないこと（Req 5.5 / 7.6）")]
        public IEnumerator NoIpcBus_DoesNotEmitErrorLogsDuringStartup()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();

            // 重要：未接続起動で Error ログが出たら LogAssert.NoUnexpectedReceived がテストを失敗させる
            var boot = SpawnBootstrapper(fakeRouting, ipcBus: null);
            yield return null;
            yield return null; // 余裕

            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase);
            Assert.IsNull(boot.Diagnostics.LastErrorMessage);
            // Error ログの不在は LogAssert により検証される。
        }

        [UnityTest]
        [Description("UI 未接続中でも Dispatcher は登録／解除を受け付けること（Req 7.4）")]
        public IEnumerator NoIpcBus_DispatcherAcceptsRegisterAndUnregister()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();
            var boot = SpawnBootstrapper(fakeRouting, ipcBus: null);
            yield return null;

            var dispatcher = boot.Dispatcher!;
            var t1 = dispatcher.RegisterStateHandler<string>("topic.a", _ => { });
            var t2 = dispatcher.RegisterEventHandler<string>("topic.a", _ => { });
            Assert.AreEqual(2, dispatcher.RegisteredHandlerCount);

            t1.Dispose();
            Assert.AreEqual(1, dispatcher.RegisteredHandlerCount);
            t2.Dispose();
            Assert.AreEqual(0, dispatcher.RegisteredHandlerCount);
        }

        private OutputSceneBootstrapper SpawnBootstrapper(IDisplayRoutingService routing, ICoreIpcBus? ipcBus)
        {
            var go = new GameObject("Bootstrapper");
            _trackedGameObjects.Add(go);
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            boot.OverrideServices(routing: routing, ipcBus: ipcBus);
            go.SetActive(true);
            return boot;
        }

        private static void InvokeOnEnvelopeReceived(IOutputCommandDispatcher dispatcher, MessageKind kind, string topic, int payload)
        {
            var method = dispatcher.GetType().GetMethod("OnEnvelopeReceived",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method);

            using var doc = System.Text.Json.JsonDocument.Parse(payload.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var envelope = new MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: kind,
                Topic: topic,
                CorrelationId: null,
                TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload: doc.RootElement.Clone());
            method!.Invoke(dispatcher, new object[] { envelope });
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

        private sealed class SimpleFakeRouting : IDisplayRoutingService
        {
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
            public void Dispose() { }
        }
    }
}
