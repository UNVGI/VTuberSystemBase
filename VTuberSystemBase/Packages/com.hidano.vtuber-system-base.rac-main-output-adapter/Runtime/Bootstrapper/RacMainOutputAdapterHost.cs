using System;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Scene;
using VTuberSystemBase.RacMainOutputAdapter.Diagnostics;
using VTuberSystemBase.RacMainOutputAdapter.ExtensionPoints;
using VTuberSystemBase.RacMainOutputAdapter.Internal;

namespace VTuberSystemBase.RacMainOutputAdapter.Bootstrapper
{
    /// <summary>
    /// シーンに 1 つ配置する MonoBehaviour ホスト。<see cref="OutputSceneBootstrapper"/> の <c>Start</c> 完了後
    /// （<see cref="DefaultExecutionOrder"/> 100 で保証）に <see cref="RacMainOutputAdapterBootstrapper.Initialize"/> を呼ぶ
    /// （Requirement 1.4 / 9.1〜9.7）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OutputSceneBootstrapper"/> 自体は <c>output-renderer-shell</c> パッケージの責務であり、本 spec は API を利用するのみで
    /// 改修しない。<see cref="OutputSceneBootstrapper.Dispatcher"/> / <see cref="OutputSceneBootstrapper.Roots"/> 経由で参照を取得する。
    /// </para>
    /// <para>
    /// <see cref="ICoreIpcBus"/> 参照は <c>OutputSceneBootstrapper</c> の private SerializeField のためアクセス不可。
    /// 本ホストでは SerializeField <c>_coreIpcBusProvider</c>（Provider パターン）を提供し、利用者プロジェクトが具体実装を Inspector から差し込む。
    /// 単独検証用には <see cref="OverrideMessageSink"/> でコード経由の差替が可能。
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class RacMainOutputAdapterHost : MonoBehaviour
    {
        [Header("Output Renderer Shell")]
        [Tooltip("OutputSceneBootstrapper への参照（同一シーンに配置されたインスタンス）。")]
        [SerializeField]
        private OutputSceneBootstrapper _outputSceneBootstrapper;

        [Header("IPC Bus Provider (optional)")]
        [Tooltip("ICoreIpcBus を提供する MonoBehaviour（任意）。実装は ICoreIpcBusProvider を継承する。空のとき OverrideMessageSink でコード経由差替を行う。")]
        [SerializeField]
        private MonoBehaviour _coreIpcBusProviderBehaviour;

        [Header("MoCap Factory Provider (optional)")]
        [Tooltip("IMoCapSourceConfigFactory を提供する MonoBehaviour（任意）。実装は IMoCapSourceConfigFactoryProvider を継承する。空のとき StubMoCapSourceConfigFactory にフォールバックする。")]
        [SerializeField]
        private MonoBehaviour _mocapFactoryProviderBehaviour;

        [Header("Diagnostics")]
        [Tooltip("初期ログレベル。")]
        [SerializeField]
        private AdapterLogLevel _minLogLevel = AdapterLogLevel.Info;

        private RacMainOutputAdapterBootstrapper _bootstrapper;
        private IAdapterMessageSink _injectedSink;
        private bool _selfDestroyed;

        /// <summary>初期化済みの Bootstrapper への参照（テスト用）。</summary>
        public RacMainOutputAdapterBootstrapper Bootstrapper => _bootstrapper;

        /// <summary>
        /// テスト時に <see cref="IAdapterMessageSink"/> を直接差し替える（<see cref="ICoreIpcBus"/> 接続が無い検証用）。
        /// 本メソッドは <see cref="Awake"/> 前に呼び出すこと。
        /// </summary>
        public void OverrideMessageSink(IAdapterMessageSink sink)
        {
            _injectedSink = sink;
        }

        private void Awake()
        {
            if (!Application.isPlaying) return; // Edit モードでは何もしない（D-9）

            // 重複検出
#if UNITY_2022_2_OR_NEWER
            var existing = UnityEngine.Object.FindObjectsByType<RacMainOutputAdapterHost>(FindObjectsSortMode.None);
#else
            var existing = UnityEngine.Object.FindObjectsOfType<RacMainOutputAdapterHost>();
#endif
            if (existing != null && existing.Length > 1)
            {
                UnityEngine.Debug.LogWarning(
                    $"[RacMainOutputAdapterHost] duplicate instance detected (count={existing.Length}); destroying this one.");
                _selfDestroyed = true;
                UnityEngine.Object.Destroy(this);
                return;
            }
        }

        private void Start()
        {
            if (_selfDestroyed) return;
            if (!Application.isPlaying) return;

            try
            {
                if (_outputSceneBootstrapper == null)
                {
                    _outputSceneBootstrapper = FindAnyObjectByType<OutputSceneBootstrapper>();
                }

                var dispatcher = _outputSceneBootstrapper?.Dispatcher;
                var roots = _outputSceneBootstrapper?.Roots;
                if (dispatcher == null)
                {
                    UnityEngine.Debug.LogWarning(
                        "[RacMainOutputAdapterHost] OutputSceneBootstrapper.Dispatcher is null; aborting Initialize. Make sure OutputSceneBootstrapper is on the same scene and started.");
                    return;
                }

                IAdapterMessageSink sink = _injectedSink;
                if (sink == null)
                {
                    var bus = ResolveBus();
                    if (bus != null) sink = new CoreIpcBusMessageSink(bus);
                }
                if (sink == null)
                {
                    UnityEngine.Debug.LogWarning(
                        "[RacMainOutputAdapterHost] No IAdapterMessageSink available (neither OverrideMessageSink nor ICoreIpcBusProvider). Aborting Initialize.");
                    return;
                }

                var logger = new UnityConsoleDiagnosticsLogger { MinimumLevel = _minLogLevel };
                _bootstrapper = new RacMainOutputAdapterBootstrapper();
                _bootstrapper.OverrideServices(
                    dispatcher: dispatcher,
                    sceneRoots: roots,
                    messageSink: sink,
                    logger: logger);

                // MoCap Factory Provider 解決（任意）。Provider 不在時は Stub フォールバックに任せる。
                if (_mocapFactoryProviderBehaviour is IMoCapSourceConfigFactoryProvider mocapProvider)
                {
                    var mocapFactory = mocapProvider.Factory;
                    if (mocapFactory != null)
                    {
                        _bootstrapper.OverrideServices(mocapFactory: mocapFactory);
                        UnityEngine.Debug.Log(
                            $"[RacMainOutputAdapterHost] MoCap factory provider resolved: {_mocapFactoryProviderBehaviour.GetType().Name}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[RacMainOutputAdapterHost] MoCap factory provider {_mocapFactoryProviderBehaviour.GetType().Name}.Factory returned null; falling back to StubMoCapSourceConfigFactory.");
                    }
                }

                _bootstrapper.Initialize();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[RacMainOutputAdapterHost] Start failed: {ex}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                _bootstrapper?.Shutdown();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[RacMainOutputAdapterHost] Shutdown threw: {ex}");
            }
            finally
            {
                _bootstrapper = null;
            }
        }

        private ICoreIpcBus ResolveBus()
        {
            if (_coreIpcBusProviderBehaviour is ICoreIpcBusProvider provider) return provider.CoreIpcBus;
            return null;
        }
    }

    /// <summary>
    /// <see cref="ICoreIpcBus"/> を <see cref="RacMainOutputAdapterHost"/> に提供するためのアダプタインタフェース。
    /// 利用者プロジェクトの IPC Bus 起動 MonoBehaviour がこれを実装し、Inspector から接続する。
    /// </summary>
    public interface ICoreIpcBusProvider
    {
        /// <summary>初期化済みの <see cref="ICoreIpcBus"/>。未初期化時は null 可（Host 側でアボート）。</summary>
        ICoreIpcBus CoreIpcBus { get; }
    }
}
