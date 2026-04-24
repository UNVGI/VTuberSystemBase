# Implementation Plan

> 本計画は design.md の Hexagonal + State Machine 構成（Abstractions / Domain / Runtime / Editor / Tests の 5 asmdef 分割）を前提に、Foundation → Core → Integration → Validation の順で積み上げる。TDD を基本とし、各 Adapter/Domain モジュールは Fake 対応のテストを先行整備して実装を駆動する。

## 1. Foundation: パッケージ骨格と共有抽象の整備

- [ ] 1.1 パッケージ骨格と asmdef 参照方向の確立
  - `jp.hidano.vtuber-system-base.camera-switcher-tab` UPM パッケージ（package.json）を新規作成し、Runtime/Editor/Tests/Samples~/UxmlUss のディレクトリ骨格を配置する。
  - Abstractions / Domain / Runtime / Editor / Tests.Runtime / Tests.Editor の 6 つの asmdef を追加し、`Abstractions → Domain → Runtime → Editor` の一方向参照、および Tests が Runtime/Domain/Abstractions を参照する関係を precompiledReferences / versionDefines 含め成立させる。
  - Runtime asmdef から `ui-toolkit-shell` 公開 API、`core-ipc-foundation` 抽象、UCAPI4Unity、hecomi/uOSC、SceneViewStyleCameraController のみを参照し、`core-ipc-foundation` 具体実装 / `output-renderer-shell` / 他タブ asmdef は参照しないことを `.asmdef` ファイルで強制する。
  - 観測可能な完了状態: Unity プロジェクトで Compile が通り、`output-renderer-shell` と他タブ asmdef への参照が存在しない状態で Assembly Definition Inspector から確認できる。
  - _Requirements: 1.7, 13.1, 13.2, 13.5, 13.6_

- [ ] 1.2 IPC トピック定数と Payload DTO の整備
  - `CameraIpcTopics` を単一ソースとして定義し、`camera/command` / `cameras/list` / `cameras/active` / `camera/created` / `camera/error` / `camera/{id}/metadata/{key}` / `camera/{id}/volume/*` / `camera/preset/*` / `camera/preview/*` / `camera/{id}/preview/handle` のアドレスを string 定数として集約する。
  - `CameraCommandPayload` / `CamerasListPayload` / `CameraListEntry` / `CameraDefaultTransform` / `CameraCreatedEventPayload` / `CameraErrorEventPayload` / `VolumeCommandPayload` / `VolumeOverrideState` / `VolumeMetadataRequest` / `VolumeMetadataResponse` / `VolumeOverrideSchema` / `VolumeParamSchema` / `PresetCommandPayload` / `PresetListState` / `PresetActiveState` / `PreviewCommandPayload` / `PreviewHandleState` を Abstractions に readonly struct として実装する。
  - すべての DTO に対して System.Text.Json ラウンドトリップ単体テストを追加し、optional フィールド（null 相当）を含めて既定値挙動が破壊的でないことを確認する。
  - 観測可能な完了状態: Tests.Runtime の DTO ラウンドトリップテストがグリーンで通り、Topic 定数の参照箇所が Abstractions asmdef 内に閉じている。
  - _Requirements: 1.7, 5.1, 5.2, 5.9, 6.1, 6.3, 6.4, 6.5, 6.6, 6.11, 6.12, 7.3, 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.9, 11.1a, 11.1b_

- [ ] 1.3 値型・ドメインモデル・Result 型の定義
  - `CameraId`（string ラッパ、null/空文字ガード、許容文字 `[A-Za-z0-9_-]`）、`CameraType`、`CameraSnapshot`、`UcapiFlatRecord`（138 byte Blob を保持する readonly struct）、`VolumeOverrideSchema`、`PresetPayload`、`CameraMetadata`、`VolumeConfig`、`VolumeOverride` を Abstractions に定義する。
  - `SerializeResult` / `OscEmitResult` / `OscEmitFailure`（Kind / Detail / Inner）、`PresetIoResult` を discriminated union 相当の readonly struct として実装し、SerializeFailureReason / OscFailureKind / PresetIoFailureKind を enum で提供する。
  - `CameraId` の不変条件違反に対する例外フロー、Result 型が成功/失敗双方で null 参照を返さないガードを単体テストでカバーする。
  - 観測可能な完了状態: 値型のガード違反と Result の両状態が Tests.Runtime のユニットテストでグリーンとなり、すべての Port インタフェースがこれらの型だけで I/O 可能な状態になる。
  - _Requirements: 3.1, 3.3, 3.4, 3.8, 6.4, 6.9, 11.1, 11.2_

