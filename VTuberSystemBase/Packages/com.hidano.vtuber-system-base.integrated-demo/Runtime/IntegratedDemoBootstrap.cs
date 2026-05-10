#nullable enable
using System;
using System.Collections;
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.CoreIpc.Core;
using VTuberSystemBase.OutputRendererShell.Scene;
using VTuberSystemBase.RacMainOutputAdapter.Bootstrapper;
using VTuberSystemBase.CameraSwitcherOutputAdapter.Runtime;
using VTuberSystemBase.StageLightingVolumeOutputAdapter.Bootstrap;

namespace VTuberSystemBase.IntegratedDemo
{
    /// <summary>
    /// MainDemo シーン相当の Wave 3d 統合 Bootstrap MonoBehaviour。
    /// シーンに 1 つだけ配置すると <see cref="Awake"/> で全コンポーネントを構築する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>結線対象</b>:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="OutputSceneBootstrapper"/>（Display 2+ メイン出力）</item>
    ///   <item><see cref="CoreIpcBusProvider"/>（同一プロセスループバック ICoreIpcBus を 3 アダプタに供給）</item>
    ///   <item><see cref="RacMainOutputAdapterHost"/>（character-selection-tab IPC → RAC）</item>
    ///   <item><see cref="StageLightingVolumeOutputAdapterBootstrapper"/>（stage-lighting-volume-tab IPC → URP Light/Volume/Stage）</item>
    ///   <item><see cref="CameraSwitcherOutputAdapterBootstrapper"/>（camera-switcher-tab OSC → URP Camera）</item>
    ///   <item><see cref="IntegratedDemoUiShellHost"/>（UiShellLifecycleDriver 経由で UI shell を起動し、3 タブを mount）</item>
    /// </list>
    /// <para>
    /// <b>ライフサイクル順序</b>:
    /// </para>
    /// <list type="number">
    ///   <item><c>RuntimeBootstrap</c>（core-ipc-foundation）が <c>BeforeSceneLoad</c> で <see cref="CoreIpcRuntime.Current"/> を起動済み。</item>
    ///   <item><see cref="Awake"/>: <see cref="CoreIpcBusProvider"/> + <see cref="OutputSceneBootstrapper"/> + 3 アダプタ Bootstrapper（停止状態）を生成。</item>
    ///   <item><see cref="Start"/>: アダプタを順次起動（OutputSceneBootstrapper の Start 完了を待ってから）。UI shell 起動は <see cref="IntegratedDemoUiShellHost.Configure"/> で driver に登録済み。</item>
    /// </list>
    /// <para>
    /// <b>失敗フェイルオーバー</b>:
    /// 個々のアダプタ初期化が失敗してもシーン全体は描画継続を最優先する。
    /// 例外は Console にログ出力され、他アダプタの起動は阻害しない。
    /// SkinProfile が空のときは UI 側の起動を skip する（メイン出力のみ立ち上がる）。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class IntegratedDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private IntegratedDemoConfig _config = new IntegratedDemoConfig();
        [SerializeField, Tooltip("Inspector で割り当てた既存の OutputSceneBootstrapper（同一 GameObject 推奨）。null のとき子 GameObject に動的に追加する。")]
        private OutputSceneBootstrapper? _outputSceneBootstrapper;

        private CoreIpcBusProvider? _busProvider;
        private RacMainOutputAdapterHost? _racHost;
        private StageLightingVolumeOutputAdapterBootstrapper? _stageHost;
        private CameraSwitcherOutputAdapterBootstrapper? _cameraHost;
        private bool _initialized;

        public IntegratedDemoConfig Config => _config;
        public OutputSceneBootstrapper? OutputScene => _outputSceneBootstrapper;
        public CoreIpcBusProvider? BusProvider => _busProvider;
        public RacMainOutputAdapterHost? RacHost => _racHost;
        public StageLightingVolumeOutputAdapterBootstrapper? StageHost => _stageHost;
        public CameraSwitcherOutputAdapterBootstrapper? CameraHost => _cameraHost;

        private void Awake()
        {
            if (!Application.isPlaying) return;
            if (_initialized) return;
            _initialized = true;

            try
            {
                EnsureBusProvider();
                EnsureOutputSceneBootstrapper();
                EnsureMainOutputAdapters();
                EnsureUiShell();
                Debug.Log("[IntegratedDemoBootstrap] Awake wiring complete (PlayMode integration scaffold ready).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IntegratedDemoBootstrap] Awake threw: {ex}");
            }
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            // OutputSceneBootstrapper の Start で Dispatcher / Roots が初期化されるため、
            // 1 フレーム遅らせてアダプタ Bootstrapper を起動する。
            StartCoroutine(StartAdaptersAfterOutputReady());
        }

