# Implementation Plan

本実装計画は `output-renderer-shell` spec の `requirements.md` / `design.md` に基づき、TDD（Red → Green → Refactor）で段階的に構築する。
上流 spec `core-ipc-foundation` の抽象インタフェース（`ICoreIpcServer`、`MessageEnvelope` 等）が利用可能であることを前提とする（本 spec 側ではモック／テストダブルで差し替え可能な構造を採る）。

並列実行（`(P)` マーカー）は、`_Boundary:_` が別コンポーネントを指し、共有リソース・順序依存のないサブタスクにのみ付与する。

---

## 1. Foundation: パッケージ骨格とテスト基盤整備

- [x] 1.1 UPM パッケージとランタイム／テスト asmdef の初期配置
  - `Packages/com.vtubersystembase.output-renderer-shell/` を UPM パッケージとして作成（`package.json`、`Runtime/`、`Tests/EditMode/`、`Tests/PlayMode/`、`Samples~/MinimalMainOutputScene/` の空骨格を含む）
  - `VTuberSystemBase.OutputRendererShell.Runtime.asmdef` を作成し、`VTuberSystemBase.CoreIpcFoundation.Abstractions`・`Unity.RenderPipelines.Universal.Runtime`・`Unity.RenderPipelines.Core.Runtime` への参照のみを宣言（具体トランスポート asmdef へは参照しない）
  - `VTuberSystemBase.OutputRendererShell.EditModeTests.asmdef` / `VTuberSystemBase.OutputRendererShell.PlayModeTests.asmdef` を作成し、Runtime asmdef と NUnit・Unity Test Framework を参照
  - Unity Editor で Assembly Reload がエラーなく完了し、Test Runner に EditMode / PlayMode の空テストランナーが現れることをもって完了とする
  - _Requirements: 3.7, 6.7_

- [x] 1.2 共通型（enum / struct / 登録トークン）と XMLDoc 契約の定義
  - `OutputCommandKind`（`State` / `Event` / `Request` / `Response`）、`OutputSceneInitPhase`（`Uninitialized` 〜 `Complete` / `Failed`）、`DisplayAssignmentInfo`、`DisplayRoutingConfig`、`StateCommand<T>` / `EventCommand<T>` / `RequestCommand<T>`、`OutputSceneRootNames` 定数クラスを `Runtime/Abstractions/` に追加する
  - XMLDoc で以下の契約を明文化する：state ハンドラ実装の冪等性要求（Req 4.4）／ハンドラからのメイン出力サーフェスへの GUI 描画禁止（Req 5.6）／`DisplayAssignmentInfo.IsFallbackActive` の意味（Req 2.4a）
  - 各値型が `readonly struct` / `record struct` として不変であり、既定値で NPE を起こさないことを EditMode テスト `CommonTypesDefaultsTests` で検証して完了とする
  - _Requirements: 1.7, 2.4a, 4.4, 4.5, 5.6_

- [x] 1.3 `OutputShellLogger` とログレベル切替設定の導入
  - `LogLevel`（`Verbose` / `Info` / `Warning` / `Error`）と `OutputShellLogger`（`Verbose` / `Info` / `Warning` / `Error` メソッド、構造化情報として `component` / `topic` / `correlationId` を引数で受ける）を実装
  - 出力先は `UnityEngine.Debug.Log*` に限定し、`OnGUI` / `IMGUI` / UI Toolkit への出力経路を持たないことをコードレベルで保証
  - EditMode テスト `OutputShellLoggerTests` で、`MinLevel` を切り替えると下位レベルのログが抑制される／`Error` が `Debug.LogError` 経由で呼ばれることをログスコープキャプチャで確認し完了とする
  - _Requirements: 5.3, 5.7, 9.1, 9.6, 9.7_

---

## 2. Core: シーン骨格（Roots / Camera / Light / Global Volume）

