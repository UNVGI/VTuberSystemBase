# Implementation Plan

> Feature: `stage-lighting-volume-tab`  
> Language: 日本語（spec.json.language=ja）  
> アーキテクチャ: MVVM-Lite + Facade（Contracts asmdef / Runtime asmdef 2 分割）  
> 上流依存: `core-ipc-foundation`（Abstractions）, `ui-toolkit-shell`（Runtime / CommonUi）, `output-renderer-shell`（メイン出力側アダプタは範囲外、Contracts のみ共有）  
> 実装スタイル: 可能な限り TDD（先にテスト → 実装 → リファクタ）。Contracts 層は純 DTO のため Contract Smoke Test のみ、Runtime の Services / ViewModel / View はテストファーストで進める。  
> 並列マーク `(P)`: 同一親の直前 peer に依存せず、`_Boundary:_` の示す責任境界が重ならないサブタスクに付与する。Foundation 層と Integration 層は原則直列。

---

- [ ] 1. Foundation: UPM パッケージ骨格と 2 asmdef 分割、テスト基盤、モック基盤の整備
- [x] 1.1 UPM パッケージ骨格と 2 つの Runtime asmdef（Contracts / Runtime）を作成する
  - `Packages/jp.hidano.vtuber-system-base.stage-lighting-volume-tab/` 配下に `package.json` を作成し、依存として `core-ipc-foundation`・`ui-toolkit-shell`・Unity UIElements・URP・System.Text.Json・SceneViewStyleCameraController を宣言する
  - `Runtime/Contracts/VTuberSystemBase.StageLightingVolumeTab.Contracts.asmdef` を追加し、Unity 標準型のみ参照可能な純 DTO 層として閉じる（`core-ipc-foundation.Core` 等の具体実装 asmdef への参照を禁止）
  - `Runtime/Runtime/VTuberSystemBase.StageLightingVolumeTab.Runtime.asmdef` を追加し、Contracts asmdef と `ui-toolkit-shell.Runtime` / `ui-toolkit-shell.CommonUi` / `core-ipc-foundation.Abstractions` を参照する
  - Runtime.Uxml / Editor / Tests のディレクトリを空で作成し、今後のタスクの受け皿を用意する
  - 観測可能完了: Unity Editor で本パッケージが読み込まれコンパイルエラーが 0 件、`Assembly-CSharp` 側から Contracts / Runtime 双方の名前空間が参照できる
  - _Requirements: 1.7_

- [x] 1.2 テスト用 asmdef（Runtime / PlayMode / Editor）と DI 差替可能なフェイク／モック基盤を用意する
  - `Tests/Runtime/VTuberSystemBase.StageLightingVolumeTab.Tests.Runtime.asmdef` を追加し、Contracts + Runtime + `core-ipc-foundation.Abstractions` + NUnit に参照を通す
  - `Tests/PlayMode/VTuberSystemBase.StageLightingVolumeTab.Tests.PlayMode.asmdef` と `Tests/Editor/VTuberSystemBase.StageLightingVolumeTab.Tests.Editor.asmdef` を追加する
  - `FakeIpcClient`（`IUiCommandClient` / `IUiSubscriptionClient` を満たす in-memory 実装、送受信履歴を検証可能）、`FakePresetStorage`（`IPresetStorage` の on-memory 実装 + 破損ファイル再現フラグ）、`FakeClock`（`IClock` 実装、時刻を手動で進める）、`FakePreviewCameraAdapter`・`FakePreviewRenderTextureAccessor`、`FakeAsyncAssetLoader`、`FakeConnectionStatus`、`FakeDiagnosticsLogger` を `Tests/Runtime` 配下に実装する
  - 観測可能完了: テストプロジェクトが起動し、空の `SmokeTests` クラスに 1 本スモークテストを置いて緑で通る（`FakeIpcClient` を `new` してメソッドが呼び出せる）
  - _Requirements: 12.1, 12.2, 12.3, 12.6, 12.7, 12.8_

- [x] 1.3 共通型（`Vector3Dto` / `Vector4Dto` / `ColorDto` / `LightTypeDto` / `ParamKind`）と `StageLightingTopics` 定数クラスを Contracts 層に定義する
  - `Contracts/Dtos/` に共通 record struct / enum を追加し、全 IPC DTO が共有するプリミティブ群を確定させる
  - `Contracts/Topics/StageLightingTopics.cs` を追加し、Stage / Light / Volume / Preview の全 topic 文字列定数と動的トピック生成ヘルパ（`LightProperty`, `VolumeOverrideEnabled`, `VolumeOverrideParam`）を実装する
  - Contract Smoke Test: `StageLightingTopicsTests` で各定数値とヘルパの出力（例: `light/{lightId}/intensity`、`volume/override/UnityEngine.Rendering.Universal.Bloom/intensity`）を固定化し、今後の typo を検出する
  - 観測可能完了: `StageLightingTopicsTests` が緑で通り、UI 側とメイン出力側アダプタが同じ文字列を参照するための単一情報源が確立される
  - _Requirements: 1.7_

---