- [ ] 1.4 Port 抽象（Serializer / Osc / Preset / Time / Preview）の定義
  - `IUcapiFlatRecordSerializer.Serialize(in CameraSnapshot) → SerializeResult`、`IUcapiOscEmitter`（State / StartAsync / StopAsync / Send / OnSendFailure / Dispose）、`IPresetStore`（LoadAllAsync / SaveAllAsync）、`ITimeProvider`（UtcNow / MonotonicSeconds / CreateDebounce）、`IPreviewHandleResolver`（ResolveAsync / Release）を Abstractions に定義する。
  - 各 Port の pre/post condition（null 禁止、StartAsync 前の Send 禁止、Stop 後の Start 再許容、Release 後の Resolve 再利用可能性）を XML Doc コメントと境界テストで表現する。
  - `ICameraSwitcherCoordinator` のコントラクト（TabStatus / EditingCameraId / ActiveCameraId / Request* / Set* / OnTabActivated / FrameTick / OnStateChanged）も Domain asmdef に先行配置する。
  - 観測可能な完了状態: Port 抽象のみを参照するモックテストが Tests.Runtime でコンパイル可能になり、実装抜きで契約レベルの単体テストが書ける状態になる。
  - _Requirements: 1.7, 3.7, 3.8, 4.1, 4.6, 4.7, 4.8, 10.1, 10.2, 10.3, 11.10, 15.4, 15.7, 15.8_

- [ ] 1.5 Fake Adapter とテスト支援ユーティリティの整備
  - `FakeIpcClient` / `FakeIpcSubscription`（ui-toolkit-shell の `IUiCommandClient` / `IUiSubscriptionClient` を模擬）、`FakeOscEmitter`（送信 blob バッファ + OnSendFailure 手動発火）、`FakePresetStore`（InMemory）、`FakeTimeProvider`（手動前進）、`FakePreviewHandleResolver`、`FakeConnectionStatus` を Tests.Runtime 配下に実装する。
  - `FakeOscEmitter` は Address 文字列 / Blob 長 / cameraId 抽出を検証しやすい形で積み、`FakeTimeProvider.Advance(TimeSpan)` で登録済みタイマーを同期的に発火する。
  - 共通テストユーティリティ（AssertEnvelope / AssertTopic / PayloadFactory）を追加し、後続の Domain テストが定型コードなしで記述できるようにする。
  - 観測可能な完了状態: 上記 Fake を用いたスモークテスト（空のシナリオ実行と時刻前進）が Tests.Runtime でグリーンになる。
  - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.7, 15.8_

## 2. Core: Domain 状態機械と送受信ロジックの実装

- [ ] 2.1 (P) CameraRegistry と ActiveCameraTracker の実装
  - cameraId → `CameraMetadata` の辞書と追加順序配列を保持する `CameraRegistry` を実装し、`Upsert` / `Remove` / `Enumerate` / `TryGet` の不変条件（採番順維持、重複排除）を単体テストで固定する。
  - `ActiveCameraTracker` で `ActiveCameraId` と `EditingCameraId` を独立に管理し、サーバ権威側 `cameras/active` state 受信時の更新、編集対象切替イベントの発火、active と editing の非同期差分検出を単体テストでカバーする。
  - 観測可能な完了状態: 1,000 件の Upsert → 並び順固定 / 削除時のギャップ不発生 / active ≠ editing のシナリオがすべて Tests.Runtime でグリーンになる。
  - _Requirements: 2.8, 2.9, 6.1, 6.4, 6.9, 7.2, 7.7_
  - _Boundary: CameraRegistry, ActiveCameraTracker_

