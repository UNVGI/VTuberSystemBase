# Implementation Plan

> 本タスクは design.md の「Components and Interfaces」「System Flows」「File Structure Plan」「Requirements Traceability」に基づく。依存方向は Abstractions → Domain → ExtensionPoints → Defaults → Receivers / Senders → Composition Root を厳守する。TDD を基本とし、各 Receiver / Sender 実装前にテストダブルと失敗テストを先行整備する。`character-selection-tab` Contracts asmdef（GUID `1e7b25ecbf9f4963b5275a52b2623640`）は **新規作成せず、参照のみ** 行う。

## 1. Foundation: パッケージ雛形と参照境界の確立

- [ ] 1.1 UPM パッケージ骨格と asmdef 境界の確立
  - UPM パッケージ（`jp.hidano.vtuber-system-base.rac-main-output-adapter`）の `package.json` を作成し、`dependencies` に `com.hidano.vtuber-system-base.core-ipc-foundation` / `com.hidano.vtuber-system-base.output-renderer-shell` / `jp.hidano.vtuber-system-base.character-selection-tab` / `com.hidano.realtimeavatarcontroller`（バージョンは manifest と一致させる）を列挙する。
  - Runtime asmdef（`VTuberSystemBase.RacMainOutputAdapter.Runtime`）を作成し、`references` に GUID 参照で以下のみを列挙する：`286be82527bb75547a774598be8243ab`（CoreIpc.Abstractions）/ `8dd1f7ecef3d4c6cae1a52cee5304e5f`（OutputRendererShell.Runtime）/ `1e7b25ecbf9f4963b5275a52b2623640`（CharacterSelectionTab.Contracts）/ RAC `RealtimeAvatarController.Core` の name 参照（GUID 取得して GUID 参照に揃える）。`overrideReferences = true`、`precompiledReferences` に `System.Text.Json.dll` / `System.Text.Encodings.Web.dll` / `System.Runtime.CompilerServices.Unsafe.dll`、`noEngineReferences = false`、`autoReferenced = true` を設定する。
  - Tests.Runtime asmdef（`VTuberSystemBase.RacMainOutputAdapter.Tests.Runtime`）と Tests.Editor asmdef（同 `.Tests.Editor`）を作成し、`InternalsVisibleTo` を Runtime asmdef に対して有効化する。
  - パッケージルート / Runtime / Tests / Samples~ 各フォルダの `.meta` と asmdef `.meta` の GUID は **PowerShell `[guid]::NewGuid().ToString('N')` で都度生成** する。連続パターン・派生 GUID は禁止（CLAUDE.md ルール）。
  - 観測可能な完了条件: Unity 6.3 プロジェクトに本パッケージを配置するとコンパイルエラーなしでロードされ、禁止参照（`character-selection-tab` Runtime asmdef、UI 側 asmdef、core-ipc 具体実装）を加えるとコンパイルエラーとなることを確認できる。
  - _Requirements: 1.1, 1.2, 1.3_
  - _Boundary: PackageRoot, Runtime/asmdef_

- [ ] 1.2 Domain 値型と列挙の定義
  - `AdapterApplyResult.cs`（`Applied` / `UnknownKey` / `OutOfRange` / `Failed`）と `PendingSettingValue.cs`（`(string SettingKey, SettingType Type, JsonElement Value, long ReceivedAtUnixMs)`）を実装する。
  - `RacMainOutputAdapterConfig.cs` を実装し、`SchemaProviderSlowThresholdMs = 4000` / `MaxErrorDetailLength = 512` / `PendingPublishQueueCapacity = 16` の既定値を持つ。
  - `IClock` / `DefaultClock`（`DateTimeOffset.UtcNow`）を実装する。
  - 観測可能な完了条件: 各値型の生成・等価性・既定値テストが緑色で通る。
  - _Requirements: 1.8, 8.7_
  - _Depends: 1.1_

- [ ] 1.3 Domain ロジック（Mapper / Decoder / Validator）の実装
  - `SlotStateMapper.Map(SlotState, bool isAssigning)` を `"Empty"` / `"Assigning"` / `"Assigned"` / `"Error"` 文字列に翻訳するロジックで実装する。
  - `SlotErrorCodeMapper.Map(SlotErrorCategory, Exception)` を design.md §Data Models のマップで実装する。例外型名のパターンマッチ（"Addressable" / "AvatarKey" → `KeyNotFound`、"MoCap" / "Source" → `MotionPipelineInit`）。
  - `SettingValueDecoder.Decode(SettingType, JsonElement)` を `float` / `int` / `bool` / `Color` / `string`（Enum）/ `Vector3` 各 6 型で実装する。型不一致は `InvalidOperationException`、未知 `SettingType` は `null` を返す。
  - `AvatarKeyValidator.Validate(string)` を `CharacterTopics.Safe` 互換の許容文字種（ASCII 英数 + `-_.`）で検証する。
  - 観測可能な完了条件: 各 Mapper / Decoder / Validator の単体テストが正常系・異常系を網羅して緑色で通る。
  - _Requirements: 2.5, 2.6, 2.9, 3.5, 3.6, 7.2, 7.3_
  - _Boundary: Domain/SlotStateMapper, Domain/SlotErrorCodeMapper, Domain/SettingValueDecoder, Domain/AvatarKeyValidator_
  - _Depends: 1.2_

