# Implementation Plan

> 本計画は design.md の Hexagonal（Ports & Adapters）構成を前提に、Foundation（パッケージ骨格 / 抽象 / Fakes）→ Adapters（採用パッケージブリッジ）→ Domain（状態機械）→ Runtime（Composition Root / IPC 登録）→ Validation（統合 / フェイルセーフ / 手動検証）の順で積み上げる。TDD を基本とし、各 Adapter / Domain モジュールは Fake を先行整備して失敗テストから実装を駆動する。タスクはすべて `[ ]` 状態で出力する。

## 1. Foundation: パッケージ骨格と共有抽象の整備

- [x] 1.1 UPM パッケージ骨格と asmdef 参照方向の確立
  - `jp.hidano.vtuber-system-base.camera-switcher-output-adapter` UPM パッケージ（package.json）を新規作成し、`Runtime/Abstractions`、`Runtime/Domain`、`Runtime/Adapters`、`Runtime/Runtime`、`Editor`、`Tests/Runtime`、`Samples~/MockedOscSenderSample` のディレクトリ骨格を配置する。
  - `Abstractions` / `Domain` / `Runtime`（実体クラス）/ `Editor` / `Tests.Runtime` の 5 つの asmdef を作成し、`Abstractions ← Domain ← Runtime`、`Tests.Runtime → Runtime + Domain + Abstractions` の参照方向を確立する。Editor は `#if UNITY_EDITOR` のみ参照する。
  - Runtime asmdef から `output-renderer-shell` Abstractions（GUID 参照）/ `core-ipc-foundation` Abstractions（GUID `286be82527bb75547a774598be8243ab`）/ `camera-switcher-tab.Contracts`（GUID 参照）/ `com.hidano.uosc` / `com.hidano.ucapi4unity` を `references` に登録、`overrideReferences:true` + `precompiledReferences` で `System.Text.Json` 系を取り込む。
  - `.meta` ファイルの GUID は `[guid]::NewGuid().ToString('N')` で都度ランダム生成する（CLAUDE.md ルール、連続パターン禁止）。
  - 観測可能な完了状態: Unity プロジェクトでパッケージがコンパイル通過し、Assembly Definition Inspector から参照方向（他タブ spec / 他出力アダプタ spec への参照無し）が確認できる。
  - _Requirements: 11.7, 12.1_
  - _Boundary: PackageSkeleton_

- [x] 1.2 Port 抽象（IOscReceiverHost / ICameraIdAllocator / ILocalVolumeBinder / IVolumeOverrideSchemaResolver）の定義
  - `Abstractions` asmdef に `IOscReceiverHost`（StartAsync / StopAsync / MessageReceived event / Status）、`ICameraIdAllocator`（Allocate）、`ILocalVolumeBinder`（CreateLocalVolume / AddOverride / RemoveOverride / SetOverrideEnabled / SetOverrideParam / SetVolumeEnabled / DestroyLocalVolume）、`IVolumeOverrideSchemaResolver`（GetSchema、cache 含む）、`ICameraSwitcherOutputAdapterClock` を定義する。
  - `OscReceivedMessage`（CameraId / Blob: byte[]）、`OscReceiverHostStatus`（Stopped/Starting/Running/Failed）、`OscReceiverStartResult`（Ok / Failure）、`VolumeBindResult`（Ok / Error）、`CameraEntry`、`CameraSwitcherOutputAdapterConfig`（ScriptableObject、OSC Host=127.0.0.1、Port=9000、DefaultCameraTransform 等）を定義する。
  - 各 Port の pre/post condition（null 禁止、StartAsync 前の MessageReceived 発火禁止、Stop 後の Start 再許容）を XML Doc コメントで明記する。
  - 観測可能な完了状態: Tests.Runtime の skeleton テストが Port 抽象のみ参照してコンパイル通過する。
  - _Requirements: 1.1, 1.2, 1.8, 6.1, 7.1, 7.5_
  - _Boundary: Abstractions/Ports_

- [x] 1.3 Fake Port 群とテスト支援ユーティリティの整備
  - `Tests.Runtime/Fakes` 配下に以下を実装する：
    - `FakeOutputCommandDispatcher`（`IOutputCommandDispatcher` のテストダブル、登録した handler を test side から `InvokeStateAt(topic, payload)` / `InvokeEventAt(topic, payload)` / `InvokeRequestAt<TReq, TRes>(topic, req)` で呼び出せる、PublishState/PublishEvent/Response の送信先バッファを持つ）。
    - `FakeOutputSceneRoots`（`IOutputSceneRoots` のテストダブル、テストシーンに動的に GameObject を作って Cameras / DefaultCamera / Volumes を返す）。
    - `FakeCameraIdAllocator`（固定値 / シーケンスを設定可能）。
    - `FakeOscReceiverHost`（`MessageReceived` を test 側から `Emit(cameraId, blob)` で発火）。
    - `FakeLocalVolumeBinder`（呼出履歴をバッファ）。
    - `FakeVolumeOverrideSchemaResolver`（任意スキーマを返却可能）。
  - 共通テストユーティリティ（`AssertEnvelope`、`PayloadFactory`、`UcapiFlatRecordTestFactory`：`UCAPI4Unity.UcApi4UnityCamera.SerializeFromCamera` で本物の Flat Record blob を生成）を追加する。
  - 観測可能な完了状態: Fake のセルフテストが Tests.Runtime でグリーンになる。
  - _Requirements: 13.1, 13.3, 13.4_
  - _Boundary: Tests/Fakes_