- [ ] 2.2 (P) TimeoutTracker と FailureAggregator の実装
  - `TimeoutTracker.Arm(clientRequestId, timeout)` / `Cancel` / `OnTimeout` を `ITimeProvider` に依存した形で実装し、5 秒タイムアウト発火・Cancel 後の非発火・重複 Arm のガードを単体テストで確認する。
  - `FailureAggregator` に `OscFailure` / `IpcSendFailure` / `VolumeMetadataFailure` / `PresetIoFailure` / `CameraError` の Kind を定義し、件数集計 + 直近 N 件の履歴保持 + Subscribe API を備える。診断スナップショット API を通じて Kind 別件数が取得できること。
  - 観測可能な完了状態: 5 秒タイムアウト発火、Cancel による抑止、FailureAggregator の Kind 別カウントが Tests.Runtime のテストでグリーンになる。
  - _Requirements: 6.7, 6.8, 11.9, 12.1, 12.2, 12.3, 12.9, 12.10, 14.5, 14.6, 14.9_
  - _Boundary: TimeoutTracker, FailureAggregator_

- [ ] 2.3 (P) UCAPI Flat Record シリアライザ Adapter の実装
  - `Ucapi4UnityFlatRecordSerializer` を UCAPI4Unity 公開 API を薄くラップする形で実装し、`CameraSnapshot` → 138 byte blob（10 byte header + 128 byte record）に変換する。`UnityCameraSnapshotCapture` で `UnityEngine.Camera` → `CameraSnapshot` の抽出を担当させる。
  - Quaternion → rotation matrix 変換、focalLength / sensorSize / near-far clip / timecode の単位換算、NaN/Inf / focalLength ≤ 0 / sensorSize ≤ 0 の sanitize を行い、失敗時は `SerializeResult.Invalid(Reason)` を返す。
  - UCAPI API 変更が本 adapter の差分のみで吸収できるよう、変換ロジックを per-field に分離して Tests.Runtime で NaN/Inf / 境界値 / 正常値の 3 系を検証する。
  - 観測可能な完了状態: NaN/Inf を含むスナップショットで `Invalid` が返り、正常値では 138 byte の blob が CRC 含めて生成される単体テストがグリーンになる（CRC は UCAPI4Unity 側への委譲確認）。
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_
  - _Boundary: Ucapi4UnityFlatRecordSerializer, UnityCameraSnapshotCapture_

- [ ] 2.4 (P) uOSC ベースの OSC 送信 Adapter の実装
  - `OscAddressBuilder` で `/ucapi/camera/{cameraId}/flat` プレフィクス組立（プレフィクスは設定で上書き可能）を実装し、`UoscFlatRecordEmitter` が `uOscClient` を動的 GameObject に attach して `Send(address, blob)` を呼ぶ構造を整える。
  - `OscClientLifecycle` で host/port の設定ファイル読込 + デフォルトフォールバック（`127.0.0.1:57300`）、`StartAsync` / `StopAsync`、`uOscClient.onErrorInSend` のコールバックを `FailureAggregator` に橋渡しする。ポート占有・LibraryNotAvailable・SocketError を `OscFailureKind` に分類。
  - uOSC client と server を同プロセス内で立てるループバック統合テストで 138 byte blob がアドレス通りに受信できることを検証する。
  - 観測可能な完了状態: ループバック統合テストで 1 秒間の連続送信が損失なしに受信され、StopAsync 後にソケットが解放されていることを確認できる。
  - _Requirements: 4.1, 4.2, 4.3, 4.7, 4.8, 4.9, 4.13, 10.1, 10.8_
  - _Boundary: UoscFlatRecordEmitter, OscClientLifecycle, OscAddressBuilder_

- [ ] 2.5 (P) FileSystemPresetStore の実装
  - `Application.persistentDataPath/camera-switcher-presets.json` を UTF-8 + 整形で読み書きし、atomic write（tmp → rename）、`schemaVersion: 1` 付与、破損検知時の `*.bak.{unixMs}` リネームを実装する。
  - `LoadAllAsync` は不在時に空の `PresetCollection`、破損時は `.bak` リネーム後に空コレクションを返す。`SaveAllAsync` は IOException 捕捉で `PresetIoFailureKind.WriteFailed` を返す。
  - 実ファイル I/O を伴う Tests.Runtime テストで「新規保存 → 再読込で完全一致」「意図的に破損した JSON → `.bak` 作成 + 空コレクション返却」「読取専用ディレクトリ → WriteFailed」を検証する。
  - 観測可能な完了状態: 上記 3 シナリオのテストがグリーンになり、persistentDataPath に残るテストファイルが teardown で削除される。
  - _Requirements: 11.1, 11.2, 11.7, 11.9, 11.10_
  - _Boundary: FileSystemPresetStore_