- [ ] 2. IPC コントラクト層（Contracts asmdef）の DTO と Preview Locator を TDD で確定する
- [x] 2.1 (P) Light 系 DTO（`LightInitialDto` / `LightListItemDto` / `LightListDto` / `LightCommandDto` / `LightAddedDto` / `LightErrorDto`）を JSON ラウンドトリップ検証付きで実装する
  - Contracts 層に 6 つの record struct を追加する（`Intensity >= 0`、`Range >= 0`、`SpotAngle` 1〜179 等の前提コメントを明記）
  - `LightDtosJsonRoundtripTests` を先に書き、`System.Text.Json` でシリアライズ → デシリアライズ後に値が一致することを検証する
  - `LightCommandDto` は `Op = "add" | "remove"` のバリアント分岐（`Op=remove` で `LightId` 必須、`Op=add` で `Initial` 必須）をユニットテストで固定化する
  - 観測可能完了: ラウンドトリップテストと Op バリアントテストが緑で通り、`light/command` / `light/added` / `light/error` / `lights/list` のワイヤフォーマットが決定する
  - _Requirements: 4.1, 4.3, 4.4, 4.5, 4.10_
  - _Boundary: StageLightingVolumeTab.Contracts.Dtos.Light_

- [x] 2.2 (P) Stage 系 DTO（`StageCatalogEntryDto` / `StageCatalogDto` / `StageCurrentDto` / `StageCommandDto` / `StageLoadFailedDto`）を JSON ラウンドトリップ検証付きで実装する
  - Contracts 層に 5 つの record struct を追加する（`StageCommandDto.Op = "load" | "unload"`、`load` 時 `AddressableKey` 必須）
  - `StageDtosJsonRoundtripTests` と Op バリアントテストを先に書く
  - 観測可能完了: `stage/catalog` / `stage/current` / `stage/command` / `stage/loaded` / `stage/load-failed` の全 topic の payload 型が固定し、テストで検証済み
  - _Requirements: 3.1, 3.4, 3.5, 3.6, 3.8_
  - _Boundary: StageLightingVolumeTab.Contracts.Dtos.Stage_

- [x] 2.3 (P) Volume 系 DTO（`VolumeOverrideSchemaDto` / `VolumeOverrideTypeDto` / `VolumeOverrideParamDto` / `VolumeOverrideParamValueDto` / `VolumeOverrideParamRangeDto`）を ParamKind ディスクリミネータ方式で実装する
  - Contracts 層に `ParamKind` enum（Bool/Int/Float/ClampedFloat/Color/Vector2/Vector3/Vector4/Enum/Unknown）を追加する
  - `VolumeOverrideParamValueDto` を ParamKind + nullable payload フィールドのディスクリミネーテッドユニオンとして設計し、`JsonConverter` の polymorphism に依存しない形にする
  - `VolumeDtosJsonRoundtripTests` に加え、`Kind=Unknown` を含むスキーマが受信できることのテスト（後続 6.10 のスキップロジック向け入力）を追加する
  - 観測可能完了: ラウンドトリップテストが各 ParamKind で緑、未知型 `Unknown` を含むスキーマもデシリアライズでき、UI 側は後段でスキップ判定可能
  - _Requirements: 6.1, 6.2, 6.10, 6.11_
  - _Boundary: StageLightingVolumeTab.Contracts.Dtos.Volume_

- [x] 2.4 (P) Preview 系 DTO（`PreviewCommandDto` / `PreviewStateDto`）と `StagePreviewHostLocator` / `IPreviewHostService` を Contracts 層に実装する
  - `PreviewCommandDto.Op = "set-enabled" | "reset-view" | "init" | "dispose"`、`set-enabled` 時 `Enabled` 必須のユニットテストを先に書く
  - `StagePreviewHostLocator` は同一プロセス内 Singleton アクセサとして `Register` / `Unregister` / `Current` を公開し、重複登録時は警告ログ + 最新採用、`Unregister` 後は `Current == null` を保証する
  - `StagePreviewHostLocatorTests` で登録 → 参照 → 解除 → null のライフサイクルと、重複登録時の最新採用を検証する
  - 観測可能完了: Preview DTO のラウンドトリップと Locator のライフサイクルテストが緑で通り、メイン出力側アダプタが本 spec の抽象のみに依存して `StagePreviewHost` を実装できる
  - _Requirements: 2.1, 2.5, 2.6, 2.7, 2.8_
  - _Boundary: StageLightingVolumeTab.Contracts.Preview_

- [x] 2.5 (P) プリセット JSON スキーマ（`PresetFileRoot` / `PresetDto` / `LightConfigDto` / `VolumeOverrideConfigDto`）を SchemaVersion=1 で実装する
  - Contracts 層に 4 クラスを追加する（`SchemaVersion` は既定 1、`LightConfigDto.LightId` は含めない = 復元時に再採番）
  - `PresetSchemaRoundtripTests` でサンプルプリセット（ステージあり/なし・Light 複数・Volume Override 複数）の JSON ラウンドトリップを検証する
  - 未知フィールド無視（`JsonSerializerOptions.IgnoreReadOnlyProperties` 等）と、`SchemaVersion != 1` の検出テストを追加し、将来 migrator を差し込む余地を残す
  - 観測可能完了: プリセットファイルの JSON フォーマットが固定され、ラウンドトリップテスト + 未知 schemaVersion 検出テストが緑で通る
  - _Requirements: 7.1, 7.8, 7.9, 8.1, 8.2_
  - _Boundary: StageLightingVolumeTab.Contracts.Presets_

---