- [x] 2.1 (P) ルート GameObject 階層と `IOutputSceneRoots` サービスロケータの実装
  - `StageRoot` / `CharactersRoot` / `LightsRoot` / `CamerasRoot` / `VolumeRoot` を `OutputSceneRootNames` 定数に従って生成する `OutputSceneRoots` を実装し、`IOutputSceneRoots` のプロパティ（`Stage` / `Characters` / `Lights` / `Cameras` / `Volumes` / `GlobalVolumeProfile` / `DefaultCamera`）で参照を公開
  - シーン配置時に同名ルートが既に存在する場合は生成せず再利用する冪等な挙動を実装（Editor プロトタイプ保存シナリオに対応）
  - PlayMode テスト `OutputSceneRootsTests` で、Awake 直後に 5 ルートすべてが存在し、プロパティが non-null を返し、後続 spec が `Stage` 配下へ子 GameObject を追加しても他ルートを破壊しないことをもって完了とする
  - _Requirements: 1.1, 1.7, 1.8_
  - _Boundary: OutputSceneRoots_

- [x] 2.2 (P) `DefaultCameraFactory` による URP 対応デフォルトカメラの生成
  - プロジェクトの URP Asset が参照された状態で `CamerasRoot` 配下に 1 台のデフォルトカメラを配置し、ステージ全景を捉える既定 Transform を設定
  - メイン出力カメラのカリングマスクから「オペレーター UI 専用レイヤー」を除外する初期構成をコードで固定（UI レイヤーの想定名称をコメントで明示）
  - `UniversalAdditionalCameraData` が付与され、`targetDisplay` の設定は `IDisplayRoutingService` の責務として委譲する構造になっていることを PlayMode テスト `DefaultCameraFactoryTests` で確認
  - カメラ生成後にレンダリングが 1 フレーム以上成立する（真っ黒・描画停止にならない）ことをスクリーンショット or `Camera.Render` 呼び出しで検証し完了とする
  - _Requirements: 1.2, 1.4, 5.1_
  - _Boundary: DefaultCameraFactory_

- [ ] 2.3 (P) `DefaultLightFactory` によるデフォルト Directional Light の生成
  - `LightsRoot` 配下に Directional Light 1 基を生成し、真っ黒画面を回避する既定輝度・角度を設定
  - PlayMode テスト `DefaultLightFactoryTests` で、ライトが `LightsRoot` の子であり、`type == Directional` / `enabled == true` であること、およびシーン描画結果がデフォルトカメラで有効な輝度を返すことをもって完了とする
  - _Requirements: 1.3_
  - _Boundary: DefaultLightFactory_

- [ ] 2.4 (P) `GlobalVolumeFactory` による空の Global Volume と空 `VolumeProfile` の生成
  - `VolumeRoot` 配下に `Volume` コンポーネントを付与した GameObject を生成し、`isGlobal = true` / `priority = 0` / ランタイム生成の空 `VolumeProfile` を割り当て
  - `IOutputSceneRoots.GlobalVolumeProfile` から取得したインスタンスに対し、後続 spec が `AddComponent<T>()` で Override を追加可能であることを PlayMode テスト `GlobalVolumeFactoryTests` で検証
  - 生成直後の `VolumeProfile.components` が空配列であり、かつ Dispose／PlayMode 停止後に ScriptableObject インスタンスがリークしないことをもって完了とする
  - _Requirements: 1.5, 1.8_
  - _Boundary: GlobalVolumeFactory_

---

## 3. Core: ディスプレイ振り分け（抽象 + 暫定実装）

