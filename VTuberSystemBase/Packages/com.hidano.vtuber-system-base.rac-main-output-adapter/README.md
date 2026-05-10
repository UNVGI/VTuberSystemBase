# com.hidano.vtuber-system-base.rac-main-output-adapter

VTuberSystemBase のメイン出力プロセスに常駐し、`character-selection-tab` Contracts asmdef が定める IPC（`slot/{id}/assignment` / `slot/{id}/settings/{key}` / `slot/{id}/command` / `avatars/{key}/schema`）を受信して `com.hidano.realtimeavatarcontroller`（RAC）の `SlotManager` を駆動する **メイン出力側アダプタ**。`output-renderer-shell` の `IOutputCommandDispatcher` にハンドラを登録するパスのみを利用し、`output-renderer-shell` 自体には変更を加えない。

## 概要

- 受信:
  - `slot/{id}/assignment`（state, UI→出力）→ `SlotManager.AddSlotAsync` / `RemoveSlotAsync`
  - `slot/{id}/settings/{key}`（state, UI↔出力）→ `IAvatarSettingsAdapter.Apply`
  - `slot/{id}/command`（event, UI→出力）→ Reset / Reload / PresetApply
  - `avatars/{key}/schema`（request, UI→出力, 同期 5 秒以内）→ `IAvatarSchemaProvider.Resolve`
- 送信:
  - `slots/catalog`, `avatars/catalog`（state, 出力→UI）
  - `slot/{id}/status`（state, 出力→UI、`Empty` / `Assigning` / `Assigned` / `Error`）
  - `slot/{id}/error`（event, 出力→UI、`KeyNotFound` / `MotionPipelineInit` / `ApplyFailed` / `Unknown`）

## 依存

- `com.hidano.vtuber-system-base.core-ipc-foundation` 0.1.0（`MessageKind`, `MessageEnvelope`, `ICoreIpcBus`）
- `com.hidano.vtuber-system-base.output-renderer-shell` 0.1.0（`IOutputCommandDispatcher`, `IOutputSceneRoots`）
- `com.hidano.vtuber-system-base.character-selection-tab` 0.1.0（Contracts asmdef のみ参照、UI 側 Runtime asmdef は参照しない）
- `com.hidano.realtimeavatarcontroller` 0.2.0（`SlotManager`, `RegistryLocator`, `ISlotErrorChannel`, `BuiltinAvatarProviderConfig` 等）
- `com.unity.addressables` 2.0.0（既定 Resolver / Provider が同期取得で使用）

## 拡張点（差し替え可能）

| 拡張点 | 役割 | 既定実装 |
|--------|------|----------|
| `IAvatarKeyResolver` | `avatarKey` → `AvatarProviderDescriptor` 解決、利用可能アバター列挙 | `AddressablesAvatarKeyResolver`（Addressables ラベル `avatar` から `GameObject` Prefab を同期取得し `BuiltinAvatarProviderConfig` を動的生成） |
| `IAvatarSchemaProvider` | `avatarKey` → `AvatarSettingsSchemaPayload` 解決 | `AddressablesAvatarSchemaProvider`（Addressables アドレス `{avatarKey}.schema` から `AvatarSchemaScriptableObject` を同期取得） |
| `IAvatarSettingsAdapter` | アバター GameObject への個別設定の適用 | `NoOpAvatarSettingsAdapter`（**全キー UnknownKey を返す。利用者プロジェクトが必ず差し替える前提**） |
| `IMoCapSourceConfigFactory` | Slot 単位の `MoCapSourceDescriptor` 構築 | `StubMoCapSourceConfigFactory`（typeId = `Stub`、no-op） |

`RacMainOutputAdapterBootstrapper.OverrideServices(...)` 経由でメモリダブル / 利用者プロジェクト独自実装に差し替える。

## Addressables 規約（既定実装が要求）

- アバター Prefab には Addressables ラベル `avatar` を付与し、address を `{avatarKey}` に設定する。
- アバター個別設定スキーマは `AvatarSchemaScriptableObject` を生成し、address を `{avatarKey}.schema` に設定する（任意、未登録時は空スキーマフォールバック）。

## シーン配置

1. メイン出力シーンに `OutputSceneBootstrapper`（`output-renderer-shell`）を配置する。
2. 同シーンに `RacMainOutputAdapterHost` を配置し、SerializeField から `OutputSceneBootstrapper` を割り当てる。
3. 利用者プロジェクトが `ICoreIpcBusProvider` を実装した MonoBehaviour を提供し、`RacMainOutputAdapterHost` の Inspector から接続する。
4. `IMoCapSource` の Stub Factory（`SourceTypeId = "Stub"`）を `RegistryLocator.MoCapSourceRegistry` に登録するスクリプトを利用者プロジェクト側で用意する。RAC v0.2.0 は Stub Factory を同梱しないため、本 spec の `Tests/Doubles/InMemoryMoCapSourceRegistry.RegisterStub("Stub")` のような最小実装を参考に登録すること。

## テスト構造

- `Tests/Runtime/Doubles/`：`InMemoryDispatcher`, `RecordingMessageSink`, `InMemoryAvatarKeyResolver`, `InMemoryAvatarSchemaProvider`, `RecordingAvatarSettingsAdapter`, `StubAvatarProvider`, `StubMoCapSource`, `InMemoryProviderRegistry`, `InMemoryMoCapSourceRegistry`, `RacRegistryFixture`, `ManualClock`, `FakeDiagnosticsLogger`。
- `Tests/Runtime/Domain/`：純関数ユニットテスト（Mapper / Decoder / Validator）。
- `Tests/Runtime/Defaults/`：既定実装の単体テスト。
- `Tests/Runtime/Integration/`：`InMemoryDispatcher` + 実 `SlotManager` + Stub Provider/Source による経路統合テスト。

## 既知の制約

- VMC 受信、uOSC 経由の MoCap 取り込みは本 spec のスコープ外。`com.hidano.realtimeavatarcontroller.mocap-vmc`（将来パッケージ）の導入時に `IMoCapSourceConfigFactory` を差し替えて利用する想定。
- `IAvatarSchemaProvider.Resolve` は同期実行が前提（`IOutputCommandDispatcher.RegisterRequestHandler` の制約）。重い実装は事前ロード戦略を採用すること。経過時間が `RacMainOutputAdapterConfig.SchemaProviderSlowThresholdMs`（既定 4000ms）を超えると `SchemaProvider.Slow` 診断ログが残る。
- 既定 `NoOpAvatarSettingsAdapter` は全キー `UnknownKey` を返すフォールバック。利用者プロジェクトは `IAvatarSettingsAdapter` を必ず差し替えること。
- `output-renderer-shell` の `IOutputCommandDispatcher` は受信専用 API のため、本 spec は送信に `ICoreIpcBus.PublishState` / `PublishEvent` を直接呼び出す。`ICoreIpcBusProvider` 経由でホスト MonoBehaviour に注入する設計。
