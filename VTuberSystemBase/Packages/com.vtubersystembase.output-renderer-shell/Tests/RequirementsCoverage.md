# Requirements Coverage Matrix — output-renderer-shell

本ドキュメントは `.kiro/specs/output-renderer-shell/requirements.md` の各 Acceptance Criteria を、
EditMode／PlayMode テストのいずれかで検証していることを記録するトレーサビリティ表。

各テストメソッドは `[Description("... Req X.Y ...")]` 属性で該当要件を明記している（または
`<summary>` XMLDoc に Req 番号を含める）ため、Test Runner 上のテスト名・説明から逆引き可能。

CI 相当（`unity -batchmode -runTests`）で 2 分以内に全 EditMode / PlayMode テストが成功することを
完了条件とする（Task 8.2）。物理ディスプレイには `FakeDisplayRoutingService`（EditMode）または
PlayMode テスト各ファイルにネストされた fake 実装で代替することで非依存となっている（Req 8.5）。

## Requirement 1: メイン出力シーンの初期構成

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 1.1 ルート GameObject 階層生成 | `OutputSceneRoots_Initialize_AllFiveRootsExist` / `OutputSceneRoots_Initialize_RootsAreSceneTopLevel` | `Tests/PlayMode/OutputSceneRootsTests.cs` |
| 1.2 デフォルトカメラ配置 | `DefaultCameraFactoryTests` 全般 / `AutoStart_RootsCameraLightVolumeReadyBeforeAnyCommand` | `Tests/PlayMode/DefaultCameraFactoryTests.cs` / `OutputSceneBootstrapperFlowTests.cs` |
| 1.3 デフォルトライト配置 | `DefaultLightFactoryTests` 全般 | `Tests/PlayMode/DefaultLightFactoryTests.cs` |
| 1.4 URP Asset 参照 | `DefaultCameraFactoryTests`（UniversalAdditionalCameraData 検証） | `Tests/PlayMode/DefaultCameraFactoryTests.cs` |
| 1.5 空の Global Volume | `GlobalVolumeFactoryTests` 全般 | `Tests/PlayMode/GlobalVolumeFactoryTests.cs` |
| 1.6 任意コマンド受信前に準備完了 | `AutoStart_ReachesCompletePhase` / `AutoStart_RootsCameraLightVolumeReadyBeforeAnyCommand` | `Tests/PlayMode/OutputSceneBootstrapperFlowTests.cs` |
| 1.7 参照取得 API 提供 | `OutputSceneRoots_Initialize_AllFiveRootsExist` | `Tests/PlayMode/OutputSceneRootsTests.cs` |
| 1.8 拡張点としてルート配下配置受入 | `OutputSceneRoots_ChildAddedToStage_OtherRootsRemain` | `Tests/PlayMode/OutputSceneRootsTests.cs` |

## Requirement 2: Display 2+ 全画面表示切替（暫定実装 + 抽象）

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 2.1 IDisplayRoutingService 抽象 + 暫定実装 | `IDisplayRoutingServiceContractTests` / `BuiltInDisplayRoutingServiceTests` | `Tests/EditMode/IDisplayRoutingServiceContractTests.cs` / `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |
| 2.2 Display アクティベート + targetDisplay 割当 | `BuiltInDisplayRoutingServiceTests` の Activate ケース | `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |
| 2.3 全画面表示モード設定 | `BuiltInDisplayRoutingServiceTests` の SetFullScreenMode ケース | `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |
| 2.4 Display 0 フォールバック + 警告 | `BuiltInDisplayRoutingServiceTests` のフォールバックケース | `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |
| 2.4a ディスプレイ割当状態の診断公開 | `OutputDiagnosticsTests.SetDisplayAssignment_*` / `AutoStart_DisplayAssignmentRetrievableFromDiagnostics` | `Tests/EditMode/OutputDiagnosticsTests.cs` / `Tests/PlayMode/OutputSceneBootstrapperFlowTests.cs` |
| 2.5 具体実装直接依存排除（Composition Root 注入） | `OverrideServices_BeforeAwake_DoesNotThrow` / Bootstrapper Flow 系で fake routing 注入 | `Tests/PlayMode/OutputSceneBootstrapperLifecycleTests.cs` ほか |
| 2.6 RDS 差し替え可能構造 | `IDisplayRoutingServiceContractTests` で Fake 実装を契約検証 | `Tests/EditMode/IDisplayRoutingServiceContractTests.cs` |
| 2.7 ディスプレイインデックス外部設定 | `BuiltInDisplayRoutingServiceTests` の境界値ケース（0/1/超過） | `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |
| 2.8 スタンドアロン / Editor 同一抽象 | `BuiltInDisplayRoutingServiceTests` の IsEditor 分岐 | `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |

## Requirement 3: コマンドディスパッチャ

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 3.1 サーバロール起動 | Bootstrapper Flow テスト（IpcServerReady フェーズ通過） | `Tests/PlayMode/OutputSceneBootstrapperFlowTests.cs` |
| 3.2 topic/kind 別振り分け | `RegisterStateHandler_ThenReceive_HandlerIsInvokedWithPayload` / `RegisterEventHandler_*` | `Tests/EditMode/OutputCommandDispatcherTests.cs` |
| 3.3 登録／解除 API | `TokenDispose_RemovesHandler_AndSubsequentReceiveIsDropped` / `HandlerRegistryTests` 全般 | `Tests/EditMode/OutputCommandDispatcherTests.cs` / `HandlerRegistryTests.cs` |
| 3.4 Unity メインスレッド呼び出し | 上流 D-3 継承（`SelfLoopDispatcherTests` で同期到達を観測） | `Tests/PlayMode/SelfLoopDispatcherTests.cs` |
| 3.5 未登録コマンド破棄 + ログ | `OnEnvelopeReceived_UnregisteredTopic_LogsWarningAndDrops` | `Tests/EditMode/OutputCommandDispatcherTests.cs` |
| 3.6 例外捕捉 + 描画継続 | `HandlerThrows_DispatcherCatchesAndContinues` / `HandlerException_DoesNotInterruptCameraRendering` | `Tests/EditMode/OutputCommandDispatcherTests.cs` / `Tests/PlayMode/MainOutputNoOverlayTests.cs` |
| 3.7 asmdef 隔離 | `VTuberSystemBase.OutputRendererShell.Runtime.asmdef` の参照宣言 + 構造で担保 | `Runtime/VTuberSystemBase.OutputRendererShell.Runtime.asmdef` |
| 3.8 request/response ハンドラ登録 | `RegisterRequestHandler_OnReceive_RespondsWithCorrelatedResponse` | `Tests/EditMode/OutputCommandDispatcherTests.cs` |

## Requirement 4: kind 別配信規律

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 4.1 kind フィールド参照 + 配信規律 | `RegisterStateHandler_ThenReceive_*` / `RegisterEventHandler_*` | `Tests/EditMode/OutputCommandDispatcherTests.cs` |
| 4.2 state coalesce 許容（上流 D-7 継承） | `SelfLoop_StateCommand_ReachesDispatcherHandler` | `Tests/PlayMode/SelfLoopDispatcherTests.cs` |
| 4.3 event FIFO 配信 | `SelfLoop_EventCommand_FifoOrderingPreserved` | `Tests/PlayMode/SelfLoopDispatcherTests.cs` |
| 4.4 state 冪等性契約ドキュメント | `StateCommand<T>` の XMLDoc に Req 4.4 記載済 | `Runtime/Abstractions/StateCommand.cs` |
| 4.5 state/event/request 別の登録 API | `IOutputCommandDispatcher` インタフェース / `RegisterStateHandler_DuplicateTopicAndKind_Throws` | `Runtime/Abstractions/IOutputCommandDispatcher.cs` / `Tests/EditMode/OutputCommandDispatcherTests.cs` |
| 4.6 kind 不整合破棄 | `OnEnvelopeReceived_KindMismatch_LogsWarningAndDrops` | `Tests/EditMode/OutputCommandDispatcherTests.cs` |
| 4.7 request/response 相関 | `RegisterRequestHandler_OnReceive_RespondsWithCorrelatedResponse` / `SelfLoop_RequestResponse_CorrelationMatched` | `Tests/EditMode/OutputCommandDispatcherTests.cs` / `Tests/PlayMode/SelfLoopDispatcherTests.cs` |
| 4.8 Last-write-wins | `SelfLoop_StateCommand_ReachesDispatcherHandler` の連続 Publish ケース | `Tests/PlayMode/SelfLoopDispatcherTests.cs` |
| 4.9 複数クライアント event FIFO | 上流 D-7 継承 + `SelfLoop_EventCommand_FifoOrderingPreserved` | `Tests/PlayMode/SelfLoopDispatcherTests.cs` |