- [ ] 2.6 OscStreamController の実装
  - `SetTarget(CameraId?)` / `OnCameraDeleted(CameraId)` / `FrameTick()` を実装し、target=null 時は送信せず、FrameTick 1 回あたり 0 または 1 回の `IUcapiOscEmitter.Send` 発行にする（1 フレーム 1 メッセージ契約）。
  - Serialize 失敗時は送信 skip + `FailureAggregator.OscFailure` 記録、delete 連動停止、編集対象切替で即時に送信先切替、例外は上位に伝播させない。
  - FakeOscEmitter + FakeSerializer を用いた単体テストで「SetTarget → FrameTick 複数回 → 送信 blob 数 = FrameTick 回数」「SetTarget(null) で停止」「OnCameraDeleted(current) で null へ戻る」「Serialize Invalid で送信 0 件 + FailureAggregator に記録」を確認する。
  - 観測可能な完了状態: 上記テストがグリーンになり、1,000 tick の連続駆動でメモリ・ハンドルがリークしないことを測定できる。
  - _Requirements: 2.5, 3.5, 3.6, 4.4, 4.5, 4.10, 4.11, 4.12_
  - _Depends: 2.1, 2.2, 2.3, 2.4_
  - _Boundary: OscStreamController_

- [ ] 2.7 VolumeUiStateManager の実装
  - `OnEditTargetChanged(CameraId)` でメタデータ Request を発行してスキーマをキャッシュ、Request 失敗時はカメラ単位でエラー状態を保持して本タブ全体を落とさない。
  - `IsUserDragging(overrideType, param)` フラグを保持し、drag 中は受信 state echo を UI 反映キューに乗せない（drag 終了で最新値に追従）。`camera/{id}/volume/overrides` state と `camera/{id}/volume/override/{type}/{param}` state の双方向受信経路を実装する。
  - FakeIpcClient + FakeIpcSubscription を用いて「メタデータ取得成功 → UI binding 要求」「タイムアウト → エラー状態 + 本タブ継続」「drag 中 echo 抑止 → drag 解除後に最新値反映」を検証する。
  - 観測可能な完了状態: 3 シナリオが Tests.Runtime でグリーンになり、他カメラの Volume 編集が Request 失敗の影響を受けないことが確認できる。
  - _Requirements: 8.1, 8.2, 8.10, 8.11, 8.13, 9.3, 12.2_
  - _Depends: 2.2_
  - _Boundary: VolumeUiStateManager_

- [ ] 2.8 PresetController の実装
  - `PresetPayload { Cameras, VolumeConfigs, ActiveCameraId }` モデルを保持し、CRUD（create/rename/duplicate/delete/activate）API、`NotifyStateMutation` による 500 ms デバウンスフラッシュ、`FlushPendingAsync`、`RestoreOnStartAsync` を実装する。
  - 切替時は現状と target の差分を計算し、**delete → add → metadata → volume → active-set** の順で `IUiCommandClient` にディスパッチ。`camera/created` の clientRequestId 受信で論理 ID を新 cameraId にマッピングする。
  - 重複名拒否、切替直列化（SemaphoreSlim 相当）、部分失敗の継続、`Application.quitting` 相当での強制フラッシュ、破損フォールバック時の初回起動挙動、起動時ファイル不在時のスキップを単体テストでカバーする。
  - 観測可能な完了状態: FakeTimeProvider 500 ms 前進で `FakePresetStore.SaveCalls` が 1 回、切替時の送信順序が上記固定、重複名で作成拒否、破損時に `.bak` 後に復元スキップ、のすべてが Tests.Runtime でグリーンになる。
  - _Requirements: 11.1, 11.1a, 11.1b, 11.1c, 11.1d, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 11.9, 11.10, 11.11, 12.9_
  - _Depends: 2.1, 2.2, 2.5_
  - _Boundary: PresetController_

- [ ] 2.9 PreviewSubscriptionController の実装
  - タブアクティブ化時に `camera/preview/command { op: "attach", cameraIds, size, fps }` を送信し、`camera/{id}/preview/handle` state 受信で `IPreviewHandleResolver.ResolveAsync` を呼んで RenderTexture を取得する。
  - タブ非アクティブ化・カメラ削除・編集対象切替の各ケースで `detach` を送り、対応する handle を Release する。解決失敗時はプレースホルダ表示情報を提示する state を外部に公開する。
  - Fake resolver と FakeIpcClient で「attach → handle 受信 → Texture 取得」「detach で Release 呼出し」「handle 未受信でプレースホルダ状態」を単体テストでカバーする。
  - 観測可能な完了状態: テストシナリオ 3 件がグリーンで、非アクティブ時に attach cameraIds が空になることを確認できる。
  - _Requirements: 2.2, 2.3, 2.7, 2.8, 2.11_
  - _Depends: 2.1_
  - _Boundary: PreviewSubscriptionController_