## 2. Adapters: 採用パッケージのブリッジ実装

- [x] 2.1 (P) FlatRecordAddressDecoder と OscMessageRouter の実装
  - `FlatRecordAddressDecoder`：`/ucapi/camera/{cameraId}/flat` の文字列から `cameraId` を抽出する純関数。`OscAddressBuilder.DefaultPrefix`（`/ucapi/camera`）との整合性を確認、不正アドレスは `null` を返す。
  - `OscMessageRouter`：`OscReceivedMessage`（cameraId 抽出済み）を CameraEntryRegistry へ流す前処理。未知 cameraId は `FailureAggregator.RecordUnknownCameraIdOnOsc` を呼び破棄。
  - Tests.Runtime の `OscMessageRouterTests` で「正常アドレス → cameraId 抽出」「不正プレフィクス → 破棄」「末尾 `/flat` 欠如 → 破棄」「`cameraId` に許容外文字 → 破棄」を検証する。
  - 観測可能な完了状態: アドレス分解の単体テストが全パターンでグリーンになる。
  - _Requirements: 1.9, 2.1_
  - _Depends: 1.2_
  - _Boundary: OscMessageRouter, FlatRecordAddressDecoder_

- [x] 2.2 (P) Ucapi4UnityFlatRecordApplier の実装
  - `Ucapi4UnityFlatRecordApplier.Apply(blob: byte[], camera: Camera)` で `UCAPI4Unity.UnityCamera.UcApi4UnityCamera.ApplyToCamera(blob, camera)` を呼ぶ薄ラップを実装する。
  - 例外（CRC 失敗 / DLL 不在 / 解析失敗）を try/catch、`FailureAggregator.RecordOscDecodeFailure(cameraId, ex)` を呼ぶ。`byte[]` 追加コピーは行わない。
  - Tests.Runtime の `Ucapi4UnityFlatRecordApplierTests` で「正常 blob で Camera プロパティ反映」（テスト用に `UcApi4UnityCamera.SerializeFromCamera` で生成した本物の blob を用いて round-trip）「無効 blob で例外捕捉 + FailureAggregator 呼出」を検証する。
  - 観測可能な完了状態: round-trip テストで送信前の Camera と受信後の Camera の position / rotation / focalLength / sensorSize が許容誤差内で一致する。
  - _Requirements: 2.3, 2.4, 2.6_
  - _Depends: 1.2, 1.3_
  - _Boundary: Ucapi4UnityFlatRecordApplier_

- [x] 2.3 UoscReceiverHostAdapter と CameraOscReceiverHost の実装
  - `CameraOscReceiverHost` MonoBehaviour（空クラス、`uOscServer` の attach 先として `new GameObject` で動的生成される）。
  - `UoscReceiverHostAdapter` で `IOscReceiverHost` 実装：`StartAsync(host, port)` で `CameraOscReceiverHost` GameObject を生成、`uOscServer.AddComponent`、`port` 設定、`autoStart=false`、`StartServer()` を呼び、`onDataReceived` を購読。`onDataReceived` のメッセージから `address`、`values[0]` を取り出して `OscReceivedMessage{ CameraId（FlatRecordAddressDecoder で抽出）, Blob: byte[] }` として `MessageReceived` event を発火。
  - `StopAsync()`：`uOscServer.StopServer()` → `Destroy(GameObject)`。`Status` プロパティを更新。
  - ポート占有等で起動失敗した場合は `OscReceiverStartResult.Failure(detail)` を返し、`Status = Failed`。
  - Tests.Runtime の `UoscReceiverHostAdapterTests`（PlayMode 必須）で「Start → Stop でソケット解放」「Start 失敗で Failure 返却」「メッセージ発火がメインスレッド」を検証する。
  - 観測可能な完了状態: PlayMode テストで Start / Stop の繰返しがリソースリーク無しで成立、不一致プレフィクスのメッセージは MessageReceived に流れない。
  - _Requirements: 1.1, 1.5, 1.6, 1.8, 1.9_
  - _Depends: 2.1_
  - _Boundary: UoscReceiverHostAdapter, CameraOscReceiverHost_