- [ ] 1.4 ログカテゴリ定数と Diagnostics スナップショット型の定義
  - `AdapterLogCategories.cs` を実装し、`Bootstrap` / `Assignment` / `Settings` / `Command` / `SchemaProvider` / `Catalog` / `Error` / `Lifecycle` の文字列定数を公開する。
  - `RacAdapterDiagnosticsSnapshot.cs`（record struct）を design.md のフィールド構成で実装する。
  - `IRacMainOutputAdapterDiagnostics.cs`（`Capture()` のみ）を定義する。
  - 観測可能な完了条件: ログカテゴリが重複なく列挙され、Snapshot 構造体が `record struct` として `Equals` / `ToString` を備えていることを確認できる。
  - _Requirements: 10.1, 10.7, 10.8_
  - _Depends: 1.2_

## 2. Extension Points と既定実装

- [ ] 2.1 Extension Point インタフェース 4 種の定義
  - `IAvatarKeyResolver.cs`（`Resolve` / `AvatarKeys` / `Refresh` / `OnAvatarKeysChanged`）を実装する。
  - `IAvatarSchemaProvider.cs`（`Resolve(string) → AvatarSettingsSchemaPayload?`）を実装する。
  - `IAvatarSettingsAdapter.cs`（`Apply(GameObject, string, SettingType, JsonElement) → AdapterApplyResult`）を実装する。
  - `IMoCapSourceConfigFactory.cs`（`Build(string) → MoCapSourceDescriptor`）を実装する。
  - 観測可能な完了条件: 各インタフェースが `public` で公開されており、テストダブル（次の Tasks 4）から実装可能であることを確認できる。
  - _Requirements: 8.1, 8.2, 8.3, 8.4_
  - _Boundary: ExtensionPoints/*_
  - _Depends: 1.2, 1.3_

- [ ] 2.2 (P) `NoOpAvatarSettingsAdapter` の実装
  - 全 `(avatar, settingKey, type, value)` に対して `AdapterApplyResult.UnknownKey` を返す既定実装を作成する。
  - 観測可能な完了条件: 任意入力で `UnknownKey` を返すことを単体テストで確認できる。利用者プロジェクトが差し替えるべきサインとして README に明記する。
  - _Requirements: 8.3, 3.4_
  - _Boundary: Defaults/NoOpAvatarSettingsAdapter_
  - _Depends: 2.1_

- [ ] 2.3 (P) `StubMoCapSourceConfigFactory` の実装
  - RAC 同梱の Stub MoCap Source Descriptor（`MoCapSourceDescriptor` の最小値、`SourceTypeId = "Stub"`）を返す既定実装を作成する。
  - `MoCapSourceConfigBase` を継承する Stub Config（あるいは RAC 既存の Stub Config）を `ScriptableObject.CreateInstance` で動的生成する。
  - 観測可能な完了条件: 任意 `slotId` で同一構造の Descriptor を返すことを単体テストで確認できる。
  - _Requirements: 8.4_
  - _Boundary: Defaults/StubMoCapSourceConfigFactory_
  - _Depends: 2.1_

- [ ] 2.4 (P) `AddressablesAvatarKeyResolver` の実装
  - `IAvatarKeyResolver.Resolve(avatarKey)` で Addressables の存在判定（`LoadResourceLocationsAsync(avatarKey, typeof(GameObject))`）を行い、`BuiltinAvatarProviderConfig` を `ScriptableObject.CreateInstance` で動的生成して `AvatarProviderDescriptor { ProviderTypeId = "Builtin", Config = config }` を返す。
  - `Refresh()` で全 avatar key を `LoadResourceLocationsAsync` 経由で再列挙し `AvatarKeys` キャッシュを更新、変更があれば `OnAvatarKeysChanged` を発火する。
  - Addressables カタログが空 / 未ロードのときは空配列を返し、`Resolve` は `null` を返す（呼出側で `KeyNotFound` 翻訳）。
  - 観測可能な完了条件: テスト用 Addressables Group を持つ Editor テストで、登録キーが `Resolve` で解決され、未登録キーが `null` を返し、`Refresh` 後に `AvatarKeys` が更新されることを確認できる。
  - _Requirements: 8.1, 6.4, 6.6_
  - _Boundary: Defaults/AddressablesAvatarKeyResolver_
  - _Depends: 2.1_

- [ ] 2.5 (P) `AddressablesAvatarSchemaProvider` の実装
  - `Resolve(avatarKey)` で `Addressables.LoadAssetAsync<ScriptableObject>($"{avatarKey}.schema").WaitForCompletion()` を同期呼び出しし、ScriptableObject から `AvatarSettingsSchemaPayload` を構築する。
  - スキーマ用 ScriptableObject 型は本 spec で `AvatarSchemaScriptableObject`（`Settings: List<SettingSchemaEntrySerializable>`）を提供し、利用者プロジェクトが Addressables に登録する。
  - `WaitForCompletion` 失敗時は `null` を返し、診断ログに `SchemaProvider.Fallback(avatarKey)` を残す（呼出側で空応答に翻訳）。
  - 観測可能な完了条件: Editor テストで登録 ScriptableObject から `AvatarSettingsSchemaPayload` が構築され、未登録キーで `null` を返すことを確認できる。
  - _Requirements: 8.2, 5.6, 5.7_
  - _Boundary: Defaults/AddressablesAvatarSchemaProvider_
  - _Depends: 2.1, 1.2_

## 3. テストダブル群の整備（TDD 基盤）

- [ ] 3.1 (P) `InMemoryDispatcher` の実装
  - `IOutputCommandDispatcher` を実装し、`Dictionary<(string topic, MessageKind kind), Delegate>` を内部に持ち、`RegisterStateHandler` / `RegisterEventHandler` / `RegisterRequestHandler` で登録、戻り値の `OutputCommandHandlerRegistration` で解除可能にする。
  - テスト用 API として `EmitState<T>(string topic, T payload)`、`EmitEvent<T>(string topic, T payload)`、`EmitRequest<TReq, TRes>(string topic, TReq payload, out TRes response)` を提供する。
  - `_sentMessages: List<(string topic, MessageKind kind, object payload)>` を保持し、Applier / Sender が `PublishState` / `PublishEvent` 相当の操作で記録した送信履歴を取り出せる API（`GetSent(string topic)` 等）を提供する。
  - 観測可能な完了条件: 登録 → Emit → ハンドラ実行 → 解除 → Emit が no-op となる動作と、重複 `(topic, kind)` 登録で `InvalidOperationException` が出る動作が単体テストで確認できる。
  - _Requirements: 11.1, 8.6_
  - _Boundary: Tests/Doubles/InMemoryDispatcher_
  - _Depends: 1.1_

- [ ] 3.2 (P) `InMemoryAvatarKeyResolver` / `InMemoryAvatarSchemaProvider` / `RecordingAvatarSettingsAdapter` の実装
  - `InMemoryAvatarKeyResolver` は `Dictionary<string, AvatarProviderDescriptor>` を持ち、`Resolve` / `AvatarKeys` を即時返却。`Refresh` は引数で渡された辞書で置き換えてイベント発火。
  - `InMemoryAvatarSchemaProvider` は `Dictionary<string, AvatarSettingsSchemaPayload>` を持ち、`Resolve` で取得（未登録時 null）。例外シミュレーション用 `_throwOnKeys: HashSet<string>` を持つ。
  - `RecordingAvatarSettingsAdapter` は `Apply` 呼出を `_calls: List<(GameObject avatar, string key, SettingType type, JsonElement value, AdapterApplyResult result)>` に記録、結果は事前設定可能（`SetResult(key, AdapterApplyResult)`）。
  - 観測可能な完了条件: 各ダブルが事前設定通りに動作し、Receivers のテストから注入できることを確認できる。
  - _Requirements: 11.3_
  - _Boundary: Tests/Doubles/*_
  - _Depends: 2.1_

- [ ] 3.3 (P) `StubAvatarProvider` / `StubMoCapSource` / `InMemoryProviderRegistry` / `InMemoryMoCapSourceRegistry` の実装
  - `StubAvatarProvider`：`IAvatarProvider`、`RequestAvatar` / `RequestAvatarAsync` で空の `GameObject`（new GameObject($"Stub-{slotId}")）を返し、`ReleaseAvatar` で `Object.Destroy`。
  - `StubMoCapSource`：`IMoCapSource`、`Initialize` で内部フラグ立て、`Tick` で何もしない。
  - `InMemoryProviderRegistry` / `InMemoryMoCapSourceRegistry`：`Resolve(descriptor)` で事前登録された Stub を返す。`Release` で参照カウントを管理。
  - `RegistryLocator.OverrideProviderRegistry` / `OverrideMoCapSourceRegistry` で注入できる構造とし、`SetUp` / `TearDown` で `RegistryLocator.ResetForTest` を呼ぶヘルパを `Tests/Doubles/RacRegistryFixture.cs` で提供する。
  - 観測可能な完了条件: `SlotManager.AddSlotAsync` を Stub 経由で呼び出すと Slot が `Active` に遷移し、`RemoveSlotAsync` で `Disposed` に戻ることを単体テストで確認できる。
  - _Requirements: 11.2, 11.7_
  - _Boundary: Tests/Doubles/*_
  - _Depends: 1.1_

- [ ] 3.4 (P) `ManualClock` / `StubMoCapSourceConfigFactoryForTests` / `FakeDiagnosticsLogger` の実装
  - `ManualClock`：`IClock`、内部時刻を `_now` として保持、`Advance(TimeSpan)` で進める。
  - `StubMoCapSourceConfigFactoryForTests`：`IMoCapSourceConfigFactory`、テスト用 Stub Descriptor を返す。
  - `FakeDiagnosticsLogger`：`IDiagnosticsLogger`（`ui-toolkit-shell` 由来、未使用なら本 spec 内で定義）、ログ履歴を `_entries: List<(string category, string message, ...)>` に記録。
  - 観測可能な完了条件: 各ダブルが Receivers / Senders のテストから注入可能で、ログが期待カテゴリで記録される。
  - _Requirements: 11.6, 11.7_
  - _Boundary: Tests/Doubles/*_
  - _Depends: 1.4_

## 4. Senders（送信層）の実装

- [ ] 4.1 `SlotStatusPublisher` の実装
  - `IOutputCommandDispatcher.PublishState` 経由で `slot/{slotId}/status` を発行するヘルパクラスを実装する。
  - `Publish(slotId, status, detail)` で `SlotStatusPayload { Status, Detail }` を構築し、`CharacterTopics.SlotStatus(slotId)` topic で送信する。
  - `IClock` を注入し送信時刻のログ記録に使う。
  - 観測可能な完了条件: `InMemoryDispatcher` を経由した Publish で、期待 topic と payload が記録される単体テストが通る。
  - _Requirements: 2.2, 2.3, 2.6, 7.7_
  - _Boundary: Senders/SlotStatusPublisher_
  - _Depends: 3.1_

- [ ] 4.2 `SlotErrorTranslator` の実装
  - `ISlotErrorChannel.Errors` を `.ObserveOnMainThread()` で購読し、`SlotError` を `slot/{slotId}/error` event に翻訳する。
  - `SlotErrorCodeMapper.Map` で `ErrorCode` を決定し、`Detail` には `category=...; type=...; message=...` を 512 文字までトリムして詰める。
  - `PublishError(slotId, errorCode, detail)` 直接呼出 API も提供する（Applier から例外時に呼ぶ）。
  - error 送信時に `SlotStatusPublisher.Publish(slotId, "Error", detail)` を併せて発火する。
  - `Disposed` 状態の Slot へのエラーは publish するが警告ログを残す。
  - 二次例外は最終 `catch` で握り潰し、Unity Console に警告。
  - 観測可能な完了条件: `ISlotErrorChannel.Publish(SlotError)` を流すと InMemoryDispatcher に `slot/{id}/error` event と `slot/{id}/status = Error` の 2 件が記録される単体テストが通る。
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 2.5, 2.6_
  - _Boundary: Senders/SlotErrorTranslator_
  - _Depends: 1.3, 4.1, 3.1_

- [ ] 4.3 `PendingPublishQueue` と `SlotCatalogPublisher` の実装
  - `PendingPublishQueue`：IPC 受信開始前に `slots/catalog` / `avatars/catalog` の publish を保留するキュー。`Enqueue(Action)` / `Flush(IOutputCommandDispatcher)` を提供。容量超過は警告ログ + 古い順に破棄。
  - `SlotCatalogPublisher` は `SlotManager.OnSlotStateChanged` を購読し、`Created` / `Active` / `Disposed` 遷移で `PublishState slots/catalog` を実行する。
  - 1 フレーム内に複数遷移があった場合は次フレーム冒頭で 1 回だけ publish する（Coroutine または `IClock` ベースの遅延）。上流 D-7 coalesce との整合を取る。
  - `OnSlotAdded` / `OnSlotRemoved` / `OnSlotStateChanged` イベントを公開し、Receivers が動的登録の動的増減に追従できるようにする。
  - publish 失敗（`InMemoryDispatcher` で例外を投げる等）は次回更新で自然リトライ。
  - 観測可能な完了条件: `SlotManager` に Stub Slot を 3 件追加 → `slots/catalog` が 1 回（または最大 2 回まで coalesce 後）publish され、各 Slot 削除で正しい Entry 配列が出る統合テストが通る。
  - _Requirements: 6.1, 6.3, 6.5, 6.7_
  - _Boundary: Senders/SlotCatalogPublisher, Internal/PendingPublishQueue_
  - _Depends: 3.1, 3.3_

- [ ] 4.4 `AvatarCatalogPublisher` の実装
  - `IAvatarKeyResolver.OnAvatarKeysChanged` を購読し、変化があれば `avatars/catalog` を publish する。初回は `PendingPublishQueue` 経由で `Initialize` 時に enqueue する。
  - `AvatarCatalogEntry` の `DisplayName` は Resolver 由来の値、空ならば `AvatarKey` をフォールバックとして詰める。
  - `OnAvatarAdded` / `OnAvatarRemoved` イベントを公開し、`AvatarSchemaResponder` が動的登録に追従できるようにする。
  - 観測可能な完了条件: `InMemoryAvatarKeyResolver` で 3 → 4 → 2 件の変化を発生させると、Dispatcher に `avatars/catalog` の publish が 3 回（初回 + 2 回の変化）記録される単体テストが通る。
  - _Requirements: 6.2, 6.4, 6.6, 6.7_
  - _Boundary: Senders/AvatarCatalogPublisher_
  - _Depends: 3.1, 3.2_

## 5. Receivers（受信層）の実装

- [ ] 5.1 `SlotAssignmentApplier` の実装
  - `RegisterDynamic(slotId)` で `IOutputCommandDispatcher.RegisterStateHandler<SlotAssignmentPayload>(CharacterTopics.SlotAssignment(slotId))` を登録する。`UnregisterDynamic(slotId)` で Registration.Dispose。
  - 受信ハンドラで `AvatarKeyValidator.Validate(payload.AvatarKey)`（null は許容、空は拒否）。
  - `null` AvatarKey → `RemoveSlotAsync(slotId)` + `slot/{id}/status = Empty` publish。
  - 値あり AvatarKey → `Assigning` publish → `IAvatarKeyResolver.Resolve` → `IMoCapSourceConfigFactory.Build` → `SlotSettings` 動的生成 → `AddSlotAsync` await → `Assigned` publish。
  - 既存 Active Slot に対して新 AvatarKey 受信時は `RemoveSlotAsync` → `AddSlotAsync` の **直列** 実行（`SemaphoreSlim` 1 で同一 slotId を直列化）。
  - Resolve null → `slot/{id}/error` で `KeyNotFound` + `status = Error` publish、`AddSlotAsync` をスキップ。
  - 例外捕捉 → `SlotErrorTranslator.PublishError(slotId, "Unknown", exception type/message)`、Dispatcher は継続。
  - `ReloadAsync(slotId)` を実装し、`SlotCommandApplier` から呼ばれた際に現 AvatarKey を保持したまま Remove → Add を実行する。
  - 観測可能な完了条件: `InMemoryDispatcher.EmitState slot/A1/assignment {AvatarKey:"miku"}` で SlotManager に Slot が追加され、`slot/A1/status = Assigning → Assigned` が 2 件 publish され、null assignment で削除されるラウンドトリップが緑色で通る。
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9, 4.3 (Reload 委譲)_
  - _Boundary: Receivers/SlotAssignmentApplier_
  - _Depends: 3.1, 3.2, 3.3, 4.1, 4.2_

- [ ] 5.2 `SlotSettingsApplier` の実装
  - `OnSchemaResolved(slotId, avatarKey, schema)` で当該 avatarKey の各 settingKey に対して `RegisterStateHandler<SlotSettingValuePayload>(CharacterTopics.SlotSettingValue(slotId, settingKey))` を動的登録する。
  - 受信ハンドラで `SlotManager.TryGetSlotResources(slotId, out source, out avatar)` を試行。`Active` であれば `SettingValueDecoder.Decode` → `IAvatarSettingsAdapter.Apply` を呼ぶ。
  - 非 Active（Empty / Assigning / Disposed）であれば `_pendingSettings[(slotId, avatarKey)][settingKey] = PendingSettingValue` に格納（key 単位 last-write-wins）。
  - `OnSlotStateChanged(slotId, prev, next, avatarKey)` で `next == Active` のとき保留バッファを flush、`next == Disposed` で `(slotId, avatarKey)` のバッファを破棄。アバター差替（`avatarKey` 変化）も同様に旧バッファを破棄。
  - `AdapterApplyResult.Failed` → `SlotErrorTranslator.PublishError(slotId, "ApplyFailed", ...)`。`UnknownKey` / `OutOfRange` は警告ログのみ。
  - `SettingType` 未知値は警告ログ + スキップ（前方互換）。
  - 例外捕捉 → ログ + `ApplyFailed` publish、Dispatcher 継続。
  - 観測可能な完了条件: 保留 → flush ラウンドトリップ、アバター差替時のバッファ破棄、UnknownKey の警告のみ動作、ApplyFailed の error publish が個別テストで緑色で通る。
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.8, 3.9_
  - _Boundary: Receivers/SlotSettingsApplier_
  - _Depends: 1.3, 3.1, 3.2, 3.3, 4.2, 5.1_

- [ ] 5.3 `SlotCommandApplier` の実装
  - `RegisterDynamic(slotId)` で `RegisterEventHandler<SlotCommandPayload>(CharacterTopics.SlotCommand(slotId))` を登録する。
  - `Kind == "Reset"` → `SlotManager.RemoveSlotAsync(slotId)` + `Empty` status publish。
  - `Kind == "Reload"` → `SlotAssignmentApplier.ReloadAsync(slotId)` を呼ぶ。Empty 状態なら no-op + 警告ログ。
  - `Kind == "PresetApply"` → 情報ログのみで no-op（`Argument` フィールドは記録）。
  - 未知 Kind → 警告ログ + スキップ。
  - 例外捕捉 → `slot/{id}/error{Unknown}` + ログ。
  - 観測可能な完了条件: 各 Kind 受信時の動作（Reset / Reload / PresetApply）が個別テストで期待動作を再現する。
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7_
  - _Boundary: Receivers/SlotCommandApplier_
  - _Depends: 3.1, 3.3, 4.2, 5.1_

- [ ] 5.4 `AvatarSchemaResponder` の実装
  - `RegisterDynamic(avatarKey)` で `RegisterRequestHandler<AvatarSchemaRequestPayload, AvatarSettingsSchemaPayload>(CharacterTopics.AvatarSchema(avatarKey))` を登録する。
  - 受信ハンドラで `Stopwatch.StartNew()` → `IAvatarSchemaProvider.Resolve(payload.AvatarKey)` を同期実行 → 経過時間計測。
  - null 結果 → 空配列の `AvatarSettingsSchemaPayload` を返却 + `SchemaProvider.Fallback(avatarKey)` ログ。
  - 経過時間 > `Config.SchemaProviderSlowThresholdMs` で `SchemaProvider.Slow(elapsed)` ログ。
  - 例外捕捉 → 空配列応答 + `SchemaProvider.Failed(avatarKey, exception)` ログ。
  - 解決成功時に `SlotSettingsApplier.OnSchemaResolved(slotId, avatarKey, schema)` を **解決した avatarKey に対応する全 active Slot** に対して通知（settingKey 動的登録の発火）。slotId 不明時は遅延発火（次回 Slot Active 遷移時に再発火）。
  - 観測可能な完了条件: 解決成功 / null / 例外 / Slow / 同期実行が `InMemoryAvatarSchemaProvider` 経由でラウンドトリップ検証できる単体テストが通る。
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - _Boundary: Receivers/AvatarSchemaResponder_
  - _Depends: 3.1, 3.2, 3.4, 5.2_

## 6. Composition Root と Lifecycle 統合

- [ ] 6.1 `RacMainOutputAdapterBootstrapper` の実装
  - コンストラクタで `RacMainOutputAdapterConfig` を受け取り、`OverrideServices(...)` で 8 つの依存（Dispatcher / SceneRoots / KeyResolver / SchemaProvider / SettingsAdapter / MoCapFactory / Clock / Logger）を任意差し替え可能にする。
  - `Initialize()` で順序固定 Step を実行する：
    1. 拡張点が未注入なら既定実装（`AddressablesAvatarKeyResolver` / `AddressablesAvatarSchemaProvider` / `NoOpAvatarSettingsAdapter` / `StubMoCapSourceConfigFactory` / `DefaultClock`）を採用。
    2. `RegistryLocator.ProviderRegistry` / `MoCapSourceRegistry` / `ErrorChannel` を取得。
    3. `SlotManager` を `new SlotManager(providerReg, sourceReg, errorChannel)` で生成。
    4. `SlotStatusPublisher` / `SlotErrorTranslator` / `SlotCatalogPublisher` / `AvatarCatalogPublisher` を生成。
    5. `SlotAssignmentApplier` / `SlotSettingsApplier` / `SlotCommandApplier` / `AvatarSchemaResponder` を生成。
    6. `SlotErrorTranslator.StartObserving()` で `ISlotErrorChannel` 購読開始。
    7. `SlotCatalogPublisher.StartObserving()` で `SlotManager.OnSlotStateChanged` 購読開始。
    8. `AvatarCatalogPublisher.StartObserving()` で `IAvatarKeyResolver.OnAvatarKeysChanged` 購読開始。
    9. `PendingPublishQueue.Flush(dispatcher)` で初回 catalog 2 件を送信。
    10. `Diagnostics.PhaseName = "Ready"`。
  - `Shutdown()` で逆順解放。`Diagnostics.Capture()` を最終ログに残す。冪等。
  - `IsRunning` プロパティで二重 Initialize を no-op 化、`Dispose()` は `Shutdown` 委譲。
  - 観測可能な完了条件: Bootstrap → Shutdown を 5 回繰り返してもハンドラ二重登録・購読リーク・GameObject 残留が発生しないことを `InMemoryDispatcher` の RegisteredHandlerCount ベースで確認できる。
  - _Requirements: 1.4, 1.5, 1.7, 1.8, 8.5, 8.7, 8.8, 9.4_
  - _Boundary: Bootstrapper/RacMainOutputAdapterBootstrapper_
  - _Depends: 4.1, 4.2, 4.3, 4.4, 5.1, 5.2, 5.3, 5.4, 1.4, 2.2, 2.3, 2.4, 2.5_

- [ ] 6.2 `RacMainOutputAdapterHost` MonoBehaviour と PlayMode ライフサイクル結線
  - シーンに 1 つ配置する `MonoBehaviour` ホストを実装し、`Awake` で `RacMainOutputAdapterBootstrapper` を生成、`Start` で `Initialize`（`OutputSceneBootstrapper.Start` が完了していることを `[DefaultExecutionOrder(100)]` で保証）、`OnDestroy` で `Shutdown`。
  - SerializeField で `OutputSceneBootstrapper` 参照を持ち、`IOutputCommandDispatcher` / `IOutputSceneRoots` を取得する経路を明示する（`OutputSceneBootstrapper` が公開する getter を経由）。
  - `Application.isPlaying` ガードで Edit モードでは Awake で何もしない（D-9）。
  - シーンに複数配置されている場合は `FindObjectsByType` で重複検知し、警告ログ + 自己破棄。
  - `PlayModeLifecycleHook`（Editor 限定）で `EditorApplication.playModeStateChanged` の `ExitingPlayMode` を購読し、`Bootstrapper.Shutdown` を冪等に呼び出す（OnDestroy より早期にフラッシュさせる保険）。
  - 観測可能な完了条件: PlayMode 開始でハンドラ登録 + catalog 初回 publish が観測でき、PlayMode 終了で全解除されることが手動と PlayMode テストの両方で確認できる。
  - _Requirements: 1.4, 1.5, 1.6, 1.7, 9.1, 9.2, 9.3, 9.5, 9.6, 9.7_
  - _Boundary: Bootstrapper/RacMainOutputAdapterHost, Bootstrapper/PlayModeLifecycleHook_
  - _Depends: 6.1_

## 7. Diagnostics と Observability

- [ ] 7.1 `RacMainOutputAdapterDiagnostics` の実装
  - `IRacMainOutputAdapterDiagnostics.Capture()` を実装し、`SlotManager.GetSlots()` / `SlotErrorTranslator` の最終エラー記録 / `IAvatarKeyResolver.AvatarKeys.Count` / Bootstrapper の `PhaseName` を集約して `RacAdapterDiagnosticsSnapshot` を返す。
  - `RegisteredHandlerCount` は本 spec 内で動的登録した Registration 数（catalog の動的増減を反映）。
  - スレッドセーフ（volatile or lock）で任意スレッドから取得可能にする。
  - 観測可能な完了条件: 5 件の Slot を追加 → Capture が `ActiveSlotCount = 5` を返し、エラー Slot 1 件で `ErrorSlotCount = 1` が反映される単体テストが通る。
  - _Requirements: 10.7_
  - _Boundary: Diagnostics/RacMainOutputAdapterDiagnostics_
  - _Depends: 6.1_

- [ ] 7.2 ログ出力の整備とログレベル切替
  - design.md §System Flows の各ステップで `IDiagnosticsLogger` 経由でログを出力する（`Bootstrap.Init.Start/Complete/Failed` / `Assignment.Receive(slotId, avatarKey)` / `Settings.Apply(slotId, key, type, result)` / `Command.Receive(slotId, kind)` / `SchemaProvider.{Slow|Fallback|Failed}` / `Catalog.Publish(topic, count)` / `Error.Publish(slotId, errorCode)` / `Lifecycle.{Start|Shutdown}`）。
  - ログレベルは `IDiagnosticsLogger` の設定経由で外部から切替可能にする（`character-selection-tab` Requirement 9 第 8 項と整合）。
  - メイン出力サーフェス（`OnGUI` / `IMGUI` / `UIDocument` 経由）には一切描画しないことを構造的に保証（asmdef レベルでこれら API を使わないコードレビュー方針）。
  - 観測可能な完了条件: 主要 5 シナリオ（Bootstrap / Assignment / Settings / Command / Schema）でログが期待カテゴリで `FakeDiagnosticsLogger` に記録される単体テストが通る。
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.8, 10.9_
  - _Boundary: 全 Receivers / Senders（横断的修正）_
  - _Depends: 6.1, 7.1_

## 8. Integration Tests と PlayMode 検証

- [ ] 8.1 Integration テスト: assignment ラウンドトリップ
  - `InMemoryDispatcher` + RAC 実 `SlotManager`（`InMemoryProviderRegistry` + `StubAvatarProvider`）で次のシナリオを統合テストする：
    1. `slot/A1/assignment {AvatarKey:"miku"}` Emit → SlotManager に `A1` Slot 追加 → `slot/A1/status = Assigning → Assigned` の 2 件記録 + `slots/catalog` に `A1` を含むエントリが publish される。
    2. `slot/A1/assignment {AvatarKey:"rin"}` Emit → Remove → Add 直列 → 最終 `Assigned` で `rin` が反映される。
    3. `slot/A1/assignment {AvatarKey:null}` Emit → Slot 削除 → `Empty` status + catalog 更新。
    4. 未解決 `AvatarKey:"unknown"` → `slot/A1/error{KeyNotFound}` + `status=Error`。
  - 観測可能な完了条件: 上記 4 シナリオが緑色で通る。
  - _Requirements: 11.1, 11.2, 11.5_
  - _Boundary: Tests/Integration/AdapterRoundTripTests, AvatarSwapSerializationTests_
  - _Depends: 6.1, 5.1, 4.3_

- [ ] 8.2 Integration テスト: settings 保留バッファと flush
  - 次のシナリオを統合テストする：
    1. Slot 未追加状態で `slot/A1/settings/colorTint {Type:Color, Value:[1,0,0,1]}` Emit → 保留バッファに格納、Adapter は呼ばれない。
    2. その後 `slot/A1/assignment {AvatarKey:"miku"}` Emit → Active 遷移 → 保留バッファ flush → `RecordingAvatarSettingsAdapter._calls` に `colorTint` 1 件記録。
    3. アバター差替（`miku` → `rin`）時に旧バッファが破棄されることを別 Settings Emit で確認。
  - 観測可能な完了条件: 各シナリオが緑色で通る。
  - _Requirements: 11.5, 3.3, 3.8_
  - _Boundary: Tests/Integration/PendingSettingsBufferTests_
  - _Depends: 5.2, 8.1_

- [ ] 8.3 Integration テスト: schema request 応答とエラー翻訳
  - Schema Request：登録キー解決成功 → schema 応答 / 未登録キー → 空応答 / Provider 例外 → 空応答 + Failed ログ、を統合テストする。
  - Error 翻訳：`RegistryLocator.ErrorChannel.Publish(SlotError(slotId, InitFailure, new AddressableLoadException(...)))` を直接発火 → `slot/{id}/error{KeyNotFound}` + `status=Error` が `InMemoryDispatcher` に記録されることを確認。
  - 観測可能な完了条件: 上記 4 シナリオが緑色で通る。
  - _Requirements: 11.5, 5.2, 5.3, 5.5, 7.1, 7.2_
  - _Boundary: Tests/Integration/ErrorChannelTranslationTests, Tests/Receivers/AvatarSchemaResponderTests_
  - _Depends: 5.4, 4.2_

- [ ] 8.4 PlayMode テスト: ライフサイクル反復とリーク検証
  - Editor PlayMode で `RacMainOutputAdapterHost` + `OutputSceneBootstrapper` + `InMemoryDispatcher` を含む最小シーンを構築し、PlayMode 開始 → 停止を 5 回繰り返す。
  - 各反復後に `Diagnostics.Capture()` を取得し、`RegisteredHandlerCount` / Slot GameObject 残留 / `ISlotErrorChannel` 購読数の不在を確認する。
  - 観測可能な完了条件: 5 回反復後にリーク不在が `[Test]` メソッドで自動検証され、緑色で通る。
  - _Requirements: 11.5, 9.4_
  - _Boundary: Tests/Integration/PlayModeLifecycleTests_
  - _Depends: 6.2, 7.1_

- [ ] 8.5 PlayMode サンプルシーンと手動検証手順
  - `Samples~/RacAdapterPlayModeSample/RacAdapterPlayModeSample.unity` を作成し、`OutputSceneBootstrapper` + `RacMainOutputAdapterHost` + 任意の `MockUiDriverScript`（`InMemoryDispatcher` を駆動して assignment / settings / command / schema を発火するスクリプト）を配置する。
  - README に手動検証手順（PlayMode 開始 → MockUi の各ボタンで Emit → メインカメラ前に Stub アバター GameObject が出現 / 消失 / 差替されることを目視確認）を整備する。
  - PlayMode 開始・停止 5 回の反復が手動でも確認できる手順を README に記載する。
  - 観測可能な完了条件: PlayMode でサンプルシーンを開くと本アダプタが Stub アセットでフル機能を再現し、README 記載手順で一連の挙動が再生できる。
  - _Requirements: 11.4_
  - _Boundary: Samples~/RacAdapterPlayModeSample_
  - _Depends: 8.4_

- [ ] 8.6* パフォーマンス / 負荷検証（任意）
  - Slot 8 件 × 各設定 10 項目でのスライダー連続操作（60Hz × 5 秒）において、`Time.unscaledDeltaTime` が 16.67ms を維持することを計測する（Requirement 9.7 のレイテンシ特性同一性の裏付け）。
  - スキーマ Request の同期実行（`AddressablesAvatarSchemaProvider` 経由）の P95 が 100ms 以下であることを計測する（Requirement 5.4 の Slow 閾値 4 秒の根拠）。
  - PlayMode 反復 100 回でメモリ消費が単調増加しないこと（リーク不在）を計測する（Requirement 9.4）。
  - 観測可能な完了条件: 計測レポートが 3 指標を記録し、しきい値を満たすか未達かを判定可能にする。
  - _Requirements: 5.4, 9.4, 9.7_
  - _Boundary: Tests/Performance（任意ディレクトリ）_
  - _Depends: 8.5_

## 9. Package Boundary 検証

- [ ] 9.1 asmdef 参照禁止リスト検証
  - `Tests/Editor/PackageBoundaryTests.cs` を実装し、Runtime asmdef の `references` に **禁止リスト**（`character-selection-tab` Runtime / 他タブ Runtime / 他出力アダプタ Runtime / `core-ipc-foundation` 具体実装 / `ui-toolkit-shell` Runtime）が含まれないことを Assembly-CSharp テストで検証する。
  - `RealtimeAvatarController.Core` 以外の RAC asmdef（`RealtimeAvatarController.Avatar.Builtin` 等）への直接参照も禁止し、利用者プロジェクト経由でのみアクセスする方針を確認する。
  - 観測可能な完了条件: 禁止リスト違反を意図的に追加すると `[Test]` が失敗することを手動で確認し、現状は緑色で通る。
  - _Requirements: 1.2_
  - _Boundary: Tests/Editor/PackageBoundaryTests_
  - _Depends: 1.1_

- [ ] 9.2 README とパッケージドキュメントの整備
  - パッケージ README を作成し、目的 / 依存関係 / `OverrideServices` の使用例 / 拡張点（`IAvatarKeyResolver` / `IAvatarSchemaProvider` / `IAvatarSettingsAdapter` / `IMoCapSourceConfigFactory`）の差し替え方 / Addressables 規約（`{avatarKey}` Prefab、`{avatarKey}.schema` ScriptableObject）/ 既知の制約（VMC 受信は別パッケージ）を記載する。
  - サンプル `RacAdapterPlayModeSample` への参照リンクを README に含める。
  - 観測可能な完了条件: README が他開発者に対して本 spec の利用方法と差し替え点を 1 ドキュメントで提示できる。
  - _Requirements: 8.1, 8.2, 8.3, 8.4_
  - _Boundary: README.md_
  - _Depends: 8.5_
