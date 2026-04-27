#nullable enable
using UnityEngine;
using VTuberSystemBase.CoreIpc.Abstractions;
using VTuberSystemBase.OutputRendererShell.Abstractions;
using VTuberSystemBase.OutputRendererShell.Diagnostics;
using VTuberSystemBase.OutputRendererShell.Dispatch;

namespace VTuberSystemBase.OutputRendererShell.Scene
{
    /// <summary>
    /// メイン出力シーンの Composition Root。<see cref="OutputSceneRoots"/> /
    /// <see cref="DefaultCameraFactory"/> / <see cref="DefaultLightFactory"/> / <see cref="GlobalVolumeFactory"/> /
    /// <see cref="IDisplayRoutingService"/> / <see cref="OutputCommandDispatcher"/> / <see cref="OutputDiagnostics"/>
    /// の生成・依存注入・ライフサイクル管理を担う MonoBehaviour（Req 1.6 / 2.2 / 2.5 / 3.1 / 6.1〜6.7）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Task 6.1 の責務（骨格）</strong>: クラス骨格と <c>SerializeField</c> 構成、
    /// テスト時のモック注入ポイント <see cref="OverrideServices"/>、重複配置検出のみを実装する。
    /// 実際の起動シーケンス（Roots → Camera → Light → Volume → IPC → Dispatcher → Display）
    /// は Task 6.2 で <see cref="Awake"/> / <see cref="Start"/> へ追加する。
    /// </para>
    /// <para>
    /// <strong>描画禁止契約（Req 5.2 / 5.6 / 9.6）</strong>: 本コンポーネントおよび配下に <c>OnGUI</c> /
    /// <c>IMGUI</c> / UI Toolkit（<c>UIDocument</c> / <c>PanelSettings</c>）を一切アタッチしない。
    /// 診断は <see cref="OutputShellLogger"/> を経由して Unity Console へ出力するのみ。
    /// </para>
    /// <para>
    /// <strong>ライフサイクル（D-9 継承）</strong>: PlayMode 開始〜停止の間のみ活動する。
    /// ドメインリロードを跨いだ状態維持を試みず、<see cref="OnDestroy"/> 後はあらゆるリソース参照を破棄する
    /// （Task 6.3 で完結）。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class OutputSceneBootstrapper : MonoBehaviour
    {
        private const string ComponentName = "OutputSceneBootstrapper";

        [Header("Display Routing")]
        [Tooltip("メイン出力カメラを割り当てるディスプレイインデックス（0-based、既定 1 = Display 2）。")]
        [SerializeField]
        private int _targetDisplayIndex = 1;

        [Tooltip("全画面表示モード。既定 FullScreenWindow。Editor PlayMode では Game View 制約により無視される。")]
        [SerializeField]
        private FullScreenMode _fullScreenMode = FullScreenMode.FullScreenWindow;

        [Tooltip("Editor PlayMode 固有の Display.Activate 制限警告を抑止する場合は ON。既定 OFF。")]
        [SerializeField]
        private bool _suppressEditorWarning;

        [Header("Lifecycle")]
        [Tooltip("シーン開始時に自動的に起動シーケンスを実行する場合は ON。既定 ON。")]
        [SerializeField]
        private bool _autoStart = true;

        [Header("Diagnostics")]
        [Tooltip("OutputShellLogger の最小ログレベル。既定 Info。")]
        [SerializeField]
        private LogLevel _minLogLevel = LogLevel.Info;

        private IDisplayRoutingService? _injectedRouting;
        private ICoreIpcBus? _injectedIpcBus;
        private OutputShellLogger? _logger;
        private bool _selfDestroyed;

        /// <summary>
        /// <see cref="Awake"/> 前にテスト時の依存注入を行う接合点（モック差し替え用）。
        /// </summary>
        /// <param name="routing">
        /// テスト用 <see cref="IDisplayRoutingService"/>。<c>null</c> の場合は本番デフォルト
        /// （<c>BuiltInDisplayRoutingService</c>、Task 6.2 で配線）が使われる。
        /// </param>
        /// <param name="ipcBus">
        /// テスト用 <see cref="ICoreIpcBus"/>。<c>null</c> の場合は <see cref="Awake"/> 時点では
        /// IPC 接続を行わず、ディスパッチャはハンドラ登録のみ受け付ける（Task 6.2 で本番配線を追加）。
        /// </param>
        /// <remarks>
        /// 本メソッドは GameObject が <see cref="GameObject.activeInHierarchy"/> = false の状態で呼び出し、
        /// その後アクティブ化することで <see cref="Awake"/> へ流すこと。
        /// </remarks>
        public void OverrideServices(IDisplayRoutingService? routing = null, ICoreIpcBus? ipcBus = null)
        {
            _injectedRouting = routing;
            _injectedIpcBus = ipcBus;
        }

        /// <summary>
        /// 本コンポーネントから取得した不変な <see cref="DisplayRoutingConfig"/>。
        /// Inspector フィールドの組合せをコンストラクションのたびに反映する。
        /// </summary>
        public DisplayRoutingConfig BuildRoutingConfig() => new()
        {
            TargetDisplayIndex = _targetDisplayIndex,
            FullScreenMode = _fullScreenMode,
            SuppressEditorWarning = _suppressEditorWarning,
        };

        /// <summary>
        /// Inspector で設定された自動起動フラグ。<c>true</c> の場合、Task 6.2 で実装される
        /// <see cref="Start"/> 内 IPC サーバ起動／ディスパッチャ起動／ディスプレイ切替が実行される。
        /// </summary>
        public bool AutoStart => _autoStart;

        /// <summary>
        /// 重複配置による自己破棄が走った場合に <c>true</c>。テストでの活動継続検証に利用する。
        /// </summary>
        public bool IsSelfDestroyed => _selfDestroyed;

        /// <summary>
        /// テスト／後続 task 用の logger 取得（Task 6.2 以降が依存注入用に利用）。
        /// </summary>
        internal OutputShellLogger Logger => _logger ??= new OutputShellLogger(_minLogLevel);

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                // Edit モードでは何もしない（D-9 / Req 6.5）。
                return;
            }

            if (DetectAndDestroyDuplicate())
            {
                return;
            }

            // Task 6.2 で本格的な初期化フェーズ（Roots / Camera / Light / Volume）をここに追加する。
        }

        private void Start()
        {
            if (_selfDestroyed) return;
            if (!_autoStart) return;
            if (!Application.isPlaying) return;

            // Task 6.2 で IPC サーバ起動 / Dispatcher バインド / Display 切替をここに追加する。
        }

        private void OnDestroy()
        {
            // Task 6.3 で逆順 Shutdown / Dispose をここに追加する。
            _injectedRouting = null;
            _injectedIpcBus = null;
            _logger = null;
        }

        private bool DetectAndDestroyDuplicate()
        {
#if UNITY_2022_2_OR_NEWER
            var existing = UnityEngine.Object.FindObjectsByType<OutputSceneBootstrapper>(FindObjectsSortMode.None);
#else
            var existing = UnityEngine.Object.FindObjectsOfType<OutputSceneBootstrapper>();
#endif
            int liveCount = 0;
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && !existing[i]._selfDestroyed)
                {
                    liveCount++;
                }
            }

            if (liveCount > 1)
            {
                Logger.Warning(
                    $"duplicate OutputSceneBootstrapper detected (live count={liveCount}); destroying this instance.",
                    ComponentName);
                _selfDestroyed = true;
                UnityEngine.Object.Destroy(this);
                return true;
            }

            return false;
        }
    }
}