- [ ] 3.1 `IDisplayRoutingService` 抽象と `DisplayRoutingConfig` の確定
  - `IDisplayRoutingService`（`Activate(Camera, DisplayRoutingConfig) -> DisplayAssignmentInfo` / `GetAssignment()` / `IsFallbackActive` / `IDisposable`）と `DisplayRoutingConfig`（`TargetDisplayIndex` 既定 1、`FullScreenMode` 既定 `FullScreenWindow`、`SuppressEditorWarning`）を最終化
  - XMLDoc で RDS（spec #7）差し替え接合点であること、`Activate` 呼び出しにより Camera の `targetDisplay` を実装側が設定する契約であることを明示
  - テストダブル用の `FakeDisplayRoutingService` を `Tests/EditMode/Fakes/` に配置し、Activate 呼び出し履歴と任意 `DisplayAssignmentInfo` を返せるように実装
  - EditMode テスト `IDisplayRoutingServiceContractTests` で、Fake 実装が契約（Activate 後に `GetAssignment` が同値を返す／`IsFallbackActive` フラグが立つ）を満たすことをもって完了とする
  - _Requirements: 2.1, 2.5, 2.6, 8.5_

- [ ] 3.2 `BuiltInDisplayRoutingService` による暫定実装
  - `Display.displays[n].Activate()` ベースで `TargetDisplayIndex` のディスプレイをアクティブ化し、`Camera.targetDisplay` を設定
  - 指定インデックスが `Display.displays.Length` を超える場合は Display 0 へフォールバックし、`DisplayAssignmentInfo.IsFallbackActive = true` と警告ログを残す（OR-1）
  - `Application.isEditor == true` の場合は Editor PlayMode 固有の制限（`Display.Activate` が効かない）を `IsEditorLimitedMode = true` として記録し、`SuppressEditorWarning` が false の場合に Info 以上のログで通知
  - `Screen.fullScreenMode` を `DisplayRoutingConfig.FullScreenMode` に従って設定（Standalone のみ、Editor では no-op）
  - `TargetDisplayIndex` が 0 / 1 / 存在しない巨大値の各ケースで期待どおりの `DisplayAssignmentInfo` を返すことを PlayMode テスト `BuiltInDisplayRoutingServiceTests`（物理ディスプレイに依存しないようテストは `Display.displays.Length` をモック可能な薄ラッパ経由で検証）で確認し完了とする
  - _Requirements: 2.2, 2.3, 2.4, 2.4a, 2.7, 2.8, 6.8, 9.2_
  - _Boundary: BuiltInDisplayRoutingService_
  - _Depends: 3.1_

---

## 4. Core: コマンドディスパッチャ

- [ ] 4.1 `HandlerRegistry` と登録解除トークンの実装
  - `Dictionary<(string topic, OutputCommandKind kind), Delegate>` を内部状態として保持し、登録・ルックアップ・解除（`IDisposable` トークン Dispose）を提供
  - 同一 `(topic, kind)` への重複登録は例外を送出する Fail-Fast 方針を実装
  - EditMode テスト `HandlerRegistryTests` で、登録 → ルックアップ → 解除後ルックアップ不可、重複登録で例外、異なる `(topic, kind)` は独立管理されること、Dispose 後に登録数が減少することを検証して完了とする
  - _Requirements: 3.3, 4.5, 4.6_
  - _Boundary: HandlerRegistry_