- [x] 2.4 (P) SequentialCameraIdAllocator の実装
  - `SequentialCameraIdAllocator` で `ICameraIdAllocator.Allocate()` を実装：内部カウンタ（初期 1、Allocate ごとにインクリメント）から `cam-{NNNN}`（4 桁ゼロ埋め、超えたら桁数拡張）の `CameraId` を返す。`OscAddressBuilder.IsValidCameraIdSegment` をパスする文字種を保証する。
  - 削除時にカウンタを減らさない（再利用しない、CSO-6）。スレッドセーフ性は要求しない（メインスレッド前提）。
  - Tests.Runtime の `SequentialCameraIdAllocatorTests` で「`cam-0001` / `cam-0002` / `cam-0003`」「9999 を超えると `cam-10000` の 5 桁拡張」「`OscAddressBuilder.IsValidCameraIdSegment(allocated)` が常に true」を検証する。
  - 観測可能な完了状態: 採番順の単体テストがグリーンになる。
  - _Requirements: 3.2_
  - _Depends: 1.2_
  - _Boundary: SequentialCameraIdAllocator_

- [x] 2.5 GlobalEnabledLocalVolumeBinder の実装
  - `GlobalEnabledLocalVolumeBinder.CreateLocalVolume(parent, cameraId, priority)`：`new GameObject($"LocalVolume-{cameraId.Value}")` を `parent.transform` の子として生成、`Volume` コンポーネントを attach、`isGlobal=true`、`weight=1`、`priority=priority`、`enabled=false`、空 `VolumeProfile`（ScriptableObject 動的生成）を割り当てて返す。
  - `AddOverride(volume, overrideTypeName)`：`VolumeComponentTypeResolver.Resolve(overrideTypeName)` で型を取得、`volume.profile.Add(type, overrides: false)` を呼ぶ。失敗時 `VolumeBindResult.Error`。
  - `RemoveOverride(volume, overrideTypeName)`：`volume.profile.Remove(type)`。
  - `SetOverrideEnabled(volume, overrideTypeName, enabled)`：該当 `VolumeComponent.active` プロパティを設定。
  - `SetOverrideParam(volume, overrideTypeName, paramName, value)`：`VolumeParameterValueWriter.Write(component, paramName, value)` 経由で Reflection 設定（次タスク）。
  - `SetVolumeEnabled(volume, enabled)` / `DestroyLocalVolume(volume)`。
  - Tests.Runtime の `GlobalEnabledLocalVolumeBinderTests`（PlayMode）で `Bloom` / `Tonemapping` の AddComponent / Remove / enabled トグル / 未知 type のエラー応答を検証。
  - 観測可能な完了状態: 実 URP `VolumeProfile` に対して Override 操作が成功し、`volume.profile.components` の数が期待通り変化する。
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.6, 6.8, 6.9_
  - _Depends: 1.2, 1.3_
  - _Boundary: GlobalEnabledLocalVolumeBinder, VolumeComponentTypeResolver_

- [x] 2.6 VolumeParameterValueWriter の実装
  - `VolumeParameterValueWriter.Write(component: VolumeComponent, paramName: string, value: JsonElement)`：
    - Reflection で `component` の public フィールドのうち名前が `paramName` のフィールドを探す。
    - フィールドが `VolumeParameter<T>` 派生型であることを確認、`T` の型に応じて `JsonElement` から値を取り出す（`float` / `int` / `bool` / `Color`（`{ r, g, b, a }` JSON object）/ `Enum`（int cast））。
    - `VolumeParameter.SetValue(VolumeParameter)` 相当の手段（base クラスの `SetValue(VolumeParameter)` API、または `parameter.value = ...` に直接代入）で値を設定し、`overrideState = true` に設定する。
    - 例外時は `FailureAggregator.RecordCameraOperationFailure(op="volume-param", ...)` を呼ぶ。
  - Tests.Runtime の `VolumeParameterValueWriterTests` で `Bloom.intensity` (FloatParameter) / `Bloom.tint` (ColorParameter) / `ColorAdjustments.colorFilter` (ColorParameter) / `Tonemapping.mode` (Enum) を順に書き、Reflection 経由で値が反映されることを検証。
  - 観測可能な完了状態: 各型タグ（float / int / bool / color / enum）のラウンドトリップがグリーンになる。
  - _Requirements: 6.5, 6.10_
  - _Depends: 2.5_
  - _Boundary: VolumeParameterValueWriter_