## Requirement 5: メイン出力サーフェスへの描画分離

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 5.1 メイン出力カメラのカリングマスク | `DefaultCameraFactoryTests`（OperatorUI 除外） | `Tests/PlayMode/DefaultCameraFactoryTests.cs` |
| 5.2 OnGUI/IMGUI 非アタッチ | `Bootstrapper_NoUIDocumentOrIMGUIComponents` | `Tests/PlayMode/MainOutputNoOverlayTests.cs` |
| 5.3 例外/警告/診断の描画禁止 | `OutputShellLogger_TypeMembers_DoNotMentionGuiTypes` / `LoggerOutput_DoesNotAttachUIElementsToScene` | `Tests/EditMode/OutputShellLoggerTests.cs` / `Tests/PlayMode/MainOutputNoOverlayTests.cs` |
| 5.4 Unity 既定ダイアログ抑止／回避 | `Samples~/MinimalMainOutputScene/README.md` 運用ガイダンス | `Samples~/MinimalMainOutputScene/README.md` |
| 5.5 重大エラー時の描画維持 | `RoutingThrows_RecordsFailedButContinues` / `HandlerException_DoesNotInterruptCameraRendering` | `Tests/PlayMode/OutputSceneBootstrapperFlowTests.cs` / `MainOutputNoOverlayTests.cs` |
| 5.6 ハンドラへの描画禁止契約 | `IOutputCommandDispatcher` / `StateCommand<T>` / `EventCommand<T>` / `RequestCommand<T>` の XMLDoc | `Runtime/Abstractions/*.cs` |
| 5.7 診断表示の UI 側／コンソール限定 | `OutputShellLogger_TypeMethods_DoNotCallGuiOrUiToolkit` | `Tests/EditMode/OutputShellLoggerTests.cs` |