- [ ] 2.10 CameraSwitcherCoordinator 状態機械の実装
  - Coordinator に `TabStatus { Initializing / ConnectionPending / Ready / Suspended / Disposing }` と、全 port への委譲、`IConnectionStatus` 観察（Disconnected ↔ Connected 時の再購読 + CRUD 非活性化）、`camera/error` の FailureAggregator 転送、`OnTabActivated/Deactivated/Dispose` 経路を実装する。
  - UI 起点 API（RequestAddCamera / RequestDeleteCamera / ActivateCamera / SelectEditTarget / UpdateCameraMetadata / AddVolumeOverride / RemoveVolumeOverride / SetVolumeOverrideEnabled / SetVolumeOverrideParam / SetVolumeEnabled / CreatePreset 等）を同期受付 + 非同期送信ディスパッチで実装し、例外を送出しない（失敗は FailureAggregator 経由）。
  - clientRequestId (GUID) 採番と TimeoutTracker Arm、`camera/created` 受信での Cancel + Registry upsert、タイムアウト時のプレースホルダ `Failed` 遷移、0 台時の活性化抑止ヒント状態、切替進行中表示の直列化を単体テストで検証する。
  - 観測可能な完了状態: Coordinator が Fake port だけで PlayMode 非依存にテスト可能となり、Initialize → Ready → Suspend → Dispose のライフサイクル遷移と、切断中 CRUD 非活性 → 復帰時の再購読 + snapshot 再取得が Tests.Runtime でグリーンになる。
  - _Requirements: 2.5, 2.9, 6.3, 6.5, 6.6, 6.7, 6.8, 6.10, 6.11, 6.12, 7.1, 7.3, 7.4, 7.5, 7.6, 7.7, 7.9, 8.3, 8.4, 8.5, 8.6, 8.9, 9.1, 11.1a, 11.1d, 12.3, 12.4, 12.7, 12.8, 14.1, 14.2, 14.3_
  - _Depends: 2.1, 2.2, 2.6, 2.7, 2.8, 2.9_
  - _Boundary: CameraSwitcherCoordinator_

## 3. Integration: Unity 層・View・ライフサイクルの組み上げ

- [ ] 3.1 (P) UXML / USS アセットの作成
  - `CameraSwitcherTab.uxml` と `CameraSwitcherTab.uss`（vsb- プレフィクス + BEM 風クラス命名）、`PreviewPanel.uxml/.uss`、`CameraCard.uxml/.uss`、`VolumeOverrideItem.uxml/.uss`、`PresetRow.uxml/.uss`、`DiagnosticsBadge.uxml/.uss` を新規作成する。
  - マルチプレビュー + 大アクティブプレビューの二層レイアウト、カメラリスト（追加/削除/選択/アクティブ切替）、Local Volume 編集領域（Override アイテムコンテナ）、プリセット CRUD 領域、診断バッジの配置を構造的に表現する。
  - Editor PlayMode で UIDocument をアタッチし、ui-toolkit-shell の `CameraSwitcherTabVisualTreeAsset` スロットに手動で差し込んだ状態で USS が適用されていることを目視確認する。
  - 観測可能な完了状態: UXML/USS がビルドエラーなく読み込まれ、Editor 上で各パネルの空枠が表示される。
  - _Requirements: 1.1, 1.2, 1.8_
  - _Boundary: UxmlUss_