- [ ] 4.2 `IOutputCommandDispatcher` と `OutputCommandDispatcher` の実装
  - `RegisterStateHandler<TPayload>` / `RegisterEventHandler<TPayload>` / `RegisterRequestHandler<TRequest, TResponse>` を分離 API として公開（Req 4.5）
  - 受信コールバック（`ICoreIpcServer` の `OnCommandReceived` 相当）で `(topic, kind)` ルックアップ → 登録 kind と envelope.kind の二重検証 → ハンドラを `try/catch` でラップして invoke する流れを実装
  - 未登録コマンド受信時は `OutputShellLogger.Warning` で topic と kind を記録し破棄（Req 3.5, 9.4）
  - ハンドラ実行中の例外は `Error` ログに topic / kind / correlationId / 例外内容を記録し、ディスパッチャ自体と描画ループは継続（Req 3.6, 5.5, 9.5）
  - `RegisteredHandlerCount` プロパティが登録数を返し、`Dispose` で全ハンドラ解除と受信購読解除を行うこと
  - state の coalesce / event の FIFO / request/response の相関は上流 `core-ipc-foundation`（D-7 / D-10）契約をそのまま引き継ぎ、本コンポーネント側でキューを持たないことをコードコメントで明示
  - EditMode テスト `OutputCommandDispatcherTests` で、(a) 登録 → 受信シミュレーション → invoke 到達、(b) 未登録コマンド破棄 + 警告ログ、(c) kind 不整合破棄 + 警告ログ、(d) ハンドラ例外捕捉 + ディスパッチャ継続、(e) request → response の相関 ID 一致、の 5 シナリオを検証して完了とする
  - _Requirements: 3.2, 3.3, 3.4, 3.5, 3.6, 3.8, 4.1, 4.2, 4.3, 4.5, 4.6, 4.7, 4.8, 4.9, 5.5, 9.3, 9.4, 9.5_
  - _Boundary: OutputCommandDispatcher_
  - _Depends: 4.1, 1.3_

---

## 5. Core: 診断 API

- [ ] 5.1 `IOutputDiagnostics` / `OutputDiagnostics` の実装
  - `CurrentPhase`（`OutputSceneInitPhase`）／ `CurrentDisplayAssignment`（`DisplayAssignmentInfo`）／ `RegisteredHandlerCount` ／ `LastErrorMessage` ／ `LastErrorAtUnixMs` の読み取り API を公開
  - 書き込みは本 spec 内コンポーネントからのみ許可する `internal` セッター or 明示的 `Set*` メソッドで実装し、任意スレッドから `Get*` が安全に呼べるよう volatile / lock を適切に選択
  - `Uninitialized → RootsCreated → CameraReady → LightReady → VolumeReady → IpcServerReady → DispatcherReady → DisplayRouted → Complete` の単調遷移、および任意段階から `Failed` への脱出のみを許容する遷移検証ロジックを実装
  - EditMode テスト `OutputDiagnosticsTests` で、(a) 単調遷移の成功ケース、(b) 逆方向遷移の拒否、(c) `Failed` 状態記録時に `LastErrorMessage` / `LastErrorAtUnixMs` が更新されること、(d) `RegisteredHandlerCount` がディスパッチャからの更新を反映することをもって完了とする
  - _Requirements: 2.4a, 9.8_
  - _Boundary: OutputDiagnostics_

---

## 6. Integration: Composition Root と起動シーケンス

- [ ] 6.1 `OutputSceneBootstrapper` MonoBehaviour の骨格と `OverrideServices` 注入ポイント
  - `OutputSceneBootstrapper` を MonoBehaviour として実装し、`[SerializeField] DisplayRoutingConfig _routingConfig` と `[SerializeField] bool _autoStart` を公開
  - `OverrideServices(IDisplayRoutingService, ICoreIpcServer)` を `Awake` 前に呼ぶことでテスト時のモック差し替えを可能にする（本番ビルドでは呼ばれず既定具体実装が使われる）
  - `FindObjectsByType<OutputSceneBootstrapper>()` による重複配置検出を `Awake` で実施し、2 つ目以降を警告ログ付きで自己破棄
  - PlayMode テスト `OutputSceneBootstrapperLifecycleTests`（骨格部分）で、重複配置時に 1 つのみ活動が継続することを検証
  - _Requirements: 6.1, 6.2, 6.5_