- [ ] 3. Runtime Services 層（永続化・デバウンス・購読状態・キャッシュ・バリデーション）を TDD で実装する
- [x] 3.1 `IPresetStorage` と `JsonPresetStorage` を atomic write・破損フォールバックを含めて実装する
  - 先に `JsonPresetStorageTests` を書き、保存 → 読込往復、初回起動時のファイル不在、書込中プロセスキル相当（temp ファイル残骸）、`.corrupted-{unixMs}` へのリネームによる破損フォールバック、`SemaphoreSlim` による並列書込直列化を検証する
  - 実装: `SaveAsync` は `tmp.json` への書込 → `File.Move(overwrite:true)` で atomic に差し替え、失敗時は `SaveResult.IOError` を返す
  - `LoadAsync` はファイル不在なら `PresetLoadResult.Success=true, Data=null`、パース失敗なら破損ファイルをリネームして初回起動扱いにする
  - `FlushAsync` は保留中の書込があれば完了を待って最新を 1 回追加書込する
  - 観測可能完了: テスト Temp ディレクトリで全ケースが緑、Windows 上で atomic write のセマンティクス（途中中断でも既存ファイル破損なし）が再現できる
  - _Requirements: 8.1, 8.3, 8.4, 8.5, 8.7, 8.9, 8.10, 10.5_
  - _Boundary: StageLightingVolumeTab.Runtime.Services.Persistence_

- [x] 3.2 (P) `IClock` / `DebounceFlusher` を 500 ms デバウンスとテスト可能な時刻抽象で実装する
  - 先に `DebounceFlusherTests` を書き、`FakeClock` で時刻を 500 ms 進めたときのみ flush が 1 度だけ発火すること、連続 `Schedule` で最新の `flushAction` のみが実行されること、`FlushImmediateAsync` が保留アクションを即時実行すること、`Dispose` で保留アクションが破棄されることを検証する
  - 実装: 内部で最新の `CancellationTokenSource` のみ保持し、古いものは破棄してリソース膨張を防ぐ
  - 観測可能完了: `DebounceFlusherTests` が全ケース緑で通り、ViewModel が後段で `Schedule(() => JsonPresetStorage.FlushAsync(...))` を安全に呼び出せる
  - _Requirements: 4.7, 8.3, 12.8_
  - _Boundary: StageLightingVolumeTab.Runtime.Services.Debounce_

- [x] 3.3 (P) `LightListState` / `StageCatalogState` / `VolumeSchemaCache` を `FakeIpcClient` で購読・リクエストの単体テスト駆動で実装する
  - `LightListStateTests` で `lights/list` 購読 → `LightListChangeEvent`（Added / Removed）の差分通知、lightId 採番順の安定ソート、重複 lightId 受信時の先着採用 + 警告ログを検証する
  - `StageCatalogStateTests` で `stage/catalog` 購読と更新時の追従、取得失敗時のエラー状態公開を検証する
  - `VolumeSchemaCacheTests` で `volume/override/schema` の `RequestAsync` 成功時キャッシュ、失敗時の再試行 API、`ParamKind=Unknown` を含むスキーマも受容することを検証する
  - 実装: 各クラスは `StartSubscribing` / `StopSubscribing` / `Dispose` を備え、ViewModel のライフサイクルに同期する
  - 観測可能完了: 3 クラスすべての単体テストが緑、モック IPC のみで購読・差分通知・キャッシュ更新が検証可能
  - _Requirements: 3.1, 3.9, 3.10, 4.1, 4.4, 4.8, 6.1, 6.9, 6.11, 7.8_
  - _Boundary: StageLightingVolumeTab.Runtime.Services.State_

- [x] 3.4 (P) `LightPropertyValidator` を境界値テストと `VolumeOverrideParamDto.Range` 適用で実装する
  - `LightPropertyValidatorTests` で `Intensity >= 0`、`Range >= 0`、`SpotAngle` 1〜179（境界: 1, 179, 0.99, 179.01, 負値）、`ColorDto` 各チャンネル 0〜1 の境界値を検証する
  - `ValidateVolumeParam(schema, value)` で ParamKind 別に `VolumeOverrideParamRangeDto` の FloatMin/Max / IntMin/Max / EnumValues を適用し、範囲外は `IsValid=false, ErrorCode="out_of_range_min" | "out_of_range_max" | "invalid_enum"` を返す
  - 観測可能完了: すべての境界値テストが緑で通り、UI 側のインラインバリデーション（Requirement 5.7, 6.7, 9.3）と VolumeOverrideParamFactory の初期値検証の単一情報源が確立される
  - _Requirements: 5.7, 6.7, 9.3_
  - _Boundary: StageLightingVolumeTab.Runtime.Validation_

- [x] 3.5 (P) `StageTabDiagnostics` と診断スナップショット API を `FakeDiagnosticsLogger` を使って実装する
  - `StageTabDiagnosticsTests` で各 Log メソッド（Initialization / CommandSent / EventReceived / AssetLoadFailure / PersistenceFailure）がログチャネルに正しい payload を送ること、`GetSnapshot()` が `ActivePresetName` / `CurrentStageKey` / `LightCount` / `LightsInErrorState` / `VolumeOverridesEnabled` / `PendingAsyncLoads` / `LastPersistenceSaveAt` / `IpcConnected` を同期的に返すことを検証する
  - 実装: ログ経路は `ui-toolkit-shell` の `IDiagnosticsLogger` を再利用し、メイン出力（Display 2+）への描画は構造的に発生しないことをコメントで明示する
  - 観測可能完了: スナップショット API が全フィールドを返し、ログ出力が `ui-toolkit-shell` 側のみに流れることが単体テストで検証される
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8_
  - _Boundary: StageLightingVolumeTab.Runtime.Diagnostics_

