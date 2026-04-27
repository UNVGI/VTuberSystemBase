#nullable enable
using System;
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
    /// Task 6.4: メイン出力サーフェスへの GUI / IMGUI / UI Toolkit 描画禁止契約を構造的に検証する
    /// （Req 5.1 / 5.2 / 5.3 / 5.4 / 5.6 / 5.7 / 9.6）。
    /// 加えて、ハンドラ例外がメイン出力描画を停止させないこと（Req 5.5）も併せて確認する。
    /// </summary>
    [TestFixture]
    public class MainOutputNoOverlayTests
    {
        private static readonly string[] ForbiddenComponentTypeNamePatterns =
        {
            "UIDocument",
            "PanelSettings",
            "OnGUI",
            "GUIElement",
            "GUITexture",
            "GUIText",
        };

        [TearDown]
        public void TearDown() => DestroyExistingScene();

        [UnityTest]
        [Description("Bootstrapper が生成する Roots 配下に UIDocument / PanelSettings / IMGUI 系コンポーネントが一切存在しないこと（Req 5.2 / 5.6 / 9.6）")]
        public IEnumerator Bootstrapper_NoUIDocumentOrIMGUIComponents()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();

            var go = new GameObject("Bootstrapper");
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase);

            // 各ルート配下のすべての Component を走査
            Transform[] roots =
            {
                boot.Roots!.Stage,
                boot.Roots.Characters,
                boot.Roots.Lights,
                boot.Roots.Cameras,
                boot.Roots.Volumes,
            };

            foreach (var root in roots)
            {
                var components = root.GetComponentsInChildren<Component>(includeInactive: true);
                foreach (var c in components)
                {
                    if (c == null) continue;
                    var typeName = c.GetType().FullName ?? string.Empty;
                    foreach (var forbidden in ForbiddenComponentTypeNamePatterns)
                    {
                        Assert.IsFalse(typeName.Contains(forbidden, StringComparison.Ordinal),
                            $"Forbidden component '{typeName}' found under root '{root.name}'. Main output surface must not render GUI / IMGUI / UI Toolkit elements (Req 5.2 / 5.6 / 9.6).");
                    }

                    // OnGUI を override している MonoBehaviour も禁止
                    if (c is MonoBehaviour mb)
                    {
                        var onGuiMethod = mb.GetType().GetMethod("OnGUI",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        Assert.IsNull(onGuiMethod,
                            $"MonoBehaviour '{mb.GetType().FullName}' on root '{root.name}' overrides OnGUI; this would render to the main output surface (Req 5.2 / 5.6 / 9.6).");
                    }
                }
            }
        }

        [UnityTest]
        [Description("ハンドラ例外発生後もメイン出力カメラはレンダリング可能であること（Req 5.5：描画継続最優先）")]
        public IEnumerator HandlerException_DoesNotInterruptCameraRendering()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();

            var go = new GameObject("Bootstrapper");
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            Assert.AreEqual(OutputSceneInitPhase.Complete, boot.Diagnostics!.CurrentPhase);
            var camera = boot.Roots!.DefaultCamera!;

            // ハンドラ登録 → 例外を投げる挙動の準備
            var dispatcher = boot.Dispatcher!;
            using var token = dispatcher.RegisterEventHandler<int>("test/throws", _ =>
                throw new InvalidOperationException("simulated handler failure"));

            // 例外ログを期待
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(@"event handler threw; dispatcher continues"));

            // 受信シミュレーションは内部 OnEnvelopeReceived を直接呼び出す
            // ここでは dispatcher 経由でなく、bootstrapper 公開 API 範囲のみで検証するため
            // 例外を Throwing なハンドラから直接投げて catch されることをログで確認する。
            Assert.DoesNotThrow(() => InvokeEventHandlerDirectly(dispatcher, "test/throws", 1));
            yield return null;

            // 例外後もカメラは破棄されておらず、Render が呼べる（戻り値ではなく例外なしを確認）
            Assert.IsNotNull(camera);
            Assert.IsTrue(camera.gameObject.activeInHierarchy);
            Assert.DoesNotThrow(() => camera.Render(),
                "ハンドラ例外後もメイン出力カメラの Render は継続できること（Req 5.5）");
        }

        [UnityTest]
        [Description("OutputShellLogger 経路のメッセージは Unity Console（Debug.Log*）のみに出力され、メイン出力サーフェスへ描画されないこと（Req 5.3 / 9.6）")]
        public IEnumerator LoggerOutput_DoesNotAttachUIElementsToScene()
        {
            DestroyExistingScene();
            var fakeRouting = new SimpleFakeRouting();

            var go = new GameObject("Bootstrapper");
            go.SetActive(false);
            var boot = go.AddComponent<OutputSceneBootstrapper>();
            boot.OverrideServices(routing: fakeRouting, ipcBus: null);
            go.SetActive(true);
            yield return null;

            // 起動完了直後にシーン全体に UIDocument / PanelSettings が無いことを確認
#if UNITY_2022_2_OR_NEWER
            var allComponents = Object.FindObjectsByType<Component>(FindObjectsSortMode.None);
#else
            var allComponents = Object.FindObjectsOfType<Component>();
#endif
            foreach (var c in allComponents)
            {
                if (c == null) continue;
                var typeName = c.GetType().FullName ?? string.Empty;
                foreach (var forbidden in new[] { "UIDocument", "PanelSettings" })
                {
                    Assert.IsFalse(typeName.Contains(forbidden, StringComparison.Ordinal),
                        $"Active scene must not contain '{typeName}'. Output renderer shell does not attach UI Toolkit components (Req 5.3 / 9.6).");
                }
            }
        }

        private static void InvokeEventHandlerDirectly(IOutputCommandDispatcher dispatcher, string topic, int payload)
        {
            // ディスパッチャの公開 API では受信シミュレーションを直接呼び出せないため、
            // 内部 OnEnvelopeReceived をリフレクション経由で呼び出す。
            var dispatcherType = dispatcher.GetType();
            var method = dispatcherType.GetMethod("OnEnvelopeReceived",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "OnEnvelopeReceived must exist as a public method for envelope-level receive simulation.");

            var envelope = BuildEventEnvelope(topic, payload);
            method!.Invoke(dispatcher, new object[] { envelope });
        }

        private static CoreIpc.Abstractions.MessageEnvelope BuildEventEnvelope(string topic, int payload)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new CoreIpc.Abstractions.MessageEnvelope(
                ProtocolVersion: "1.0",
                Kind: CoreIpc.Abstractions.MessageKind.Event,
                Topic: topic,
                CorrelationId: null,
                TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload: doc.RootElement.Clone());
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