        private IEnumerator StartAdaptersAfterOutputReady()
        {
            // Wait until OutputSceneBootstrapper reports Complete (or maxFrames timeout).
            int maxFrames = Mathf.Max(1, _config.AdapterStartupMaxFrames);
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (_outputSceneBootstrapper != null
                    && _outputSceneBootstrapper.Diagnostics != null
                    && _outputSceneBootstrapper.Diagnostics.CurrentPhase ==
                        VTuberSystemBase.OutputRendererShell.Abstractions.OutputSceneInitPhase.Complete)
                {
                    break;
                }
                yield return null;
            }

            // RAC Host が同期的に Start で起動するので、自前 Coroutine からは TryStart 系のあるアダプタのみ起動する。
            // Stage adapter は MonoBehaviour.Start で auto-start するが、Dispatcher 未初期化時は no-op で抜ける作りなので
            // ここで明示的に再起動して "complete" 状態の Dispatcher / Roots に対する解決を確実にする。
            if (_stageHost != null)
            {
                try { _stageHost.TryStart(); }
                catch (Exception ex)
                {
                    Debug.LogError($"[IntegratedDemoBootstrap] StageLightingVolume adapter TryStart threw: {ex}");
                }
            }

            // Camera adapter は OutputSceneBootstrapper.Start (= Dispatcher 作成) より後に
            // 生成しないと Awake → TryStart が deferred になる。ここで初めて GameObject を作る。
            EnsureCameraAdapterAfterOutputReady();