---

- [ ] 4. Preview 層（RT アクセサ、カメラアダプタ、パネルコントローラ）を TDD で実装する
- [x] 4.1 `IPreviewRenderTextureAccessor` と `PreviewRenderTextureAccessor`（`StagePreviewHostLocator` 経由）を実装する
  - `PreviewRenderTextureAccessorTests` で Locator に `IPreviewHostService` を登録 → `TryGet` が RT 参照を返す、`Unregister` 後は null を返す、`RenderTextureChanged` イベント購読で RT 差替を検知できることを検証する
  - 実装: Accessor は Locator のイベントを透過し、ライフサイクルで `IsReady` を提供する
  - 観測可能完了: RT 参照解決が同一プロセス内で完結し、メイン出力側 Host 未登録でも UI 側がクラッシュせず「準備中」状態を観測できる
  - _Requirements: 2.1, 2.2, 2.5_
  - _Boundary: StageLightingVolumeTab.Runtime.Preview.Accessor_

- [x] 4.2 `IPreviewCameraAdapter` 抽象と `SceneViewStylePreviewCameraAdapter`（SceneViewStyleCameraController ラップ）を実装する
  - `IPreviewCameraAdapter` に `ResetToDefaultView()` / `IsAvailable` / `OnAvailabilityChanged` を定義し、`FakePreviewCameraAdapter` で ViewModel テストから差替可能にする
  - `SceneViewStylePreviewCameraAdapter` は `StagePreviewHostLocator.Current` 経由で実カメラへ委譲し、Locator 未登録時は `IsAvailable = false`
  - SceneViewStyleCameraController の実際の公開 API 名（`ResetView()` 等）は実装時にパッケージソースを確認し、齟齬があれば Transform 直接操作にフォールバックする
  - 観測可能完了: Fake アダプタで ViewModel テストが書ける状態になり、本番アダプタは Locator 登録後に `ResetToDefaultView()` を呼び出せる
  - _Requirements: 2.4, 2.8, 2.10_
  - _Boundary: StageLightingVolumeTab.Runtime.Preview.CameraAdapter_

- [x] 4.3 `PreviewPanelController` を RT バインド・アクティブ化連動・視点リセットの単体テスト駆動で実装する
  - `PreviewPanelControllerTests` を PlayMode テストとして作成する（`UnityEngine.UIElements.VisualElement` と RenderTexture の実体が必要）
  - `OnActivated` で `preview/command` の `set-enabled:true` event が `FakeIpcClient` に送信されること、`OnDeactivated` で `set-enabled:false` event が送られること、`ResetView` で `reset-view` event が送られることを検証する
  - RT が `RenderTextureChanged` で差し替わったときに VisualElement の `style.backgroundImage`（`Background.FromRenderTexture(rt)`）が更新され、null 時は「プレビュー準備中」プレースホルダ表示になることを検証する
  - 観測可能完了: プレビューパネルのライフサイクル（TabNotActivated → Enabled → Paused → Disposed）がテストで駆動可能になり、Flow 4 の状態遷移が UI 単体で再現できる
  - _Requirements: 2.2, 2.6, 2.7, 2.8, 2.11_
  - _Boundary: StageLightingVolumeTab.Runtime.Preview.PanelController_

---

- [ ] 5. ViewModel（全 UI ロジック集約）を TDD で実装する
- [x] 5.1 `StageLightingVolumeTabViewModel` のライフサイクル（OnActivated / OnDeactivated / Dispose）と購読・Volume Schema 取得・プリセット読込の初期化順序を実装する
  - `ViewModelActivationTests` を先に書き、初回 `OnActivated` で `LightListState.StartSubscribing`・`StageCatalogState.StartSubscribing`・`VolumeSchemaCache.FetchAsync`・`IPresetStorage.LoadAsync` が呼ばれ、`OnStateChanged` が発火し、`OnDeactivated` で購読解除が走ることを検証する
  - IPC 未接続時は Command 送信が `SendError.NotConnected` を返す前提で、`OnActivated` は購読登録のみ行い、接続確立（`IConnectionStatus.OnStatusChanged`）後に Schema Fetch + プリセット復元を開始する分岐を実装する
  - `Dispose` で `JsonPresetStorage.FlushAsync()` が 200 ms タイムアウト付きで呼ばれ、超過時は警告ログのみで進めることを検証する
  - 観測可能完了: Fake IPC / Fake Storage / Fake Clock のみで ViewModel の起動〜終了サイクルがテスト駆動でき、Flow 1 が再現される
  - _Requirements: 1.5, 1.6, 3.1, 4.1, 6.1, 8.4, 8.5, 11.3_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.Lifecycle_

- [x] 5.2 Stage 操作系 Command（`SwitchStage` / `UnloadStage`）と Stage 進行中表示・重複抑止・失敗時状態維持を実装する
  - `ViewModelStageTests` を先に書き、`SwitchStage(key)` で `stage/command`（`op=load`, `AddressableKey=key`）event が送信されること、待機中は `IsSwitchingStage = true` となり重複要求が抑止されること、`stage/loaded` 受信で解除されること、`stage/load-failed` 受信で直前のステージ状態が維持され `OnOperationWarning` が発火することを検証する
  - `UnloadStage` は `stage/command`（`op=unload`）event を送信する
  - 観測可能完了: Flow 3 のステージ切替経路の単体動作と、失敗時の縮退動作が ViewModel テストで検証済み
  - _Requirements: 3.4, 3.5, 3.7, 3.8, 3.11, 9.2_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.Stage_