- [ ] 6.2 Flow 1 に準拠した起動シーケンスの実装
  - `Awake` 内で Roots → Camera → Light → Volume の順に生成し、各フェーズ完了ごとに `IOutputDiagnostics.CurrentPhase` を更新（Req 1.6）
  - `Start` 内で IPC サーバ起動 → `OutputCommandDispatcher` を IPC 受信コールバックへバインド → `IDisplayRoutingService.Activate(DefaultCamera, _routingConfig)` の順で実行し、各段階のフェーズを Diagnostics へ反映
  - 起動シーケンス中に発生した任意フェーズの例外を捕捉し、`OutputSceneInitPhase.Failed` を記録したうえで可能な限り後続フェーズを続行（描画継続優先、Req 5.5）、`Application.Quit()` は呼ばない
  - PlayMode テスト `OutputSceneBootstrapperFlowTests` で、(a) 正常起動時に `CurrentPhase` が `Complete` に到達すること、(b) Roots / Camera / Light / Volume が任意のコマンド受信前にすべて準備完了していること、(c) ディスプレイ割当が Diagnostics から取得可能なことをもって完了とする
  - _Requirements: 1.6, 2.2, 2.5, 3.1, 5.5, 6.1, 6.2, 6.7, 9.1_
  - _Depends: 2.1, 2.2, 2.3, 2.4, 3.2, 4.2, 5.1_

- [ ] 6.3 `OnDestroy` 逆順シャットダウンと PlayMode 反復時のクリーンアップ
  - `OnDestroy` で `IOutputCommandDispatcher.Dispose` → `IDisplayRoutingService.Dispose` → IPC サーバ `Shutdown` → ルート GameObject 破棄 の逆順処理を実装
  - ドメインリロードを跨いだ状態維持を試みず、PlayMode 開始のたびに新しいライフサイクルで初期化する（D-9 継承）
  - PlayMode テスト `OutputSceneBootstrapperReinitTests` で、PlayMode 開始→停止を 10 回反復してもメモリリーク（ScriptableObject 残留、GameObject 残留、ディスパッチャハンドラ残留）が発生しないこと、および停止後に `CurrentPhase = Uninitialized` へ戻ることをもって完了とする
  - _Requirements: 6.3, 6.4, 6.6_
  - _Depends: 6.2_

- [ ] 6.4 メイン出力サーフェス描画禁止契約の固定化
  - `OutputSceneBootstrapper` が生成するメイン出力カメラ配下および Roots 配下へ `OnGUI` / `IMGUI` / UI Toolkit の `UIDocument` / `PanelSettings` を一切アタッチしないことを構造的に保証する（コード上で追加を試みない、XMLDoc で禁止契約を明示）
  - Unity 既定のエラーダイアログ・クラッシュダイアログ・Development Build オーバーレイがメイン出力側で表示される可能性への運用ガイダンスを `Samples~/MinimalMainOutputScene/README.md` に記載（Player Settings の関連オプションと Editor 側での確認手順）
  - `OutputShellLogger` から出るすべてのメッセージが Unity Console のみに流れ、メイン出力サーフェスのピクセルに影響しないことを PlayMode テスト `MainOutputNoOverlayTests` で（カメラ描画結果に GUI 由来のピクセルが混入しないこと、ハンドラ例外ログ発生時も描画が継続していることを確認して）検証し完了とする
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.6, 5.7, 9.6_
  - _Depends: 6.2_

---

## 7. Integration: フェイルセーフと自己ループ検証

- [ ] 7.1 UI 未接続／接続断フェイルセーフの検証と実装固め
  - `ICoreIpcServer` のサーバ起動に対してクライアント未接続のまま PlayMode 起動しても、シーン初期構成・ディスプレイ切替・ディスパッチャ起動が完遂し、デフォルトカメラでの描画が継続する挙動を固定
  - 接続断・再接続イベントが通知された場合、`OutputShellLogger.Info` に記録するのみでメイン出力サーフェスへの視覚的通知は一切行わない（Req 7.5 / 5.3）
  - 後続接続後は通常どおりコマンド受信・反映が開始されること、外部クライアント（将来の LAN / WebUI）が組み込み UI の接続有無に依存せず接続できる構造であることをコードで維持
  - PlayMode テスト `UiDisconnectedFailsafeTests` で、(a) クライアント未接続のまま `Complete` 到達、(b) 未接続状態で 5 秒間描画ループが継続、(c) 後続接続後にハンドラが通常 invoke されること、(d) 例外・クラッシュが発生しないこと、をもって完了とする
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_
  - _Depends: 6.2_