- [ ] 3.2 (P) SceneViewStyleCameraControllerWrapper と EditingCameraTickDriver の実装
  - `SceneViewStyleCameraControllerWrapper` でパッケージ本体を薄くラップし、`Enable()` / `Disable()` によりマウスキャプチャと回転/パン/ズームを制御。非アクティブ化時に `Disable()` で入力を解除する。
  - `EditingCameraTickDriver` を MonoBehaviour として実装し、`LateUpdate()` 内で `Application.isPlaying && ITabLifecycleHandle.IsActive` を満たす場合のみ `OscStreamController.FrameTick()` を駆動する。
  - PlayMode テストでマウスイベント相当の入力を注入して RenderTexture が更新されること、タブ非アクティブ化で Tick が停止することを観測する。
  - 観測可能な完了状態: PlayMode テストで 1 秒間の入力注入に対して RenderTexture のフレームが更新され、タブ非アクティブ時に OSC 送信カウントが増えないことが確認できる。
  - _Requirements: 2.1, 2.4, 2.6, 2.7, 2.8, 3.6, 4.4, 4.5_
  - _Boundary: SceneViewStyleCameraControllerWrapper, EditingCameraTickDriver_

- [ ] 3.3 View レイヤの実装と ViewBinder への結線
  - `CameraSwitcherViewBinder`（Coordinator の `OnStateChanged` 購読）、`PreviewPanelController`（マルチ + アクティブプレビュー、サムネイル fps 設定反映）、`CameraListView`（`VsbNumberedList` 利用、selected/editing/active USS クラス、0 台時 empty-state CTA）を実装する。
  - `LocalVolumeEditorView`（VolumeMetadataResponse スキーマから `VsbSlider` / `VsbColorPicker` / `VsbToggleGroup` を動的生成、drag 中 echo 抑止、min/max clip、編集対象切替時の再構成）、`PresetPanelView`（CRUD + アクティブ表示 + 重複名即時赤字）、`DiagnosticsBadgeView`（OSC/IPC 状態バッジ + 直近エラー件数）を実装する。
  - Unity PlayMode 最小シーンで Coordinator と View が Fake port 経由でエンドツーエンドに動作することを観測する。
  - 観測可能な完了状態: PlayMode でカメラ追加 → カードが追加 → 選択 → Volume UI 再構成 → プリセット作成・切替・重複名拒否が UI 上で操作可能になる。
  - _Requirements: 1.4, 2.2, 2.3, 2.10, 6.2, 6.7, 7.1, 7.6, 7.9, 8.7, 8.8, 8.10, 9.6, 11.1a, 11.1b, 11.1d, 14.9_
  - _Depends: 2.10, 3.1_
  - _Boundary: Views (ViewBinder, PreviewPanelController, CameraListView, LocalVolumeEditorView, PresetPanelView, DiagnosticsBadgeView)_

- [ ] 3.4 Composition Root と ITabLifecycleHandle ブリッジの実装
  - `CameraSwitcherTabBehaviour` を MonoBehaviour として実装し、`UiShellBootstrapper` 経由で `ITabPanelRegistry.RegisterTab(TabId.CameraSwitcher)` を呼んで `ITabLifecycleHandle` を取得、`OnActivated` / `OnDeactivated` / `OnDisposed` を Coordinator の `OnTabActivated` / `OnTabDeactivated` / `Dispose` に橋渡しする。
  - DI 経由で全 Adapter（Ucapi4UnityFlatRecordSerializer / UoscFlatRecordEmitter + OscClientLifecycle / FileSystemPresetStore / UnityTimeProvider / RenderTextureHandleResolver）を生成して Coordinator に注入し、`IUiCommandClient` / `IUiSubscriptionClient` / `IAsyncAssetLoader` / `IConnectionStatus` / `IDiagnosticsLogger` を shell から受け取る。
  - `CameraSwitcherTabDiagnostics` を外部公開し、`Coordinator.Status` / カメラ数 / アクティブ cameraId / 編集対象 cameraId / OSC 状態 / IPC 接続状態 / 永続化最終時刻 / アクティブプリセット名 / FailureAggregator Kind 別件数 を取得可能にする。
  - 観測可能な完了状態: Editor PlayMode でタブアクティブ化 → IPC 接続後に OSC 起動 → `CameraSwitcherTabDiagnostics.GetSnapshot()` から状態が取得でき、非アクティブ化で Tick 停止、Dispose で全 Adapter がシャットダウンされる。
  - _Requirements: 1.3, 1.4, 1.5, 1.6, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 13.1, 13.2, 13.3, 13.7, 14.1, 14.4, 14.9_
  - _Depends: 2.3, 2.4, 2.5, 2.10, 3.2, 3.3_
  - _Boundary: CameraSwitcherTabBehaviour, CameraSwitcherTabDiagnostics_