            // UI shell 起動完了後にタブ Bootstrapper を構築する。
            // UiShellLifecycleDriver.StartShell は EnsureUiShell() で呼んでいるため、ここでは状態確認のみ。
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (VTuberSystemBase.UiToolkitShell.Bootstrap.UiShellLifecycleDriver.IsRunning)
                {
                    break;
                }
                yield return null;
            }
            if (VTuberSystemBase.UiToolkitShell.Bootstrap.UiShellLifecycleDriver.IsRunning)
            {
                try { IntegratedDemoUiShellHost.LaunchTabBootstrappers(); }
                catch (Exception ex)
                {
                    Debug.LogError($"[IntegratedDemoBootstrap] LaunchTabBootstrappers threw: {ex}");
                }
            }
            else if (_config.SkinProfile != null)
            {
                Debug.LogWarning(
                    "[IntegratedDemoBootstrap] UI shell did not become running within "
                    + $"{maxFrames} frames; tab Bootstrappers were not launched.");
            }
        }

        private void OnDestroy()
        {
            // 各 Host MonoBehaviour は OnDestroy で自分の Bootstrapper を Shutdown するので、
            // ここでは GameObject の破棄に任せる。CoreIpcBus 自体は core-ipc-foundation の
            // RuntimeBootstrap が Application.quitting で dispose するので本クラスでは触らない。
        }

        // ---- private wiring ------------------------------------------------

        private void EnsureBusProvider()
        {
            _busProvider = GetComponent<CoreIpcBusProvider>()
                ?? gameObject.AddComponent<CoreIpcBusProvider>();
        }

        private void EnsureOutputSceneBootstrapper()
        {
            if (_outputSceneBootstrapper == null)
            {
                // 1) 同 GameObject (Inspector でドロップしたケース or テストハーネス) を最優先で再利用。
                _outputSceneBootstrapper = GetComponent<OutputSceneBootstrapper>();
            }
            if (_outputSceneBootstrapper == null)
            {
                // 2) シーン内に既存の OutputSceneBootstrapper があれば共有する。
#if UNITY_2022_2_OR_NEWER
                _outputSceneBootstrapper = UnityEngine.Object.FindAnyObjectByType<OutputSceneBootstrapper>();
#else
                _outputSceneBootstrapper = UnityEngine.Object.FindObjectOfType<OutputSceneBootstrapper>();
#endif
            }
            if (_outputSceneBootstrapper == null)
            {
                // 3) 無ければ同 GameObject に AddComponent する (README で「同一 GameObject 推奨」を明記)。
                _outputSceneBootstrapper = gameObject.AddComponent<OutputSceneBootstrapper>();
            }

            // Inject the IPC bus into the OutputSceneBootstrapper before its Awake runs.
            // 既に Awake が走っているケースは「同 GameObject 配置 → 同フレーム Awake 順」に依存する。
            // README で AddComponent 順を明示している前提で OverrideServices を呼ぶが、
            // 既に IPC server started 状態の場合は no-op として安全に抜ける（D-4: トランスポートは上流委譲）。
            try
            {
                var bus = _busProvider?.Bus;
                if (bus != null)
                {
                    _outputSceneBootstrapper.OverrideServices(routing: null, ipcBus: bus);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[IntegratedDemoBootstrap] OverrideServices threw: {ex.Message}");
            }
        }

        private void EnsureMainOutputAdapters()
        {
            // RAC Host - Awake は重複検出のみ、Start で Initialize するので AddComponent 順序は問わない。
            _racHost = GetComponent<RacMainOutputAdapterHost>()
                ?? gameObject.AddComponent<RacMainOutputAdapterHost>();
            // Reflection で OutputSceneBootstrapper / CoreIpcBusProvider を private SerializeField に inject。
            // Inspector 配線を想定したフィールドなので、コードからは reflection で渡す。
            BindBusProviderToRacHostViaReflection(_racHost);

            // Stage adapter Bootstrapper - Awake は no-op、Start で TryStart。
            _stageHost = GetComponent<StageLightingVolumeOutputAdapterBootstrapper>()
                ?? gameObject.AddComponent<StageLightingVolumeOutputAdapterBootstrapper>();

            // Camera adapter は AddComponent 直後に Awake → TryStart が走り、その時点で
            // Dispatcher / SceneRoots が null だと「deferring」警告になる。
            // ここでは AddComponent せず、StartAdaptersAfterOutputReady() の後段で
            // OutputSceneBootstrapper.Diagnostics == Complete を確認した後に InjectForTesting → AddComponent する。
            _cameraHost = null; // 後段で生成
        }

        private void EnsureCameraAdapterAfterOutputReady()
        {
            if (_cameraHost != null) return;
            try
            {
                if (_outputSceneBootstrapper == null
                    || _outputSceneBootstrapper.Dispatcher == null
                    || _outputSceneBootstrapper.Roots == null)
                {
                    Debug.LogWarning(
                        "[IntegratedDemoBootstrap] Cannot create CameraSwitcherOutputAdapter: "
                        + "OutputSceneBootstrapper subsystems are still null.");
                    return;
                }

                // Camera adapter を inactive な child GameObject で生成し、Inject 完了後に activate
                // する（Awake → TryStart の順序を踏むため）。Bus がまだ揃っていない場合でも
                // child GameObject + AddComponent は実行する：シーン構造は Bus 有無に独立であり、
                // テストハーネスや CoreIpcRuntime 初期化遅延ケースでも GameObject 探索が成立する。
                var camGo = new GameObject("CameraSwitcherOutputAdapterHost");
                camGo.transform.SetParent(transform, worldPositionStays: false);
                camGo.SetActive(false);
                _cameraHost = camGo.AddComponent<CameraSwitcherOutputAdapterBootstrapper>();

                var bus = _busProvider?.Bus;
                if (bus != null)
                {
                    _cameraHost.InjectForTesting(
                        bus,
                        _outputSceneBootstrapper.Dispatcher!,
                        _outputSceneBootstrapper.Roots!);
                    camGo.SetActive(true);
                }
                else
                {
                    // Bus が null のままで activate すると Awake → TryStart →
                    // CamerasListPublisher(bus, ...) で ArgumentNullException が走る。
                    // GameObject だけ残し、Bus が後で揃ったときに activate する。
                    Debug.LogWarning(
                        "[IntegratedDemoBootstrap] CameraSwitcherOutputAdapter GameObject created but ICoreIpcBus is null; "
                        + "leaving the host inactive until the bus becomes available.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IntegratedDemoBootstrap] Camera adapter creation failed: {ex}");
            }
        }

        private void BindBusProviderToRacHostViaReflection(RacMainOutputAdapterHost host)
        {
            try
            {
                // Set _coreIpcBusProviderBehaviour to the BusProvider component on this GameObject.
                var providerField = typeof(RacMainOutputAdapterHost).GetField(
                    "_coreIpcBusProviderBehaviour",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (providerField != null && _busProvider != null)
                {
                    providerField.SetValue(host, _busProvider);
                }

                var sceneField = typeof(RacMainOutputAdapterHost).GetField(
                    "_outputSceneBootstrapper",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (sceneField != null && _outputSceneBootstrapper != null)
                {
                    sceneField.SetValue(host, _outputSceneBootstrapper);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[IntegratedDemoBootstrap] Reflection bind to RacMainOutputAdapterHost failed: {ex.Message}");
            }
        }

        private void EnsureUiShell()
        {
            // SkinProfile が無い場合は UI shell を起動しない（メイン出力のみで起動）。
            if (_config.SkinProfile == null)
            {
                Debug.Log(
                    "[IntegratedDemoBootstrap] SkinProfile not set in IntegratedDemoConfig; " +
                    "skipping UI shell startup. Main-output adapters will still run. " +
                    "Assign a SkinProfile asset in the Inspector to enable Display 1 UI.");
                return;
            }

            ICoreIpcBus? bus = _busProvider?.Bus;
            if (bus == null)
            {
                Debug.LogWarning(
                    "[IntegratedDemoBootstrap] CoreIpcRuntime.Current.Bus is null; " +
                    "skipping UI shell startup until the bus is available.");
                return;
            }

            IntegratedDemoUiShellHost.Configure(_config, bus);
            // UiShellLifecycleDriver は RuntimeInitializeOnLoadMethod(BeforeSceneLoad) で StartShell を一度試行済み。
            // Configure 直前ではダミー (no provider) のため shell は dormant のはず。手動で StartShell を呼び直す。
            VTuberSystemBase.UiToolkitShell.Bootstrap.UiShellLifecycleDriver.StartShell();
        }
    }
}
