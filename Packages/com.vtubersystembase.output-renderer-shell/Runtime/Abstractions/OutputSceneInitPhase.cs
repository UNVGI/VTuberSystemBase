#nullable enable

namespace VTuberSystemBase.OutputRendererShell.Abstractions
{
    /// <summary>
    /// メイン出力シーン初期化の進行フェーズ。診断 API（<c>IOutputDiagnostics</c>）が公開する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 通常は <see cref="Uninitialized"/> から <see cref="Complete"/> へ単調遷移する。
    /// 任意のフェーズで例外が発生した場合は <see cref="Failed"/> へ脱出し、可能な限り後続フェーズを続行する
    /// （描画継続最優先, Req 5.5）。
    /// </para>
    /// </remarks>
    public enum OutputSceneInitPhase
    {
        /// <summary>未初期化（PlayMode 開始直前または OnDestroy 後）。既定値。</summary>
        Uninitialized = 0,

        /// <summary>ルート GameObject 階層生成完了（StageRoot / CharactersRoot / LightsRoot / CamerasRoot / VolumeRoot）。</summary>
        RootsCreated = 1,

        /// <summary>デフォルトカメラ生成完了（URP 設定 / カリングマスク契約適用済み）。</summary>
        CameraReady = 2,

        /// <summary>デフォルト Directional Light 生成完了。</summary>
        LightReady = 3,

        /// <summary>空の Global Volume + 空 VolumeProfile 生成完了。</summary>
        VolumeReady = 4,

        /// <summary>core-ipc-foundation サーバロール起動完了。</summary>
        IpcServerReady = 5,

        /// <summary>OutputCommandDispatcher の IPC 受信バインド完了。</summary>
        DispatcherReady = 6,

        /// <summary>IDisplayRoutingService.Activate 完了（フォールバック含む）。</summary>
        DisplayRouted = 7,

        /// <summary>シーン初期化完了。任意のコマンド受信前に到達済み（Req 1.6）。</summary>
        Complete = 8,

        /// <summary>初期化中に重大エラーが発生した状態。描画は可能な限り継続する。</summary>
        Failed = 99,
    }
}