- [ ] 3.5 診断ロガーと観測性の結線
  - Coordinator / 各 Adapter / FailureAggregator / PresetController / OscStreamController / VolumeUiStateManager に `IDiagnosticsLogger.Log(Level, LogCategory.TabSpec, message, context)` を呼ぶ経路を埋め、初期化・UIDocument アタッチ・購読登録・OSC 起動停止・CRUD 送信・Volume 編集送信・プリセット I/O 成否・カメラエラー受信・OSC 送信件数と失敗件数を網羅する。
  - ログレベルは shell の `IDiagnosticsLogger.MinimumLevel` に従い、メイン出力サーフェス（Display 2+）に一切描画しない構造的保証（PanelSettings `targetDisplay=0`）を CI で確認可能なスモークテストに落とす。
  - `CameraSwitcherTabDiagnostics.GetSnapshot()` が Requirement 14.9 の全項目を返すことを単体テストで固定する。
  - 観測可能な完了状態: 代表シナリオ（起動 / 追加 / 切替 / プリセット保存 / 切断 / 復旧）で期待ログが Console に出力され、Snapshot に反映されていることを確認できる。
  - _Requirements: 12.6, 12.10, 14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7, 14.8, 14.9_
  - _Depends: 3.4_
  - _Boundary: Diagnostics (logger wiring + CameraSwitcherTabDiagnostics)_

## 4. Validation: 統合テスト・フェイルセーフ・手動検証

- [ ] 4.1 (P) IPC ループバック統合テスト
  - `core-ipc-foundation` の `InMemoryLoopbackTransport`（または同等 Fake）を用いて、UI → メイン出力モック → UI の往復を検証する統合テスト群を追加する。
  - 「`camera/command add` → `camera/created` で Registry 更新 + 初期プレースホルダ差替え」「`cameras/list` / `cameras/active` 受信で UI 状態同期」「`camera/error` 受信で操作単位の失敗表示」「WebSocket 切断 → CRUD 非活性化 → 復旧 → 再購読 + 再スナップショット」を網羅する。
  - 観測可能な完了状態: 上記 4 系統のテストが Tests.Runtime でグリーンとなり、切断/復旧シナリオで Coordinator.Status 遷移が意図通りであることを確認できる。
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 6.1, 6.3, 6.4, 6.5, 6.6, 6.10, 6.12, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8, 7.10, 8.12, 9.1, 9.2, 9.4, 9.5, 12.3, 12.4, 12.7, 12.8, 15.1, 15.2, 15.6_
  - _Depends: 3.4_
  - _Boundary: IpcLoopbackIntegrationTests_

- [ ] 4.2 (P) OSC ループバック + 138 byte blob 統合テスト
  - uOSC client + server を同プロセス内に立て、1 秒以上連続で `/ucapi/camera/{id}/flat` を送信し受信バイト列が常に 138 byte（10 byte header + 128 byte record）として整合することを観測する。
  - cameraId 階層化（2 台以上の cameraId で同時送信 → 受信側で ID 別に区別）、編集対象切替時の送信先切替、delete 連動停止、LateUpdate 1 フレーム 1 メッセージを確認する。
  - 観測可能な完了状態: 60 fps 相当の 600 フレーム送信で受信件数 = 送信件数 + cameraId 別バケット検証がグリーン、delete 後に該当 ID の追加受信が 0 件になる。
  - _Requirements: 3.1, 3.2, 3.3, 4.3, 4.4, 4.5, 4.6, 4.11, 4.12, 4.13, 5.1, 5.2, 5.4, 5.5, 5.6, 15.3, 15.6, 15.7_
  - _Depends: 3.2, 3.4_
  - _Boundary: OscLoopbackIntegrationTests_

- [ ] 4.3 フェイルセーフ統合テスト
  - OSC 断（`OscFailureKind.PortInUse` / `InitializationFailed` を Fake で注入）時に WebSocket 経路の CRUD/Volume/プリセットが継続可能であること、UI バッジが「OSC 送信不可」を示し、他タブ・メイン出力描画に波及しないことを検証する。
  - WebSocket 断時に CRUD が非活性化、OSC は UDP 特性で送信継続、復旧で再購読 + 再スナップショットが走ること、Volume メタデータ Request 失敗時に対象カメラだけがエラー表示になること、`camera/error` の操作単位局所化、範囲外入力の送信抑止を網羅する。
  - 観測可能な完了状態: FailureAggregator のカウントと Diagnostics Snapshot が期待通りで、いずれの失敗経路も例外を送出せず、メイン出力描画に影響しないこと（shell の targetDisplay=0 確認）を整合テストで確認できる。
  - _Requirements: 4.9, 4.10, 8.8, 8.11, 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8, 12.9, 12.10_
  - _Depends: 4.1, 4.2_
  - _Boundary: FailsafeTests_