- [x] 5.3 Light 操作系 Command（`AddLight` / `RemoveLight` / `SelectLight` / `UpdateLightProperty` / `SetLightPropertyDragging`）と 5 秒タイムアウト・連打抑止・プロパティ単位購読を実装する
  - `ViewModelLightTests` を先に書き、`AddLight(initial)` で `light/command`（`op=add`）event が送信され、「追加中」プレースホルダ状態となること、`light/added` 受信で lightId が確定し `light/{lightId}/*` 購読が追加されること、`FakeClock` で 5 秒進めたときタイムアウト警告が発火すること、`RemoveLight(lightId)` で `light/command`（`op=remove`）event + 該当プロパティ購読解除が走ることを検証する
  - `UpdateLightProperty(lightId, property, value)` はバリデータを通過した値のみ `light/{lightId}/{property}` へ `PublishState` し、範囲外は `OnValidationError` 発火 + 送信抑止となることを検証する
  - `SetLightPropertyDragging(lightId, property, true)` 中は該当トピックの受信 state を保持し、false 後にまとめて反映する（操作中逆流抑止）
  - 観測可能完了: Flow 2 が ViewModel 単体で再現でき、Light の CRUD と編集・タイムアウト・逆流抑止が全て検証済み
  - _Requirements: 4.3, 4.4, 4.5, 4.6, 4.7, 4.10, 5.1, 5.2, 5.3, 5.4, 5.7, 5.8, 5.11_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.Light_

- [x] 5.4 Volume 操作系 Command（`SetVolumeOverrideEnabled` / `UpdateVolumeOverrideParam` / `RetryVolumeSchemaFetch`）とスキーマ再試行・逆流追従を実装する
  - `ViewModelVolumeTests` を先に書き、`SetVolumeOverrideEnabled(typeFullName, true)` で `volume/override/{typeFullName}/enabled` へ `PublishState(true)` が送信されること、`UpdateVolumeOverrideParam(typeFullName, paramName, value)` でバリデータ通過後に `volume/override/{typeFullName}/{paramName}` へ `PublishState` が走ること、範囲外は送信抑止 + `OnValidationError` 発火となることを検証する
  - `RetryVolumeSchemaFetch` が `VolumeSchemaCache.FetchAsync` 再試行を呼ぶこと、タイムアウト時は `OnOperationWarning` 発火を検証する
  - 観測可能完了: Volume Override 編集の全 Command が ViewModel 単体で駆動でき、スキーマ欠落時のエラー UI 経路もテスト済み
  - _Requirements: 6.1, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.Volume_

- [x] 5.5 プリセット CRUD（Create / Rename / Duplicate / Delete / Activate）と重複名バリデーション、デバウンス保存結線を実装する
  - `ViewModelPresetCrudTests` を先に書き、`CreatePreset(name)` で空名拒否・重複名拒否（`PresetOpError.DuplicateName`）・成功時に `Presets` コレクション更新 + `DebounceFlusher.Schedule` 呼び出しを検証する
  - `RenamePreset` / `DuplicatePreset` / `DeletePreset` / `ActivatePreset` の各分岐を検証する（`DeletePreset` でアクティブが消えた場合は `ActivePresetName = null` に戻す）
  - プリセット CRUD が発生するたびに `DebounceFlusher.Schedule(() => JsonPresetStorage.FlushAsync(...))` が呼ばれ、`FakeClock` を 500 ms 進めると実際に保存が走ることを検証する
  - 観測可能完了: プリセット CRUD の全操作が in-memory で駆動でき、重複名拒否や自動保存トリガまで単体テストで検証済み
  - _Requirements: 7.1, 7.2, 7.3, 7.5, 7.9, 8.3_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.Preset_

- [x] 5.6 プリセット切替オーケストレーション（Flow 3 のセマンティクス）と部分失敗継続、逆流抑止、プレビュー非阻害を実装する
  - `ViewModelPresetSwitchTests` を先に書き、`ActivatePreset(targetName)` の実行中に以下の固定順序で Command が送信されることを検証する:
    1. 既存有効な Volume Override を `enabled=false` で無効化
    2. ステージを `stage/command`（`op=load` または `op=unload`）で切替
    3. 既存 Light を `light/command`（`op=remove`）で一括削除
    4. プリセットの Light 配列を順に `light/command`（`op=add`）で追加し、`light/added` の lightId を収集
    5. 新 lightId ごとにプロパティ state を送信
    6. プリセットの Volume Override を `enabled=true` + param state で有効化
  - 切替中は `IsSwitchingPreset = true` となり、受信 state は UI に反映せず切替完了後に再同期されることを検証する
  - 個別 Command が `SendResult.Error` / `light/error` / `stage/load-failed` を返した場合は、`OnOperationWarning` 発火 + 診断ログ記録のうえ他 Command は継続されることを検証する（rollback しない）
  - 観測可能完了: プリセット切替の送信順序・部分失敗継続・逆流抑止が ViewModel テストで再現できる
  - _Requirements: 7.4, 7.6, 7.7, 7.8_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.PresetSwitch_