- [x] 2.7 ReflectionVolumeOverrideSchemaResolver の実装
  - `ReflectionVolumeOverrideSchemaResolver.GetSchema()` で URP の `UnityEngine.Rendering.VolumeManager.instance.baseComponentTypeArray` から `VolumeComponent` 派生型を列挙する。
  - 各型について `VolumeOverrideSchema { Type=t.Name, DisplayName=VolumeComponentMenuAttribute.menu ?? t.Name, Params }` を構築。
  - 各型の public フィールドを Reflection で取得し、`VolumeParameter` 派生型のフィールドのみを残す。フィールドごとに `VolumeParamSchema { Name, TypeTag, Min, Max, Default, DisplayName, Unit, EnumValues }` を生成：
    - TypeTag: 派生型から判定（`FloatParameter`/`MinFloatParameter`/`ClampedFloatParameter`/`NoInterpFloatParameter` → `"float"`、`IntParameter`/`MinIntParameter`/`ClampedIntParameter`/`NoInterpIntParameter` → `"int"`、`BoolParameter`/`NoInterpBoolParameter` → `"bool"`、`ColorParameter`/`NoInterpColorParameter` → `"color"`、`Enum` 派生（`VolumeParameter<TEnum>` where TEnum : Enum）→ `"enum"`）。
    - Min / Max: `MinAttribute` / `MaxAttribute` / `ClampedFloatParameter.min` / `.max` から抽出。なければ `null`。
    - Default: フィールドの初期値（`Activator.CreateInstance` した `VolumeComponent` の値）を `JsonSerializer.Serialize` 経由で `JsonElement` に。
    - EnumValues: enum 型のとき `Enum.GetNames(t)` の配列。
  - 初回 `GetSchema()` 時に Reflection を実行、結果を private field にキャッシュ、以降は同 instance を返す。
  - 例外時は空 `VolumeMetadataResponse` を返す。
  - 未知 `VolumeParameter` 派生型に遭遇したら当該フィールドをスキップしてログ。
  - Tests.Runtime の `ReflectionVolumeOverrideSchemaResolverTests` で「`Bloom` / `Tonemapping` / `ColorAdjustments` のスキーマが取得できる」「2 回呼んでも同 instance」「未知派生型でスキップ」「例外時に空 schema」を検証。
  - 観測可能な完了状態: URP 標準 Override 群について TypeTag 判定 + Default 値抽出 + Min/Max 抽出が正しく行われる。
  - _Requirements: 7.2, 7.3, 7.4, 7.5, 7.6, 7.8_
  - _Depends: 1.2_
  - _Boundary: ReflectionVolumeOverrideSchemaResolver_

## 3. Domain: 状態機械の実装

- [x] 3.1 (P) CameraEntryRegistry と DefaultCameraFallbackController の実装
  - `CameraEntryRegistry`：`Dictionary<CameraId, CameraEntry>` + 追加順 List を保持、`Upsert(entry)` / `Remove(cameraId)` / `TryGet` / `Enumerate()` を実装。並び順は `AllocOrder` 昇順で安定（CSO-6）。
  - `DefaultCameraFallbackController`：`IOutputSceneRoots.DefaultCamera` を保持、`NotifyCameraCountChanged(int count)` で `count >= 1 → DefaultCamera.enabled = false`、`count == 0 → DefaultCamera.enabled = true` を切替。
  - Tests.Runtime の `CameraEntryRegistryTests` で「Upsert 100 件 + Remove 50 件で残数 50 + 採番順安定」「未知 cameraId Remove で no-op」、`DefaultCameraFallbackControllerTests` で「count=1 で DefaultCamera 無効化」「count=0 復帰」を検証。
  - 観測可能な完了状態: 単体テストがグリーンで、CameraEntry が enumerate される順序が AllocOrder 昇順で固定。
  - _Requirements: 3.9, 4.1, 4.3_
  - _Depends: 1.2_
  - _Boundary: CameraEntryRegistry, DefaultCameraFallbackController_

- [x] 3.2 (P) ActiveCameraGate の実装
  - `ActiveCameraGate.SetActive(target)`：`CameraEntryRegistry.Enumerate()` を走査し、target == entry.CameraId なら `entry.CameraComponent.enabled = true` + `entry.LocalVolume.enabled = true`、他は両方 `false` にする。`Active = target` を更新。
  - `OnCameraRemoved(removed)`：`Active == removed` なら `Active = null` にして `DefaultCameraFallbackController` にも通知（registry が空に近づいた場合）。
  - 未知 cameraId は `FailureAggregator.RecordUnknownCameraIdOnActiveSet` を呼び `Active` を変更しない。
  - Tests.Runtime の `ActiveCameraGateTests` で「3 台中 cam-0002 を SetActive → cam-0002 のみ enabled」「未知 cameraId で no-op + エラー記録」「現アクティブを Remove → Active=null」を検証。
  - 観測可能な完了状態: 単体テストで Camera.enabled / Volume.enabled の組み合わせが期待通りになる。
  - _Requirements: 3.6, 3.7, 6.7_
  - _Depends: 3.1_
  - _Boundary: ActiveCameraGate_

- [x] 3.3 FailureAggregator の実装
  - `FailureAggregator.RecordOscDecodeFailure(cameraId, exception)`：ログのみ、`camera/error` 発行しない（Requirement 2.4 / 12.2）。
  - `RecordCameraOperationFailure(op, cameraId, reason, detail, clientRequestId)`：`camera/error` event を `IOutputCommandDispatcher` 経由（実際には PublishEvent シンク）で発行。Kind 別カウンタを進める。
  - `RecordOscStartupFailure(detail)`：`camera/error { Reason: "OscStartupFailed" }` 発行。
  - `RecordUnknownCameraIdOnOsc/Ipc` / `RecordVolumeBindFailed` / `RecordReflectionFailed` / `RecordIpcSendFailed`。
  - `GetSnapshot()`：Kind 別件数 + 直近 N=20 件履歴の構造化スナップショット。
  - Tests.Runtime の `FailureAggregatorTests` で「Kind 別カウントが正しく加算」「camera/error が PublishEvent シンクに渡る」「OscDecodeFailure は camera/error 発行しない」を検証。
  - 観測可能な完了状態: 単体テストで Kind 別カウント / Snapshot / event 発行が期待通り。
  - _Requirements: 1.4, 2.2, 2.4, 3.4, 3.7, 6.9, 6.10, 8.4, 12.4, 14.x_
  - _Depends: 1.2, 1.3_
  - _Boundary: FailureAggregator_