- [ ] 4.4 プリセット永続化統合テストと送信順序検証
  - `FileSystemPresetStore` 実体を使った round-trip（Create → 500 ms 待機 → 保存 → 再読込で一致）、破損 JSON → `.bak` リネーム → 初回起動扱い、書込失敗 → 次回変更で再試行、`Application.quitting` 相当での強制フラッシュを検証する。
  - プリセット切替時の送信順序が **delete → add → metadata → volume → active-set** であることを FakeIpcClient の送信ログで確定、`camera/created` の clientRequestId マッピングで論理 ID → 新 cameraId 差替え、切替中直列化（2 回連続切替の後続が前者完了を待つこと）を確認する。
  - 観測可能な完了状態: 上記シナリオがすべてグリーンで、実ファイル I/O のテストが teardown で persistentDataPath 配下の生成ファイルを除去する。
  - _Requirements: 11.1c, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8, 11.9, 11.11, 12.9_
  - _Depends: 3.4_
  - _Boundary: PresetRestoreIntegrationTests_

- [ ] 4.5 PlayMode ライフサイクル統合テスト
  - PlayMode 開始 → タブ起動 → OSC 起動 → PlayMode 停止 → ポート解放 / MonoBehaviour 消滅 / 購読解除を 5 回以上繰返し、ハンドル数・GC 可到達参照・uOSC ソケットの残存がないことを確認する。
  - ドメインリロード跨ぎなし、Edit モード非起動、スタンドアロン相当（Build 検証は手動 Sample で補完）と Editor PlayMode で UI 挙動・OSC 到達性・永続化挙動が同一になることを観察する。
  - 観測可能な完了状態: 5 回の PlayMode 繰返しでリソースリーク計測（メモリ / ハンドル / ソケット）がベースラインからの増加なしで収束する。
  - _Requirements: 10.2, 10.3, 10.4, 10.5, 10.6, 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7_
  - _Depends: 3.4_
  - _Boundary: PlayModeLifecycleTests_

- [ ] 4.6 Samples~/MockedStandaloneSample の整備と手動検証手順
  - `MockedStandaloneSample` に最小シーン `MockedCameraSwitcherTab.unity` を配置し、`MockedIpcPeer`（InMemoryLoopback 経由でメイン出力側の挙動をモック）を同梱する。`camera/command` 受信で clientRequestId に基づく `camera/created` 応答、`camera/{id}/volume/overrides/metadata` Request への固定スキーマ Response、`cameras/list` / `cameras/active` state 発行を模倣する。
  - README.md に「PlayMode 起動 → タブ選択 → カメラ追加 → 切替 → Volume 編集 → プリセット保存 → PlayMode 停止」の手順、OSC ループバック受信の確認方法、診断 Snapshot の読み方を記述する。
  - 観測可能な完了状態: Sample を Import して PlayMode で手順どおりに操作すれば、UI 操作 → mock の応答 → UI 反映の一連フローが実機上で再現でき、OSC 受信バイト列も観測できる。
  - _Requirements: 13.7, 15.1, 15.2, 15.3, 15.5_
  - _Depends: 3.4, 4.1, 4.2_
  - _Boundary: Samples~/MockedStandaloneSample_

- [ ]* 4.7 (P) パフォーマンスベースライン計測
  - OSC 送信スループット（60 fps × 1 cameraId × 10 秒で損失 0）、マルチプレビュー GPU コスト（既定 8 台 × 192x108 × 15 fps + 640x360 × 60 fps）、プリセット debounce 負荷（1 秒間 100 回変更 → 1 回 SaveAsync）を測定する。
  - いずれも Requirement 4.5（60 Hz 同期）・11.3（500 ms デバウンス）・2.10（メイン出力描画非干渉）のベースライン確認として結果を残す。
  - 観測可能な完了状態: 3 計測の結果が README に記録され、以降の変更で劣化検知の基準値として参照可能になる。
  - _Requirements: 2.10, 4.5, 11.3_
  - _Boundary: Performance Benchmarks_