- [x] 5.7 プリセット復元（起動時）・破損フォールバック・ステージ未解決時の未選択化を実装する
  - `ViewModelPresetRestoreTests` を先に書き、`IPresetStorage.LoadAsync` がプリセットを返した場合に `ActivatePreset` のセマンティクスで復元送信されること、`StageAddressableKey` が `stage/catalog` に存在しない場合は未選択化 + `OnOperationWarning("stage_unresolved")` 発火となること、破損ファイル時は `.corrupted-{unixMs}` リネーム後に初回起動扱いにフォールバックすることを検証する
  - 復元中の部分失敗（Light 追加タイムアウト等）は Flow 3 と同じ戦略で個別警告 + 継続する
  - 観測可能完了: 破損ファイル・ステージ欠落・初回起動の全経路が ViewModel テストで駆動でき、Requirement 8.5〜8.11 が満たされる
  - _Requirements: 8.5, 8.6, 8.7, 8.8, 8.11, 9.1_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.PresetRestore_

- [x] 5.8 IPC 切断・接続回復・送信エラーのフェイルセーフを実装する
  - `ViewModelConnectionTests` を先に書き、`IConnectionStatus.IsConnected = false` の間は Command メソッドが `OnOperationWarning("ipc_disconnected")` 発火 + 早期リターンし、UI 側で操作 UI を非活性化できる状態（`IsConnected` プロパティ公開）となることを検証する
  - 接続回復時に `StageCatalogState` / `LightListState` / `VolumeSchemaCache` の再取得 + 全 Volume Override enabled トピックの再購読が走ることを検証する
  - 送信エラー（`SendResult.Error` の `NotConnected` / `PayloadTooLarge` / `InternalError`）は診断ログに記録され、UI クラッシュは発生しないことを検証する
  - 観測可能完了: Requirement 9.5, 9.8, 9.9 の縮退挙動が単体テストで検証済み
  - _Requirements: 9.4, 9.5, 9.8, 9.9, 9.10_
  - _Boundary: StageLightingVolumeTab.Runtime.ViewModel.Connection_

---

- [ ] 6. View（UXML / USS / セクション View / 動的ファクトリ）を UI コンポーネント活用で実装する
- [x] 6.1 タブルート UXML / USS と `StageLightingVolumeTabPanel`（VisualElement ハンドル取得）を実装する
  - `Runtime.Uxml/StageLightingVolumeTab.uxml` を作成し、プレビューパネル・プリセットセクション・ステージ選択セクション・Light 一覧＋編集セクション・Volume Override セクションのコンテナを配置する
  - `Runtime.Uxml/StageLightingVolumeTab.uss` を作成し、`vsb-slv-` プレフィクス + BEM で USS セレクタを定義する（スキン差替 UI-3 対応）
  - `StageLightingVolumeTabPanel` は `UIDocument` から本タブルートを `Q<VisualElement>` で取得し、各セクション View に VisualElement を受け渡すだけの薄い層として実装する
  - `UiToolkitShellSkinProfile` 経由の UXML 差し替え時、欠落した必須要素（`preview-panel`, `preset-section` 等）は `UxmlImportValidator` が診断ログに記録する
  - 観測可能完了: プリロード時に UXML が一度だけ clone され、タブ切替時は `display` / `visible` 切替のみで再 clone されないことが PlayMode で観測できる
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.8_
  - _Boundary: StageLightingVolumeTab.Runtime.View.Panel_

- [x] 6.2 (P) `StagePresetSectionView`（プリセット CRUD UI）と `StageSelectionSectionView`（ステージ選択 + サムネイル）を実装する
  - `StagePresetSectionView`: プリセット一覧表示（`VsbNumberedList` 相当）、新規作成・名前変更・複製・削除・アクティブ化の操作 UI、アクティブプリセット名の明示表示を組み、ViewModel の Command メソッドに結線する
  - `StageSelectionSectionView`: `StageCatalogState` のカタログを一覧表示し、各項目に `DisplayName` を表示、`ThumbnailAddressableKey` があれば `IAsyncAssetLoader` で非同期取得、失敗時はデフォルト画像にフォールバックする
  - 切替中は進行中表示（スピナー/オーバーレイ）を提示、切替失敗時は `OnOperationWarning` を受けてエラーバッジを表示する
  - 観測可能完了: プリセット CRUD のボタン群が ViewModel を呼び出し、ステージ候補一覧が UI に表示され、サムネイル失敗時はデフォルト画像に置き換わる
  - _Requirements: 3.2, 3.3, 3.7, 3.8, 3.9, 3.10, 7.2, 7.3, 9.6_
  - _Boundary: StageLightingVolumeTab.Runtime.View.PresetAndStageSections_

- [x] 6.3 (P) `LightListSectionView`（Light 一覧 + 追加削除 + 追加中プレースホルダ）を実装する
  - 一覧は `LightListState.CurrentList` を lightId 採番順で安定表示し、`LightListChangeEvent` で差分反映する
  - `Add Light` ボタンは ViewModel に `AddLight(初期値)` を要求、送信中はボタンを一時無効化して連打を抑止、「追加中」プレースホルダ行を挿入する
  - タイムアウト（5 秒、ViewModel 発火）時にプレースホルダをエラー表示に切り替え、再試行ボタンを露出する
  - Light 一覧 state を未受信の間は「接続待ち」プレースホルダを表示し、Light 操作を非活性化する
  - 観測可能完了: 追加 → lightId 発行 → 一覧反映 → 削除のサイクルが UI 操作で駆動でき、タイムアウト時も UI がクラッシュせず再試行可能な状態に戻る
  - _Requirements: 4.2, 4.6, 4.7, 4.9, 4.10_
  - _Boundary: StageLightingVolumeTab.Runtime.View.LightListSection_