- [x] 3.4 CamerasListPublisher の実装
  - `CamerasListPublisher.PublishCamerasList(IEnumerable<CameraEntry>)`：`CamerasListPayload { Cameras=[CameraListEntry...], UpdatedAtUnixMs=now }` を構築して `IOutputCommandDispatcher` 経由（実際には shell 提供の PublishState シンク）で発行。
  - `PublishCamerasActive(CameraId? active)`：`CamerasActiveStatePayload { ActiveCameraId, UpdatedAtUnixMs }`。
  - `PublishCameraCreated(clientRequestId, cameraId, metadata)`：`CameraCreatedEventPayload`。
  - `PublishVolumeEnabledForAll(IEnumerable<CameraEntry>)`：active-set 連動時に各 cameraId の `camera/{id}/volume/enabled` を発行（Requirement 6.7）。
  - Tests.Runtime の `CamerasListPublisherTests` で「Registry 3 件 → CameraListEntry 3 件、AllocOrder 昇順」「Active 切替で publish」「camera/created の clientRequestId echo」を検証。
  - 観測可能な完了状態: PublishState/Event のシンクに期待 payload が積まれる。
  - _Requirements: 4.2, 4.3, 4.4, 4.5, 4.6, 6.7, 8.1〜8.5_
  - _Depends: 3.1_
  - _Boundary: CamerasListPublisher_

- [x] 3.5 CameraSwitcherOutputAdapter 状態機械の統合
  - `CameraSwitcherOutputAdapter` を実装し、内部に `CameraEntryRegistry` / `ActiveCameraGate` / `CamerasListPublisher` / `DefaultCameraFallbackController` / `FailureAggregator` / `OscMessageRouter` を composition として保持。
  - 全 port を DI で受け取る `InitializeAsync(ct)` を実装：`IpcHandlerRegistration.RegisterAll(dispatcher, this)` を呼んで IPC ハンドラ登録 → `oscReceiverHost.StartAsync(...)` で OSC 起動 → 初期 `cameras/list` / `cameras/active=null` を publish。
  - `OnOscMessageReceived(in OscReceivedMessage)`：`MainThreadGuard.AssertMainThread()` → `OscMessageRouter` で cameraId 解決 → `CameraEntryRegistry.TryGet` → `Ucapi4UnityFlatRecordApplier.Apply(blob, entry.CameraComponent)`。未知 cameraId は破棄。
  - `OnCameraCommand(EventCommand<CameraCommandPayload>)`：op に応じて add / delete / active-set を分岐。add は `ICameraIdAllocator.Allocate()` → `CameraGameObjectFactory.Create(...)` → `Registry.Upsert` → `DefaultCameraFallbackController.NotifyCameraCountChanged` → per-camera Request handler 登録 → `CamerasListPublisher.PublishCameraCreated` + `PublishCamerasList`。
  - `OnCameraMetadata`、`OnVolumeCommand`、`OnVolumeEnabled`、`OnVolumeOverrideEnabled`、`OnVolumeOverrideParam`、`OnVolumeMetadataRequest`、`OnPreviewCommand`、`OnPresetCommandObservation` を実装。
  - `Dispose()`：`oscReceiverHost.StopAsync()` → `IpcHandlerRegistration.Dispose()` → `Registry.Enumerate` で各 GameObject 破棄 → `DefaultCameraFallbackController` で `DefaultCamera.enabled = true` 復帰。
  - Tests.Runtime の `CameraSwitcherOutputAdapterStateTests` で「add → cameraId 採番 → camera/created + cameras/list publish」「delete → registry から削除 + cameras/list 再 publish」「active-set → ActiveCameraGate 呼出 + cameras/active publish + 各 camera/{id}/volume/enabled publish」を Fake Adapter 注入で検証。
  - 観測可能な完了状態: Fake 全 port を注入した状態で、IPC envelope の各種注入から期待 publish が出る一連の状態機械テストがグリーンになる。
  - _Requirements: 2.x, 3.x, 5.x, 6.x, 9.x, 11.1, 11.2_
  - _Depends: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 3.1, 3.2, 3.3, 3.4_
  - _Boundary: CameraSwitcherOutputAdapter_

## 4. Runtime: Composition Root と Unity 統合