- [ ] 7.2 `core-ipc-foundation` 自己ループ機構を用いた End-to-End ディスパッチ検証
  - `core-ipc-foundation` の `InMemoryLoopbackTransport`（spec #1 Requirement 8）を用い、同一プロセス内でサーバ／クライアント双方を起動してダミーコマンドを送受信する PlayMode テスト `SelfLoopDispatcherTests` を追加
  - state（同一 topic への連続送信で最新値のみ反映されること、ハンドラが冪等に呼ばれること）／ event（FIFO 順と取りこぼしゼロ）／ request-response（correlationId の 1:1 対応）の 3 種類をすべて検証
  - 複数クライアント想定のシミュレーション（2 セッション分の送信）で、state は last-write-wins、event は到着順 FIFO が守られることも検証（OR-2）
  - 全シナリオが 5 秒以内に成功することをもって完了とする（性能目標 `docs/requirements.md` §6.1 の範囲内）
  - _Requirements: 4.2, 4.3, 4.7, 4.8, 4.9, 8.2_
  - _Depends: 6.2_

---

## 8. Validation: サンプルシーンと単独検証経路の整備

- [ ] 8.1 `Samples~/MinimalMainOutputScene` 最小サンプルシーンと手動検証手順
  - `Samples~/MinimalMainOutputScene/MainOutput.unity` に `OutputSceneBootstrapper` 1 つのみを配置した最小シーンを作成し、PlayMode で開くだけで Requirement 1〜9 の主要挙動が観察可能な状態にする
  - `Samples~/MinimalMainOutputScene/README.md` に手動検証手順を記載（PlayMode 起動→ Console で全フェーズの完了ログ確認／Display 2 接続有無でのフォールバック挙動確認／PlayMode 停止→再開 3 回の回帰確認／Unity 既定ダイアログ抑止に関する Player Settings ガイダンス）
  - サンプル UPM `Samples` の import 手順も README に含め、後続 spec の担当者が開発開始時に本シェル単体で起動を確認できる状態とする
  - サンプルシーンが後続 spec（#3〜#6）不在でもエラーなく `Complete` に到達することをもって完了とする
  - _Requirements: 8.1, 8.3_
  - _Depends: 6.3_

- [ ] 8.2 Requirements カバレッジ回帰スイートの整備
  - EditMode / PlayMode テスト全体を Test Runner でまとめて実行し、`requirements.md` の Requirement 1〜9 の各 Acceptance Criteria に対する検証テストが存在することを `docs` コメントまたはテストフィクスチャの `[Description]` 属性で対応付け
  - モック `IDisplayRoutingService`（FakeDisplayRoutingService）を用い物理ディスプレイに依存しない回帰実行が可能であることを確認（Req 8.5）
  - CI 相当（`unity -batchmode -runTests`）で 2 分以内に全テストが成功することをもって完了とする
  - _Requirements: 8.4, 8.5_
  - _Depends: 2.1, 2.2, 2.3, 2.4, 3.2, 4.2, 5.1, 6.2, 6.3, 6.4, 7.1, 7.2_

- [ ] 8.3* 観測性ログの拡張検証（任意・MVP 後）
  - `OutputShellLogger` のログレベルを Verbose に切り替え、シーン初期化フェーズごとの開始／完了／失敗ログ、ディスプレイ切替の対象インデックス・割当結果・失敗事由、ハンドラ登録／解除／振り分け結果が網羅的に出力されることを EditMode テストで確認
  - 本タスクは実装クリティカルではなく、Req 9.1〜9.5 / 9.7 の補助カバレッジとして MVP 後に追加する
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.7_
  - _Depends: 6.4, 7.1_