- [x] 6.4 (P) `LightPropertyEditorView`（選択 Light のプロパティ編集）を `VsbSlider` / `VsbColorPicker` / `VsbToggleGroup` で実装する
  - 選択時に Light の Type / 角度 / 色 / 強さ / Range / Spot Angle を現在 state で初期化し、変更時は ViewModel の `UpdateLightProperty` を呼ぶ
  - Type（Directional / Point / Spot / Area）変更に応じて派生プロパティの活性/非活性を切り替える（例: Directional は Range / Spot Angle 非活性、Spot のみ Spot Angle 活性）
  - コントロール近傍に `LightPropertyValidator` 由来のインラインバリデーションエラーを表示する
  - ドラッグ開始/終了を `SetLightPropertyDragging` で ViewModel に通知し、ドラッグ中の逆流 state 抑止を有効化する
  - 観測可能完了: スライダーのドラッグで `light/{lightId}/intensity` の `PublishState` 送信が観測でき、Type 切替に応じた UI 活性/非活性が即座に反映される
  - _Requirements: 5.1, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.10, 5.11, 9.3_
  - _Boundary: StageLightingVolumeTab.Runtime.View.LightPropertyEditor_

- [x] 6.5 (P) `VolumeOverrideSectionView` と `VolumeOverrideParamFactory`（ParamKind → VisualElement 動的生成）を実装する
  - `VolumeOverrideParamFactoryTests` を先に書き、`ParamKind.Bool → Toggle`、`Int/Float/ClampedFloat → VsbSlider`、`Color → VsbColorPicker`、`Vector2/3/4 → 各軸 VsbSlider 組み合わせ`、`Enum → VsbToggleGroup または DropdownField`、`Unknown → null 返却 + 診断ログ` の 6 分岐を検証する
  - `VolumeOverrideSectionView` は `VolumeSchemaCache` から取得した `VolumeOverrideSchemaDto` を元に Override ごとに enabled トグル + param コントロール群を動的配置する
  - 各 param の変更時に `VolumeOverrideParamValueDto` を構築し ViewModel の `UpdateVolumeOverrideParam` を呼ぶ。スキーマ取得失敗時は再試行ボタンを露出する
  - 観測可能完了: URP の Bloom / Tonemapping / ColorAdjustments が動的 UI として出現し、利用者プロジェクトが独自 VolumeComponent を追加しても UI に自動反映される（`Unknown` 型の param のみスキップ + ログ）
  - _Requirements: 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9, 6.10, 6.11_
  - _Boundary: StageLightingVolumeTab.Runtime.View.VolumeOverrideSection_

---

- [ ] 7. Integration: Bootstrapper によるタブ統合・ライフサイクル結線・エディタ検証を実装する
- [ ] 7.1 `StageLightingVolumeTabBootstrapper` で `RegisterTab`・DI 構築・Lifecycle 結線・Dispose 時 Flush を実装する
  - `ITabPanelRegistry.RegisterTab(TabId.StageLighting, metadata)` を起動時に呼び、返却された `ITabLifecycleHandle` の `OnActivated` / `OnDeactivated` / `OnDisposed` に ViewModel / PreviewController / JsonPresetStorage.Flush を結線する
  - DI 注入は コンストラクタで `IUiCommandClient` / `IUiSubscriptionClient` / `IAsyncAssetLoader` / `IConnectionStatus` / `IDiagnosticsLogger` / `IPresetStorage` / `IPreviewRenderTextureAccessor` / `IPreviewCameraAdapter` / `IClock` / `UIDocument` を受け取る
  - `Dispose` は冪等とし、200 ms タイムアウト付きで `JsonPresetStorage.FlushAsync()` を同期待機、超過時は警告ログのみで続行する
  - 二重 `RegisterTab` 呼び出し時は診断ログ + no-op で安全に抜ける
  - 観測可能完了: 利用者プロジェクトの `UiShellBootstrapper` 拡張点で本 Bootstrapper を new するだけでタブが登録され、PlayMode 開始 → 停止で購読・RT 参照・永続化が全てクリーンアップされる
  - _Depends: 5.1, 4.3_
  - _Requirements: 1.3, 1.4, 1.5, 1.7, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7_
  - _Boundary: StageLightingVolumeTab.Runtime.Bootstrap_

- [ ] 7.2 Editor 側 `UxmlImportValidator` と PlayMode 手動検証シーン（`StageLightingVolumeTabPlayModeSample.unity`）を整備する
  - `Editor/UxmlImportValidator.cs` は利用者プロジェクトが UXML を差し替えた際に必須要素（`preview-panel`, `preset-section`, `stage-selection-section`, `light-list-section`, `light-editor-section`, `volume-override-section`）の欠落を検出し、診断ログへ記録する
  - `Tests/PlayMode/StageLightingVolumeTabPlayModeSample.unity` にモック `StagePreviewHost` + モック `IUiCommandClient/IUiSubscriptionClient`（事前応答配信）+ モック Stage Catalog + モック Volume Schema を配置し、本タブの全表示・全操作を手動検証可能にする
  - サンプルシーンの README（コメント）に、どのモックがどの IPC 契約を模擬するかを明記する
  - 観測可能完了: Unity Editor で当該シーンを開き Play すると、ステージ候補・Light CRUD・Volume Override 編集・プレビューパネルが全て触れる状態で起動する
  - _Depends: 7.1_
  - _Requirements: 1.8, 12.4, 12.6, 12.7_
  - _Boundary: StageLightingVolumeTab.Editor + Tests.PlayMode_