- [x] 4.1 CameraGameObjectFactory の実装
  - `CameraGameObjectFactory.Create(parent: Transform, cameraId, displayName, defaultTransform, allocOrder)`：`new GameObject($"Camera-{cameraId}-{displayName}")` を `parent` 下に生成、`Camera` コンポーネントを attach、`usePhysicalProperties=true`、`focalLength=defaultTransform.FocalLengthMm`、`sensorSize=(36,24)` 既定、デフォルト transform を `position` / `rotation` で適用、`enabled=false`（active-set されるまで）。
  - 子 GameObject `LocalVolume-{cameraId}` を `ILocalVolumeBinder.CreateLocalVolume(parent=cameraGo, cameraId, priority=allocOrder)` で生成。
  - `CameraEntry` を返す。
  - `Destroy(entry)`：`ILocalVolumeBinder.DestroyLocalVolume(entry.LocalVolume)` → `UnityEngine.Object.Destroy(entry.GameObject)`。
  - Tests.Runtime（PlayMode）の `CameraGameObjectFactoryTests` で「生成された Camera が `usePhysicalProperties=true` で `focalLength=50`」「LocalVolume が `isGlobal=true` で `enabled=false`」「Destroy で全オブジェクトが消える」を検証。
  - 観測可能な完了状態: PlayMode テストで Camera と LocalVolume が `CamerasRoot` 下に生成・破棄される。
  - _Requirements: 3.2, 5.4, 6.1, 6.8_
  - _Depends: 2.5_
  - _Boundary: CameraGameObjectFactory_

- [x] 4.2 IpcHandlerRegistration の実装
  - `IpcHandlerRegistration.RegisterAll(dispatcher: IOutputCommandDispatcher, adapter: CameraSwitcherOutputAdapter)`：
    - `RegisterEventHandler<CameraCommandPayload>(CameraIpcTopics.CameraCommand, adapter.OnCameraCommand)`
    - `RegisterEventHandler<PreviewCommandPayload>(CameraIpcTopics.PreviewCommand, adapter.OnPreviewCommand)`
    - `RegisterEventHandler<PresetCommandPayload>(CameraIpcTopics.PresetCommand, adapter.OnPresetCommandObservation)`（観測のみ）
    - cameraId 単位の動的 topic（`camera/{id}/metadata/{key}` 等）は cameraId 確定後（`OnCameraCommand add` 完了時）に `RegisterPerCameraHandlers(dispatcher, cameraId)` で動的登録、`delete` 時に対応 `OutputCommandHandlerRegistration` を Dispose する。
  - `RegisterPerCameraHandlers(dispatcher, cameraId)`：`displayName` / `type` / `defaultTransform` の各 metadata、`volume/command` / `volume/enabled` / `volume/override/{type}/enabled` / `volume/override/{type}/{param}` / `volume/overrides/metadata` Request の handler を登録、戻りの `OutputCommandHandlerRegistration` を per-cameraId のリストに保持。
  - `Dispose()`：全登録を逆順で Dispose。
  - Tests.Runtime の `IpcHandlerRegistrationTests` で「RegisterAll 後の登録件数」「per-camera handler が cameraId ごとに増減」「Dispose で全件解除」を Fake Dispatcher 経由で検証。
  - 観測可能な完了状態: cameraId 増減で `RegisteredHandlerCount` が期待通り変化、Dispose で 0 に戻る。
  - _Requirements: 3.1, 5.1, 6.x, 7.1, 9.1, 11.2_
  - _Depends: 3.5_
  - _Boundary: IpcHandlerRegistration_

- [x] 4.3 MainThreadGuard と CameraSwitcherOutputAdapterDiagnostics の実装
  - `MainThreadGuard.AssertMainThread()`：Unity の `UnitySynchronizationContext.Current` が `null` でないこと、現在のスレッドがメインスレッドと一致することをチェック。違反時は `InvalidOperationException` 送出。`Awake/Start` で `MainThreadId = Thread.CurrentThread.ManagedThreadId` を保存して以降の比較に使う。
  - `CameraSwitcherOutputAdapterDiagnostics.GetSnapshot()`：Adapter / Registry / OscReceiverHost / FailureAggregator から状態を集約した `Snapshot` 構造を返す（CameraCount / ActiveCameraId / OSC Status / OSC 受信件数 / IPC ハンドラ件数 / camera/error 発行件数 / Kind 別失敗カウント / DefaultCamera fallback 状態 / Registered topics）。
  - Tests.Runtime の `MainThreadGuardTests`（PlayMode、別スレッドから呼んで例外確認）と `DiagnosticsSnapshotTests` で各項目が期待値を返すことを検証。
  - 観測可能な完了状態: ワーカースレッドからの呼出で MainThreadGuard が例外、Diagnostics Snapshot が代表シナリオで期待値を返す。
  - _Requirements: 10.x, 14.x_
  - _Depends: 3.5_
  - _Boundary: MainThreadGuard, CameraSwitcherOutputAdapterDiagnostics_