## Requirement 6: ライフサイクル

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 6.1 スタンドアロン起動時自動初期化 | `SingleBootstrapper_Survives` / `DuplicateBootstrapper_SecondInstanceSelfDestroys` | `Tests/PlayMode/OutputSceneBootstrapperLifecycleTests.cs` |
| 6.2 Editor PlayMode 自動初期化 | Bootstrapper Flow テスト（PlayMode 環境で実行） | `Tests/PlayMode/OutputSceneBootstrapperFlowTests.cs` |
| 6.3 PlayMode 終了時完全解放 | `AfterDestroy_DiagnosticsResetAndRootsCleared` / `OnDestroy_DisposesDispatcherAndClearsHandlers` | `Tests/PlayMode/OutputSceneBootstrapperReinitTests.cs` |
| 6.4 PlayMode 反復時クリーン再初期化 | `RepeatedSpawnAndDestroy_DoesNotAccumulateRoots` | `Tests/PlayMode/OutputSceneBootstrapperReinitTests.cs` |
| 6.5 Edit モードで起動しない | `Awake_InPlayMode_DoesNotErrorOut`（`Application.isPlaying` 分岐の構造保証） | `Tests/PlayMode/OutputSceneBootstrapperLifecycleTests.cs` |
| 6.6 ドメインリロード跨ぎ状態維持しない | `RepeatedSpawnAndDestroy_DoesNotAccumulateRoots`（D-9 継承） | `Tests/PlayMode/OutputSceneBootstrapperReinitTests.cs` |
| 6.7 スタンドアロン / Editor 挙動同一 | Bootstrapper Flow / Lifecycle テスト全般 | `Tests/PlayMode/*Tests.cs` |
| 6.8 Editor 固有挙動差の明文化 | `BuiltInDisplayRoutingServiceTests` の IsEditor ケース / XMLDoc | `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |

## Requirement 7: フェイルセーフ

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 7.1 UI 未接続時の初期構成完了 | `NoIpcBus_ReachesCompleteAndCameraIsRenderable` | `Tests/PlayMode/UiDisconnectedFailsafeTests.cs` |
| 7.2 UI 未接続中の描画継続 | `NoIpcBus_RendersForMultipleFrames_NoErrors` | `Tests/PlayMode/UiDisconnectedFailsafeTests.cs` |
| 7.3 UI 後続接続時の通常受信 | `AfterComplete_LateHandlerRegistration_IsInvokedOnReceive` | `Tests/PlayMode/UiDisconnectedFailsafeTests.cs` |
| 7.4 接続断時のシーン状態維持 | `NoIpcBus_DispatcherAcceptsRegisterAndUnregister` | `Tests/PlayMode/UiDisconnectedFailsafeTests.cs` |
| 7.5 接続断イベントの診断のみ記録 | XMLDoc 契約 + Logger 経路の構造保証（5.3 と同経路） | `Runtime/Diagnostics/OutputShellLogger.cs` |
| 7.6 UI 未接続でクラッシュしない | `NoIpcBus_DoesNotEmitErrorLogsDuringStartup` | `Tests/PlayMode/UiDisconnectedFailsafeTests.cs` |
| 7.7 外部クライアントの独立受付 | 上流 D-4 継承（CoreIpcBus が複数クライアント受け入れ可能） | `core-ipc-foundation` 側で担保 |

## Requirement 8: 検証戦略

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 8.1 単独での起動完遂 | `Samples~/MinimalMainOutputScene/MainOutput.unity` + Bootstrapper Flow テスト | `Samples~/MinimalMainOutputScene/MainOutput.unity` |
| 8.2 自己ループによるディスパッチャ検証 | `SelfLoopDispatcherTests` 3 シナリオ | `Tests/PlayMode/SelfLoopDispatcherTests.cs` |
| 8.3 Editor PlayMode 手動検証シーン | `Samples~/MinimalMainOutputScene/MainOutput.unity` + README | `Samples~/MinimalMainOutputScene/` |
| 8.4 単体テストケース提供 | EditMode + PlayMode テスト全般（本ドキュメント参照） | `Tests/` |
| 8.5 ディスプレイ切替モック差し替え | `FakeDisplayRoutingService` / 各テストファイル内 fake | `Tests/EditMode/Fakes/FakeDisplayRoutingService.cs` ほか |

## Requirement 9: 観測性

| AC | 検証テスト | 場所 |
|----|-----------|------|
| 9.1 シーン初期化フェーズログ | `AutoStart_ReachesCompletePhase` + Logger Info 経路 | `Tests/PlayMode/OutputSceneBootstrapperFlowTests.cs` |
| 9.2 ディスプレイ切替ログ | `BuiltInDisplayRoutingServiceTests` のログ検証 | `Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` |
| 9.3 ディスパッチャ登録／振り分けログ | `OutputCommandDispatcherTests` の Verbose ログ経路 | `Tests/EditMode/OutputCommandDispatcherTests.cs` |
| 9.4 未登録コマンドの診断ログ | `OnEnvelopeReceived_UnregisteredTopic_LogsWarningAndDrops` | `Tests/EditMode/OutputCommandDispatcherTests.cs` |
| 9.5 ハンドラ例外の診断ログ | `HandlerThrows_DispatcherCatchesAndContinues` / `Error_WithException_IncludesTypeAndMessage` | `Tests/EditMode/OutputCommandDispatcherTests.cs` / `OutputShellLoggerTests.cs` |
| 9.6 メイン出力サーフェス描画禁止（ログ系統） | `OutputShellLogger_*GuiTypes`（リフレクションでシグネチャ走査） | `Tests/EditMode/OutputShellLoggerTests.cs` |
| 9.7 ログレベル外部切替 | `MinLevel_IsRuntimeMutable` / `Verbose_BelowMinLevel_IsSuppressed` | `Tests/EditMode/OutputShellLoggerTests.cs` |
| 9.8 最小状態の外部取得 | `OutputDiagnosticsTests` 全般 | `Tests/EditMode/OutputDiagnosticsTests.cs` |

## 物理ディスプレイ非依存性（Req 8.5 サブ要件）

すべての PlayMode テストは `IDisplayRoutingService` のフェイク実装を経由するため、
`Display.displays.Length == 1` の CI 環境（モニタ非接続のヘッドレス Linux runner 等）でも
`IsFallbackActive` を含む結果を制御可能：

- `Tests/EditMode/Fakes/FakeDisplayRoutingService.cs` — EditMode テスト共有 fake
- `OutputSceneBootstrapperFlowTests.TestFakeDisplayRoutingService` — Flow テスト用 nested fake
- `OutputSceneBootstrapperReinitTests.ReinitFakeDisplayRoutingService` — Reinit テスト用 nested fake
- `MainOutputNoOverlayTests.SimpleFakeRouting` — 描画禁止テスト用 nested fake
- `UiDisconnectedFailsafeTests.SimpleFakeRouting` — フェイルセーフテスト用 nested fake

`BuiltInDisplayRoutingServiceTests`（暫定実装の単体テスト）も `IDisplayProbe` のスタブを差し込むことで
`Display.displays.Length` を制御し、CI 環境で安定実行可能となっている。

## 実行所要時間（参考）

各テストファイル単位での想定所要時間：

- EditMode テスト一式: < 5 秒（設定／呼び出しのみ、I/O なし）
- PlayMode テスト（IPC ループバック以外）: < 30 秒（フレーム待機が中心）
- `SelfLoopDispatcherTests`: < 15 秒（CoreIpcRuntimeHost の InitializeAsync 含め最大 5 秒 / シナリオ）
- 合計: 約 50 秒。CI の 2 分制約に対し十分なマージン。