---

- [ ] 8. Validation: 自己ループ IPC・再接続・タブライフサイクル・プリセット完全サイクルの統合テストを整備する
- [ ] 8.1 自己ループ IPC 統合テスト（`SelfLoopIpcTest`）を PlayMode で実装する
  - `core-ipc-foundation` の `InMemoryLoopbackTransport` を用いて、ViewModel の Command 送信 → 仮想メイン出力ハンドラ → state/event 応答 → ViewModel 反映の往復を検証する
  - 検証ケース: ステージ候補 UI の反映、ステージ切替 event 送信、Light 追加 event 送信 + `light/added` 反映、Light プロパティ state 送信、Light 削除 event 送信、Volume Override enabled 切替、Volume param state 送信
  - 観測可能完了: `SelfLoopIpcTest` が緑で通り、メイン出力側アダプタ未実装の段階でも本 spec の IPC 契約が全経路で閉じていることが保証される
  - _Depends: 7.1_
  - _Requirements: 12.2, 12.5_
  - _Boundary: StageLightingVolumeTab.Tests.PlayMode.SelfLoop_

- [ ] 8.2 プリセット完全サイクル統合テスト（`CompleteWorkflowTest` / `PresetApplyIntegrationTest`）を実装する
  - プリセット作成 → Light 追加 → Volume 編集 → プリセット保存（デバウンス経過）→ アプリ再起動相当（新しい ViewModel + 新しい JsonPresetStorage.LoadAsync）→ 復元の完全サイクルを実行する
  - プリセット切替時の送信順序（Flow 3 固定順序）を検証し、一部 Command が失敗 event を返したときも継続することを確認する
  - ステージ未解決時の未選択化 + 警告バッジ動作を結合レベルで検証する
  - 観測可能完了: 永続化ファイルを経由した完全サイクルが緑で通り、Requirement 7 / 8 が結合レベルで成立する
  - _Depends: 8.1_
  - _Requirements: 7.4, 7.6, 7.7, 8.5, 8.8, 8.11, 12.5_
  - _Boundary: StageLightingVolumeTab.Tests.PlayMode.Workflow_

- [ ] 8.3 再接続・タブライフサイクルの統合テスト（`ReconnectIntegrationTest` / `TabSwitchLifecycleTest`）を実装する
  - `FakeConnectionStatus` を切断 → UI 非活性化を観測 → 復帰 → 全 state/event 再取得と再購読が走ることを検証する
  - プリロード → 初回アクティブ化 → 非アクティブ化 → 再アクティブ化 → Dispose のサイクルを繰り返し、購読リーク（`IUiSubscriptionClient` 登録数が増え続けない）・RT 参照リーク（`RenderTexture` が GC 可能）・永続化ファイルロック残存がないことを検証する
  - 観測可能完了: 両テストが緑で通り、Requirement 9.8 / 9.9 / 11.4 が結合レベルで担保される
  - _Depends: 8.1_
  - _Requirements: 9.8, 9.9, 11.4, 12.5_
  - _Boundary: StageLightingVolumeTab.Tests.PlayMode.Lifecycle_

- [ ] 8.4 不可用ステージ・IPC 切断中フェイルセーフ・引きカメラ非アクティブ停止の統合テストを実装する
  - 保存プリセットのステージ key が `stage/catalog` に無い場合、UI がステージ未選択 + 警告バッジ表示となり他 UI（Light, Volume, プレビュー）は動作継続することを検証する
  - IPC 切断中に全操作 UI が非活性化されること、メイン出力側（Display 2+）にエラー UI が描画されていないことを検証する
  - タブ非アクティブ化時に `preview/command`（`set-enabled:false`）が送信され、再アクティブ化時に `set-enabled:true` が送信されることを検証する
  - 観測可能完了: テストが緑で通り、Requirement 9.1 / 9.7 / 2.6 / 2.7 / 12.5 が結合レベルで検証される
  - _Depends: 8.1_
  - _Requirements: 2.6, 2.7, 9.1, 9.7, 12.5_
  - _Boundary: StageLightingVolumeTab.Tests.PlayMode.FailureScenarios_

- [ ]* 8.5 パフォーマンス検証テスト（Light 50/100 個スケーラビリティ、Volume param ドラッグ応答、プリセット切替レイテンシ）を任意で追加する
  - `LightCountScalabilityTest`: Light 50 / 100 個状態での一括購読登録が 1 フレームあたり 10 件上限で分割処理され、UI がブロックされないことを検証する（Requirement 4.8 と整合）
  - `VolumeParamDragResponsivenessTest`: スライダー連続ドラッグ（60 Hz 模擬）で coalesce が効き UI フレームドロップが発生しないことを検証する（Requirement 5.5, 6.6 と整合）
  - `PresetSwitchLatencyTest`: プリセット切替の所要時間（中間フレームが「真っ暗」にならない）を測定する（Requirement 7.4, 7.7 と整合）
  - MVP 段階ではオプショナル（Light 上限なし SL-14 方針のため、運用側の fps 監視に委ねる前提）
  - _Requirements: 4.8, 5.5, 6.6, 7.4, 7.7_
  - _Boundary: StageLightingVolumeTab.Tests.PlayMode.Performance_