- [x] 4.4 CameraSwitcherOutputAdapterBootstrapper の実装
  - `CameraSwitcherOutputAdapterBootstrapper` MonoBehaviour を実装：
    - Inspector で `CameraSwitcherOutputAdapterConfig`（ScriptableObject、OSC Host/Port、DefaultCameraTransform）を受け取る。
    - `OutputSceneBootstrapper` の `IOutputSceneRoots` 提供完了を待ってから（イベント / Service Locator / FindObjectOfType の順で取得、本フェーズは FindObjectOfType を許容）、`Awake/Start` で全 Adapter を `new` し `CameraSwitcherOutputAdapter` に注入、`InitializeAsync` を呼ぶ。
    - 取得する shell 抽象：`IOutputCommandDispatcher`、`IOutputSceneRoots`。
    - `Application.isPlaying` チェックで Edit モードでは起動しない。
    - `OnDestroy` / `OnApplicationQuit` / Editor の `EditorApplication.playModeStateChanged` 経由で `Adapter.Dispose()` を呼ぶ。
  - 観測可能な完了状態: PlayMode 起動でログ「Camera Switcher Output Adapter ready」、PlayMode 停止で全 Disposable 解放。Edit モードに残留 GameObject 無し。
  - _Requirements: 1.3, 1.5〜1.7, 11.1〜11.7, 13.7_
  - _Depends: 4.1, 4.2, 4.3_
  - _Boundary: CameraSwitcherOutputAdapterBootstrapper_

## 5. Validation: 統合・フェイルセーフ・手動検証

- [x] 5.1 (P) OSC ループバック統合テスト（1000 件 / 60Hz）
  - Tests.Runtime に `OscLoopbackIntegrationTests`（PlayMode）を追加：
    - 同プロセス内で `uOSC.uOscClient` を `127.0.0.1:9000` 向けに用意し、`UcApi4UnityCamera.SerializeFromCamera` で生成した本物の Flat Record blob を `/ucapi/camera/cam-0001/flat` に対して 1 秒間 60 fps × 60 秒（合計 3600 件、損失検出のため少なくとも 1000 件以上）送信。
    - 本 spec 側で `Bootstrapper` を起動し、`camera/command add` を IPC 経由で 1 台 add → 採番された cameraId で OSC 送信し、Camera transform が更新されることを検証。
    - 同時に 2 台目を add し、`/ucapi/camera/cam-0002/flat` に異なる transform を送信、cameraId 別に区別されて適用されることを確認。
    - `delete` 後は当該 cameraId 向け blob が無視されることを確認。
  - 観測可能な完了状態: 1000 件以上のメッセージで損失率 0、cameraId 別の Camera transform が期待通り更新される。
  - _Requirements: 1.1, 1.8, 2.x, 3.5, 13.2_
  - _Depends: 4.4_
  - _Boundary: OscLoopbackIntegrationTests_

- [x] 5.2 (P) IPC ハンドラ統合テスト
  - Tests.Runtime に `IpcHandlerIntegrationTests` を追加（Fake `IOutputCommandDispatcher` 経由でハンドラを直接呼び出し）：
    - `add` event 注入 → `camera/created` echo 発行 + `cameras/list` 再 publish + Camera GameObject 生成 + LocalVolume 生成。
    - `delete` event → Camera/LocalVolume 破棄 + `cameras/list` 再 publish。
    - `active-set` event → ActiveCameraGate 切替 + `cameras/active` publish + 各 cameraId の `camera/{id}/volume/enabled` publish。
    - `camera/{id}/metadata/displayName` state → GameObject 名更新 + `cameras/list` 再 publish。
    - `camera/{id}/metadata/type` state → `Camera.orthographic` 切替。
    - `camera/{id}/volume/command override-add Bloom` event → `volume.profile.Add<Bloom>()`。
    - `camera/{id}/volume/override/Bloom/intensity` state → Reflection で `Bloom.intensity.value` 設定。
    - `camera/{id}/volume/overrides/metadata` Request → URP の VolumeComponent 派生型を含む `VolumeMetadataResponse` 返却。
    - `camera/preview/command attach` → `camera/{id}/preview/handle` プレースホルダ publish（textureKey="")。
  - 観測可能な完了状態: 各シナリオで FakeOutputCommandDispatcher の publish/response バッファに期待 payload が積まれる。
  - _Requirements: 3.x, 4.x, 5.x, 6.x, 7.x, 8.x, 9.x, 13.1, 13.3_
  - _Depends: 4.4_
  - _Boundary: IpcHandlerIntegrationTests_

- [x] 5.3 フェイルセーフ統合テスト
  - Tests.Runtime に `FailsafeTests` を追加：
    - **OSC 起動失敗**: 別の `uOscServer` で `127.0.0.1:9000` を先に占有 → 本 spec を起動 → `OscReceiverStartResult.Failure` が返り、`camera/error { Reason="OscStartupFailed" }` が UI 側 publish バッファに出ること。IPC ハンドラ系（add / delete / metadata / volume）は引き続き動作。
    - **未知 cameraId on OSC**: `add` 完了前に `/ucapi/camera/cam-0001/flat` を送信 → 破棄、`camera/error` 発行されない、ログのみ。
    - **未知 cameraId on IPC**: 存在しない cameraId に対して `delete` / `active-set` / `metadata` 送信 → `camera/error` 発行、他処理は継続。
    - **VolumeBind 失敗**: 未知 OverrideType の `override-add` → `VolumeBindResult.Error` + `camera/error` 発行、他カメラ・他 Override 継続。
    - **UCAPI デコード失敗**: 不正な byte[] を OSC 経由で送信 → `Ucapi4UnityFlatRecordApplier` が例外捕捉、Camera 状態維持、ログのみ。
    - **VolumeMetadataRequest 例外**: `IVolumeOverrideSchemaResolver` を Fake で例外送出に切替 → 空 schema 応答、Adapter 継続。
  - 観測可能な完了状態: 各シナリオで本 spec が落ちず、Diagnostics Snapshot に Kind 別カウントが正しく加算される。
  - _Requirements: 1.4, 2.2, 2.4, 3.4, 3.7, 6.9, 6.10, 7.8, 12.1〜12.7_
  - _Depends: 5.1, 5.2_
  - _Boundary: FailsafeTests_

- [ ] 5.4 PlayMode ライフサイクル統合テスト
  - Tests.Runtime に `PlayModeLifecycleTests` を追加：PlayMode 開始 → Bootstrapper 起動 → 5 台 add + active-set + Volume 編集 → PlayMode 停止を 5 回繰返し、(1) `127.0.0.1:9000` ポート解放、(2) `Camera-cam-*` GameObject 残存数 0、(3) `CameraOscReceiverHost` GameObject 消滅、(4) IPC ハンドラ登録件数 0、(5) GC 可到達参照増加なし、を計測。
  - Edit モード経路では Bootstrapper が早期 return するため何も起きないことを検証。
  - 観測可能な完了状態: 5 回繰返しでベースラインからのリソース増加なしで収束。
  - _Requirements: 1.5, 1.6, 1.7, 11.x, 13.7_
  - _Depends: 4.4_
  - _Boundary: PlayModeLifecycleTests_

- [ ] 5.5 配信適合性テスト（メイン出力に UI 描画なし）の追加
  - `output-renderer-shell` の `MainOutputNoOverlayTests`（既存）の対象範囲を本 spec が追加する `Camera-cam-*` / `LocalVolume-*` / `CameraOscReceiverHost` GameObject に拡張するテストを Tests.Runtime に追加（`AssertNoOverlayOnMainDisplay`）。
  - `OnGUI` / `IMGUI` / `UIDocument` / `PanelSettings.targetDisplay >= 1` を持つコンポーネントが本 spec が生成した GameObject 階層配下に存在しないことを構造的に検証する（FindObjectsOfType + 各コンポーネントタイプの検出）。
  - 観測可能な完了状態: 本 spec のすべての生成オブジェクトが `targetDisplay = 0` 限定、または描画コンポーネント自体を持たないことを単体テストでグリーン化。
  - _Requirements: 12.1, OR-1, 5.6_
  - _Depends: 4.4_
  - _Boundary: MainOutputNoOverlayCoverage_

- [ ] 5.6 Samples~/MockedOscSenderSample の整備と手動検証手順
  - `Samples~/MockedOscSenderSample/` に `MockedOscSender.unity`、`MockedOscSenderTest.cs`（`uOscClient` を 127.0.0.1:9000 向けに用意し、編集対象の Camera を 1 つ持って `UcApi4UnityCamera.SerializeFromCamera` で blob 生成 + 60Hz 送信）、`README.md`（手順書）を配置。
  - README は次の手順を含める：
    1. Unity で本パッケージを Import + 本 sample を有効化。
    2. 本 spec の `CameraSwitcherOutputAdapterBootstrapper` を `OutputSceneBootstrapper` シーンに配置。
    3. PlayMode 起動 → Sample 側 `uOscClient` で送信開始。
    4. UI 不在の状態で、テストハーネス側から `IOutputCommandDispatcher` モック経由で `camera/command add` を注入し cameraId を採番。
    5. Hierarchy で `CamerasRoot/Camera-cam-0001-...` の transform が Sample 側の値に追従していることを目視確認。
    6. PlayMode 停止 → リソース解放を確認。
  - 観測可能な完了状態: README どおりに手順を踏むと OSC 受信 + Camera 適用が動作することを目視確認できる。
  - _Requirements: 13.5, 13.6_
  - _Depends: 4.4, 5.1, 5.2_
  - _Boundary: Samples~/MockedOscSenderSample_

- [ ]* 5.7 (P) パフォーマンスベースライン計測
  - `OscReceiveThroughputBenchmark`：1 cameraId × 60Hz × 60 秒で損失 0 を確認、UnityPerformanceTesting の `Measure.Method` で `Apply` の per-call ms を測定。
  - `CameraApplyAllocationBenchmark`：Profiler API で 1 適用あたりの GC.Allocations を計測、フレームあたり 0 アロケーション目標。
  - `VolumeMetadataResolverBenchmark`：初回 Reflection 時間（10ms 想定）、キャッシュヒット時間（数 µs 想定）。
  - 結果を README に記録し、以降の劣化検知の基準値とする。
  - 観測可能な完了状態: 3 計測結果が README に追記される。
  - _Requirements: 2.8, 2.9, 7.5_
  - _Depends: 4.4_
  - _Boundary: PerformanceBaseline_
