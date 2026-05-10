# Requirements Document

## Project Description (Input)
rac-main-output-adapter

`character-selection-tab`（spec #4）が UI 側で発行する IPC コマンド（state / event / request）をメイン出力プロセスで受信し、`com.hidano.realtimeavatarcontroller`（以下 RAC）の `SlotManager` を駆動してアバター割当・個別設定・離散操作を実シーンに反映する **メイン出力側アダプタ** を提供する spec。`output-renderer-shell` の `IOutputCommandDispatcher` にハンドラを登録する形で受信し、RAC が公開する `SlotManager` / `AvatarProviderDescriptor` / `MoCapSourceDescriptor` / `ISlotErrorChannel` 等の公開 API のみを使ってアバター生成・モーション結線・解放を行う。

スコープ:

- `character-selection-tab` の Contracts asmdef（`VTuberSystemBase.CharacterSelectionTab.Contracts`）に定義された topic / payload DTO を **そのまま参照** して受信ハンドラを登録する。新規 Contracts asmdef は作らない。
- 受信:
  - `slot/{id}/assignment`（state, UI→出力）→ RAC の `SlotManager.AddSlotAsync` / `RemoveSlotAsync` を呼び出してアバターを割当・解除する。
  - `slot/{id}/settings/{key}`（state, UI↔出力）→ 該当 Slot に紐付く RAC アバター GameObject 上のアバター設定値（ボーンスケール・表情バイアス等、利用者プロジェクトが拡張するもの）を適用する。
  - `slot/{id}/command`（event, UI→出力）→ `Reset` / `Reload` / `PresetApply` を実行する。
  - `avatars/{key}/schema`（request, UI→出力, 5 秒タイムアウト）→ アバター個別設定スキーマを応答する。
- 送信:
  - `slots/catalog`（state, 出力→UI）— RAC が公開する Slot 一覧を発行する（追加・削除・状態変化に追従）。
  - `avatars/catalog`（state, 出力→UI）— Addressables 由来のアバターカタログを発行する。
  - `slot/{id}/status`（state, 出力→UI）— Empty / Assigning / Assigned / Error の現状を発行する。
  - `slot/{id}/error`（event, 出力→UI）— 失敗事由を発行する（RAC `ISlotErrorChannel` 経由で観測される `SlotErrorCategory.InitFailure` / `ApplyFailure` / `RegistryConflict` 等）。
- メイン出力側に常駐する RAC ランタイム本体の起動・解放・GameObject 配置（`output-renderer-shell` の `IOutputSceneRoots.Characters` 配下）を所有する。

非目標:

- UI 側のタブ実装（`character-selection-tab` の責務）。
- `core-ipc-foundation` のトランスポート・シリアライゼーション（spec #1 の責務）。
- `output-renderer-shell` の `IOutputCommandDispatcher` 実装そのもの（spec #2 の責務、本 spec は受け側 API を **利用するのみ** で API 変更を加えない）。
- RAC ランタイム本体の機能追加・改修（採用パッケージをそのまま利用）。
- アバターアセット本体（VRM / Prefab / 設定スキーマアセット）— 利用者プロジェクトの責務。
- ステージ・ライト・カメラ・OSC（他出力側アダプタ spec の責務）。

対応要件: docs/requirements.md §3.2（UI/メイン出力分離）/ §5.1（タブ 1: キャラクター選択・設定画面）/ §6.2（配信適合性）

上位計画: docs/integration-plan.md Wave 3c（メイン出力側アダプタの実装、`character-selection-tab` 完成と `output-renderer-shell` 完成済み前提で並行 3 トラックの 1 トラック）

採用パッケージ: https://github.com/Hidano-Dev/RealtimeAvatarController v0.2.0（manifest.json で固定済み）

依存パッケージ:

- `com.hidano.vtuber-system-base.core-ipc-foundation`（Abstractions: `MessageKind`, `MessageEnvelope`, `IsExternalInit`）
- `com.hidano.vtuber-system-base.output-renderer-shell`（Abstractions: `IOutputCommandDispatcher`, `IOutputSceneRoots`, `IOutputDiagnostics`）
- `com.hidano.vtuber-system-base.character-selection-tab` — Contracts asmdef のみ（topic 定数 + payload DTO）
- `com.hidano.realtimeavatarcontroller` 0.2.0（`RealtimeAvatarController.Core.asmdef` を参照）

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応

言語: 日本語で生成（CLAUDE.md の規約に従う）

## Open Questions and Decisions (Dig)

本セクションは本 spec 固有の設計上の決定事項を記録する。上流 spec（`core-ipc-foundation`, `output-renderer-shell`, `character-selection-tab`）の決定（D-1 / D-3 / D-4 / D-5 / D-7 / D-9 / D-10 / D-11、OR-1 / OR-2、CS-1〜CS-13）は暗黙に継承される。本 spec 固有の暗黙デフォルトは以下のとおり。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| RA-1 | RAC ランタイム本体の所在 | **RAC `SlotManager` 等の本体は本 spec の Composition Root が所有する**。`output-renderer-shell` の `IOutputSceneRoots.Characters` 配下に各 Slot の GameObject を配置する。RAC 本体は `OutputSceneBootstrapper` のライフサイクル（PlayMode 開始〜停止）に従属する。 | CS-1 の継承（メイン出力側に RAC 本体を常駐）。`output-renderer-shell` の `Characters` ルートは「アバター／Slot 配置用」と明示されている。 | 低 |
| RA-2 | Contracts asmdef の所有者 | **本 spec は `character-selection-tab` の Contracts asmdef（`VTuberSystemBase.CharacterSelectionTab.Contracts`、GUID `1e7b25ecbf9f4963b5275a52b2623640`）を参照のみ行い、新たな Contracts asmdef を作らない**。topic / payload DTO の編集権は `character-selection-tab` 側にあり、本 spec は受信側として追従する。 | Contracts を 1 ソースに保つことで UI 側と出力側の二者契約が分裂しない。`character-selection-tab` の design.md および統合計画 §3.1 の "Contracts asmdef を 1 ソース" 方針と整合。 | 中（UI 側 Contracts の破壊的変更時は本 spec も再検証） |
| RA-3 | アバター解決方式 | **`AvatarKey` から RAC `AvatarProviderDescriptor` を構築する解決層（`IAvatarKeyResolver`）を本 spec に置き、Addressables 経由のアバター解決を担う**。利用者プロジェクトは `IAvatarProviderConfigFactory` を差し替えることで `BuiltinAvatarProviderConfig` 以外（Addressable Provider 等）を使える。本 spec は既定として「Addressables の `{avatarKey}` で `GameObject` を引く `BuiltinAvatarProviderConfig` 動的生成」を提供する。 | CS-4（Addressables key を一級識別子）の継承。RAC は `AvatarProviderDescriptor` 単位で Provider/Config を解釈する設計のため、UI が送る `AvatarKey` 文字列を Descriptor へ翻訳する層が必要。 | 中（Addressables 規約の整備が利用者責務、本 spec は規約違反を診断ログで報告） |
| RA-4 | MoCap ソースの既定 | **本 spec は `MoCapSourceDescriptor` の既定として「Stub MoCap Source」（RAC 既定の何もしないソース）を採用する**。実 MoCap ソース（VMC 等）は本 spec のスコープ外で、利用者プロジェクトが `IMoCapSourceConfigFactory` 経由で差し替える。 | docs/spec-breakdown.md および integration-plan.md は本フェーズで MoCap ソース選定を非目標としている。Slot 機構と UI 結線の検証は Stub Source で十分。VMC 受信は別パッケージ（`com.hidano.realtimeavatarcontroller.mocap-vmc`）で導入される将来拡張。 | 低 |
| RA-5 | アバター切替の RAC 操作 | **アバター切替は `SlotManager.RemoveSlotAsync` → `AddSlotAsync` の連続操作で実装する**。RAC は in-place な Avatar 差替 API を持たないため、本 spec は両者を直列に呼び出し、その間に `slot/{id}/status` を `Assigning` で送る（UI 側のローディング表示と同期）。 | RAC 0.2.0 の公開 API に in-place アバター差替が存在しない（`SlotHandle` は読取専用、`SlotSettings` は SO で `AddSlotAsync` の入力）。Remove → Add の直列化が現実的に唯一の経路。 | 中（差替時の "瞬間消失" を UI 側で `Assigning` 状態として吸収する責務分担を明確化） |
| RA-6 | settings/{key} の意味論 | **`slot/{id}/settings/{key}` の payload を本 spec の `IAvatarSettingsApplier` が解釈し、対応するアバター GameObject 上のコンポーネント（BlendShape, Animator パラメータ, ボーンスケール等）に適用する**。設定キーごとの具体適用ロジックは利用者プロジェクト（`IAvatarSettingsAdapter` 拡張点）が提供する。本 spec は「キー名 + 値型 + 範囲 + 既定値のメタデータ」を用いて適用フォールバック（未知キーは警告ログ + 無視）と検証を行う。 | CS-5（設定スキーマは RAC 側が権威）の本 spec 側具現化。RAC コアは表情・モーション以外の汎用設定 API を提供しないため、利用者プロジェクトでアバター個別の適用ロジックを差し込む拡張点が必要。 | 中（拡張点未定義のままだと利用者プロジェクトでアバター設定が反映されない。本 spec は「拡張点 + サンプル」を提供する） |
| RA-7 | スキーマ応答の権威 | **`avatars/{key}/schema` request への応答は、利用者プロジェクトが登録する `IAvatarSchemaProvider` を経由して構築する**。本 spec は既定 Provider として「Addressables の `{avatarKey}.schema` ScriptableObject から読み出す」または「空スキーマ（設定項目ゼロ件）」を返すフォールバックを実装する。タイムアウト 5 秒は `core-ipc-foundation` 上流契約に従う。 | CS-5（権威は RAC 側）の現実解。RAC v0.2.0 は設定スキーマ API を公開していないため、本 spec が Provider 拡張点を切り出して利用者プロジェクトに権威移譲する。 | 中（既定で空スキーマを返すと UI に項目が出ないが、設定機能の最小限は割当だけで成立する） |
| RA-8 | エラー伝播経路 | **RAC `ISlotErrorChannel` を購読して `SlotError` を受信し、`SlotErrorCategory` を `slot/{id}/error` payload の `ErrorCode` に翻訳して送信する**。`InitFailure` → `KeyNotFound`（要 Provider 解決失敗）/ `MotionPipelineInit`（要 MoCap 初期化失敗）/ `Unknown` を文脈で振り分け、`ApplyFailure` → `ApplyFailed`、`RegistryConflict` → `Unknown` + 詳細ログにマップする。 | CS-11 の継承（不可用アバター → empty + 警告）と `SlotErrorPayload` の `ErrorCode` 列挙（`KeyNotFound` / `MotionPipelineInit` / `ApplyFailed` / `Unknown`）の整合。 | 中（マップが粗いと UI 側のリカバリ動作が雑になる。詳細は `Detail` 文字列で補完） |
| RA-9 | catalog 送信の発火条件 | **`slots/catalog` は RAC `SlotManager.OnSlotStateChanged` ストリームを観測して状態が `Created` / `Active` / `Disposed` に遷移したタイミングで coalesced state として再発行する**。`avatars/catalog` は Addressables カタログ更新（`IAvatarKeyResolver.Refresh()` 完了）または PlayMode 開始時に再発行する。 | OR-2（last-write-wins）と D-7（coalesce）に整合。`SlotManager` の状態変化を IPC に橋渡しする経路を 1 本化することで、UI 側が独自ポーリングを行わずに済む。 | 低 |
| RA-10 | request ハンドラのスレッド | **`avatars/{key}/schema` request ハンドラは `IOutputCommandDispatcher` から Unity メインスレッド上で呼ばれる前提で実装する**（D-3 継承）。`IAvatarSchemaProvider` の同期 API を採用し、利用者プロジェクトが内部で I/O を行う場合でも 5 秒以内に応答するよう設計を促す（応答シンクの呼び出し時刻と request 受信時刻の差を診断ログに記録）。 | `IOutputCommandDispatcher.RegisterRequestHandler<TReq,TRes>` の戻り値は `Func<RequestCommand<TRequest>, TResponse>` であり同期戻り値型。非同期化は本 spec のスコープ外（必要なら別 PR で `Func<RequestCommand<TRequest>, ValueTask<TResponse>>` 拡張）。 | 中（重い Provider 実装が描画ループをブロックする可能性。XMLDoc で警告） |
| RA-11 | Domain Reload OFF 対応 | **本 spec の Composition Root は RAC `RegistryLocator.ResetForTest()` の挙動に従って PlayMode 開始のたびに `SlotManager` を新規生成し、PlayMode 終了で `Dispose` する**。Edit モード残留物を作らない（D-9 継承）。 | RAC 0.2.0 の Locator は `RuntimeInitializeLoadType.SubsystemRegistration` で自動リセットするため、本 spec の生成タイミング（`OutputSceneBootstrapper.Awake/Start`）はこれに整合する。 | 低 |
| RA-12 | Hand-shake / 起動タイミング | **本 spec のハンドラ登録は `OutputSceneBootstrapper.Start` 完了後（IPC サーバ受信開始後）に行い、`slots/catalog` / `avatars/catalog` の初回 publish は `Awake` 完了直後に Pending としてキューに入れ、IPC 受信開始後に flush する**。 | `output-renderer-shell` design.md の Postcondition（`Start` 完了で Dispatcher へのハンドラ登録受付開始）に整合。UI 側が IPC 接続成立直後に再取得（CS の Requirement 7 第 8 項）するため、初回 publish の取りこぼしは UI 側 retry で吸収可能だが、本 spec も Pending → flush で堅牢化する。 | 中（順序ハマりが起きやすい。Composition Root の Step 表で固定） |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase における **RAC メイン出力アダプタ（RAC Main Output Adapter）** を定義する。`character-selection-tab` が UI 側で発行する IPC コマンドをメイン出力プロセスで受信し、RAC の Slot 機構を駆動してアバターを実シーンに反映する責務を負う。Wave 2 で完成済みの `output-renderer-shell` が提供する `IOutputCommandDispatcher` へハンドラを登録するパスのみを使い、`output-renderer-shell` 自体には変更を加えない。具体的には次の責務を持つ：

1. **タブ Contracts の参照購読**：`character-selection-tab` Contracts asmdef の topic 定数・payload DTO を参照し、UI 側との二者契約を 1 ソースに保つ。
2. **受信ハンドラ登録**：`slot/{id}/assignment`（state）/ `slot/{id}/settings/{key}`（state）/ `slot/{id}/command`（event）/ `avatars/{key}/schema`（request）を `IOutputCommandDispatcher` に登録する。
3. **RAC 駆動**：受信した割当・設定・離散操作を `SlotManager.AddSlotAsync` / `RemoveSlotAsync` / `ApplyWithFallback` / `IAvatarSettingsApplier` 等に翻訳して実シーンに反映する。
4. **状態送信**：`slots/catalog` / `avatars/catalog` / `slot/{id}/status`（state）と `slot/{id}/error`（event）を発行し、UI 側に最新状態とエラーを通知する。
5. **エラーハンドリング**：RAC `ISlotErrorChannel` を購読して `SlotError` を `slot/{id}/error` イベントに翻訳・転送する。失敗時は他 Slot を継続させ、メイン出力描画には影響を与えない。
6. **拡張点提供**：`IAvatarKeyResolver` / `IAvatarSchemaProvider` / `IAvatarSettingsAdapter` / `IMoCapSourceConfigFactory` を利用者プロジェクトが差し替えられる構造を維持する。

本 spec は **メイン出力側の責務** に限定される。UI 側のタブ実装、`output-renderer-shell` の Dispatcher 実装、`core-ipc-foundation` のトランスポート、RAC ランタイム本体の機能追加、他タブの出力側アダプタ（stage / camera）、アバターアセット本体・MoCap ハードウェア設定は本 spec の責務外である（RA-1, RA-2 参照）。

## Boundary Context

- **In scope**:
  - 本 spec パッケージ（`com.hidano.vtuber-system-base.rac-main-output-adapter`）のアセンブリ構成（`Runtime` のみ、Engine 参照あり）。
  - `character-selection-tab` Contracts asmdef の参照購読と `IOutputCommandDispatcher.RegisterStateHandler<T>` / `RegisterEventHandler<T>` / `RegisterRequestHandler<TReq,TRes>` への登録。
  - RAC `SlotManager` のメイン出力側ライフサイクル管理（生成・Dispose・状態購読）。
  - RAC `ISlotErrorChannel` 購読と `slot/{id}/error` への翻訳。
  - `slot/{id}/assignment` 受信 → `SlotManager.AddSlotAsync` / `RemoveSlotAsync` 翻訳と `slot/{id}/status` 発行。
  - `slot/{id}/settings/{key}` 受信 → `IAvatarSettingsApplier` 経由のアバター GameObject 設定適用。
  - `slot/{id}/command` 受信 → `Reset` / `Reload` / `PresetApply` 実行（`Reset` は Slot 解除、`Reload` は Remove → Add 再実行、`PresetApply` は本 spec では no-op + 警告ログ：プリセット適用は UI 側 state 経路で個別 assignment / setting に分解される設計）。
  - `avatars/{key}/schema` request 受信 → `IAvatarSchemaProvider.Resolve` 同期呼び出し → `AvatarSettingsSchemaPayload` 応答。
  - `slots/catalog` / `avatars/catalog` の publish（初回 + 状態変化追従）。
  - `IAvatarKeyResolver`（既定: Addressables 経由 `BuiltinAvatarProviderConfig` 構築）/ `IAvatarSchemaProvider`（既定: Addressables `{avatarKey}.schema`）/ `IAvatarSettingsAdapter`（既定: 未知キー警告 + 無視）/ `IMoCapSourceConfigFactory`（既定: Stub Source）の拡張点公開。
  - スタンドアロンと Editor PlayMode 両対応（D-9 継承、ドメインリロード跨ぎなし）。
  - 本 spec 単独検証構造（`IOutputCommandDispatcher` をモック化してシナリオを再生する Editor / PlayMode テスト）。

- **Out of scope**:
  - `character-selection-tab` の UI 側実装・UXML / USS（spec #4 の責務）。
  - `output-renderer-shell` の Dispatcher 実装・シーンルート生成・ディスプレイ振り分け（spec #2 の責務、API 利用のみ）。
  - `core-ipc-foundation` のトランスポート・JSON Codec・メインスレッド配信機構（spec #1 の責務）。
  - RAC ランタイム本体の機能追加・改修（採用パッケージをそのまま利用）。
  - VMC 受信、uOSC 経由の MoCap 取り込み（`com.hidano.realtimeavatarcontroller.mocap-vmc` に分離されている将来拡張）。
  - アバターアセット本体（VRM Prefab、`{avatarKey}.thumbnail`、`{avatarKey}.schema` ScriptableObject）— 利用者プロジェクトの責務。
  - ステージ Prefab / Light / Volume / Camera 関連の出力側アダプタ（他 spec の責務）。
  - 永続化（プリセット保存）— UI 側（CS-8 / CS-9 / CS-10 / CS-12）の責務、本 spec はあくまで通常 state 経路で受信するだけ。
  - 配信メディア出力（Spout / Display 切替）— `output-renderer-shell` および RDS の責務。

- **Adjacent expectations**:
  - `core-ipc-foundation`（spec #1）が `MessageEnvelope` / `MessageKind` 列挙とメインスレッド配信を提供していること（D-3）。
  - `output-renderer-shell`（spec #2）の `OutputSceneBootstrapper` が PlayMode 開始時に `IOutputCommandDispatcher` を起動し、`IOutputSceneRoots.Characters` を露出していること。
  - `character-selection-tab`（spec #4）の Contracts asmdef（GUID `1e7b25ecbf9f4963b5275a52b2623640`）が、本 spec の利用する topic 定数 / payload DTO（`SlotAssignmentPayload` / `SlotSettingValuePayload` / `SlotCommandPayload` / `SlotErrorPayload` / `AvatarSchemaRequestPayload` / `AvatarSettingsSchemaPayload` / `SlotCatalogPayload` / `AvatarCatalogPayload` / `SlotStatusPayload`）を公開していること。
  - 採用パッケージ `com.hidano.realtimeavatarcontroller` v0.2.0 が `SlotManager` / `SlotHandle` / `SlotSettings` / `AvatarProviderDescriptor` / `MoCapSourceDescriptor` / `ISlotErrorChannel` / `RegistryLocator` を公開していること。
  - 利用者プロジェクトが Addressables Groups で `{avatarKey}` でアバター Prefab を解決可能にしていること（必須）、`{avatarKey}.schema` ScriptableObject を任意で提供していること。

---

### Requirement 1: パッケージ境界と Composition Root の常駐

**Objective:** 本 spec の開発者として、メイン出力プロセス（`OutputSceneBootstrapper`）が PlayMode を開始したときに本アダプタが自動的に常駐し、`IOutputCommandDispatcher` への登録と RAC 起動を一括で行うブートストラップを得たい。そうすればメイン出力シーンを単独で起動するだけで「UI からの IPC を受けて RAC を駆動する」状態が成立する。

**Note:** 本要件は `output-renderer-shell` Requirement 3.1（ハンドラ登録受付開始）と D-9（PlayMode 限定常駐）の継承に該当する。本 spec は `output-renderer-shell` を **API として利用するのみ** で改修しない（RA-1, RA-2）。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall UPM パッケージ `com.hidano.vtuber-system-base.rac-main-output-adapter` として配布可能で、`Packages/manifest.json` で `com.hidano.vtuber-system-base.core-ipc-foundation` / `com.hidano.vtuber-system-base.output-renderer-shell` / `com.hidano.vtuber-system-base.character-selection-tab` / `com.hidano.realtimeavatarcontroller` を依存に持つ構造とする。
2. The RAC Main Output Adapter shall Runtime asmdef を `VTuberSystemBase.RacMainOutputAdapter.Runtime` として 1 つ持ち、参照先を `VTuberSystemBase.CoreIpc.Abstractions` / `VTuberSystemBase.OutputRendererShell.Runtime` の Abstractions 部 / `VTuberSystemBase.CharacterSelectionTab.Contracts`（GUID `1e7b25ecbf9f4963b5275a52b2623640`）/ RAC `RealtimeAvatarController.Core` に限定し、UI 側 asmdef・他タブ asmdef・`core-ipc-foundation` 具体実装への直接参照を構造的に禁止する。
3. The RAC Main Output Adapter shall asmdef の `noEngineReferences` を `false` とし、`UnityEngine.Object` / `Transform` / `GameObject` の直接操作を許容する（RA-1）。
4. When `OutputSceneBootstrapper` が `Start` を完了したとき、the RAC Main Output Adapter shall `IOutputCommandDispatcher` に対して `slot/+/assignment`（state）/ `slot/+/settings/+`（state）/ `slot/+/command`（event）/ `avatars/+/schema`（request）の各 topic にハンドラを登録する（RA-12）。
5. When PlayMode が終了したとき, the RAC Main Output Adapter shall 登録した `OutputCommandHandlerRegistration` を全て Dispose し、`SlotManager.Dispose` を呼び出して RAC 由来 GameObject を `Characters` ルート配下から解放する（RA-1, RA-11）。
6. The RAC Main Output Adapter shall Edit モードでは常駐せず、`UnityEditor.EditorApplication.playModeStateChanged` の `EnteredPlayMode` / `ExitingPlayMode` でのみライフサイクル処理を実行する（D-9 継承）。
7. The RAC Main Output Adapter shall `OutputSceneBootstrapper` が複数存在する誤配置に対して、警告ログを残しつつ自身も二重生成しない（`output-renderer-shell` の重複検出契約に従属）。
8. The RAC Main Output Adapter shall 単一の Composition Root クラス（`RacMainOutputAdapterBootstrapper`）を提供し、外部から `OverrideServices(IOutputCommandDispatcher, IOutputSceneRoots, IAvatarKeyResolver, IAvatarSchemaProvider, IAvatarSettingsAdapter, IMoCapSourceConfigFactory, IClock, IDiagnosticsLogger)` でテスト用ダブルを差し込める構造を持つ。

---

### Requirement 2: Slot 割当の受信と RAC 駆動

**Objective:** 配信オペレーターとして、UI で「Slot にアバターを割り当てる」操作を行ったら、メイン出力上に該当アバター GameObject が生成され、Slot 解除を行ったら GameObject が解放される動作を得たい。そうすれば UI と Display 2 の状態が常に同期し、配信中の差替も即座に反映される。

**Note:** 本要件は `slot/{id}/assignment` state を `SlotManager.AddSlotAsync` / `RemoveSlotAsync` に翻訳する責務を定義する。RAC は in-place 差替 API を持たないため、Remove → Add の連続操作で実装する（RA-5）。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall `IOutputCommandDispatcher.RegisterStateHandler<SlotAssignmentPayload>(CharacterTopics.SlotAssignment(slotId))` で `slot/{id}/assignment` を購読する（`SlotId` は dynamic、catalog 受信時に動的登録解除も可）。
2. When `SlotAssignmentPayload.AvatarKey` が **null** で受信されたとき、the RAC Main Output Adapter shall 該当 Slot が `Active` であれば `SlotManager.RemoveSlotAsync(slotId)` を呼び、Slot 状態を `Empty` として `slot/{id}/status` を publish する。
3. When `SlotAssignmentPayload.AvatarKey` が **空でない値** で受信されたとき、the RAC Main Output Adapter shall まず `slot/{id}/status = Assigning` を publish し、次に `IAvatarKeyResolver.Resolve(avatarKey)` で `AvatarProviderDescriptor` を構築、`SlotSettings` を組み立てて `SlotManager.AddSlotAsync(settings)` を呼び、成功時に `slot/{id}/status = Assigned` を publish する。
4. When 既に同一 `slotId` で別アバターが `Active` の状態で `slot/{id}/assignment` を新しい `AvatarKey` で受信したとき、the RAC Main Output Adapter shall **`RemoveSlotAsync(slotId)` を完了 → `AddSlotAsync(newSettings)` を実行** の直列順序で差替を行い、その間 `slot/{id}/status = Assigning` を publish する（RA-5）。
5. If `IAvatarKeyResolver.Resolve` が key を解決できなかった場合, the RAC Main Output Adapter shall `slot/{id}/status = Error`（`Detail = "AvatarKeyNotFound"`）を publish し、`slot/{id}/error`（`ErrorCode = KeyNotFound`、`Detail = "{avatarKey}"`）を publish する。`SlotManager` には登録しない。
6. If `SlotManager.AddSlotAsync` が `SlotErrorCategory.InitFailure` の `ISlotErrorChannel` 通知を発行した場合, the RAC Main Output Adapter shall `slot/{id}/status = Error` と `slot/{id}/error`（`ErrorCode = MotionPipelineInit` または `KeyNotFound`、文脈で振り分け、RA-8）を publish する。
7. The RAC Main Output Adapter shall `slot/{id}/assignment` の処理中に同一 `slotId` への新規 `assignment` を受信した場合、coalesce による上流契約（D-7）に従い **最新 `AvatarKey` のみ** が反映されるよう、内部キューを 1 件のみ保持する設計とする。中間値は破棄（last-write-wins, OR-2）。
8. The RAC Main Output Adapter shall `slot/{id}/assignment` ハンドラ実行中の例外を `try/catch` で捕捉し、`slot/{id}/error`（`ErrorCode = Unknown`、`Detail = exception.GetType().Name + ":" + exception.Message`）を publish したうえで、ディスパッチャは継続する（`output-renderer-shell` Requirement 3.6 に整合）。
9. The RAC Main Output Adapter shall 受信した `SlotAssignmentPayload` の `AvatarKey` を ASCII 検証（`CharacterTopics.Safe` ロジックを通過する文字種）し、不正値はエラー扱い + `slot/{id}/error`（`ErrorCode = KeyNotFound`）で破棄する。

---

### Requirement 3: Slot 個別設定の受信とアバターへの適用

**Objective:** 配信オペレーターとして、UI のスライダー・カラーピッカー操作で連続値を変えたら、メイン出力上のアバター GameObject にリアルタイムで反映される体験を得たい。そうすれば配信前の調整が UI 上で完結する。

**Note:** 本要件は `slot/{id}/settings/{key}` state を `IAvatarSettingsAdapter.Apply` に翻訳する責務を定義する。設定キーごとの具体適用ロジックは利用者プロジェクトの拡張点（RA-6）。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall `IOutputCommandDispatcher.RegisterStateHandler<SlotSettingValuePayload>(CharacterTopics.SlotSettingValue(slotId, settingKey))` を slot × key の組ごとに動的登録する。catalog 受信に応じて新規 Slot の登録を増やし、削除時は登録解除する。
2. When `SlotSettingValuePayload` が受信されたとき、the RAC Main Output Adapter shall `SlotManager.TryGetSlotResources(slotId, out source, out avatar)` でアバター GameObject を解決し、`IAvatarSettingsAdapter.Apply(avatar, settingKey, type, value)` を呼び出す。
3. If 該当 `slotId` が `Active` でない（`Empty` / `Assigning` / `Disposed` / 未登録）場合, the RAC Main Output Adapter shall 当該 setting payload を内部に **保留バッファ** として保持し、Slot が `Active` に遷移したタイミングで保留分を順次適用する（最新値のみ、key 単位で last-write-wins）。
4. If `IAvatarSettingsAdapter.Apply` が未知の `settingKey` を返した場合（`AdapterApplyResult.UnknownKey`）, the RAC Main Output Adapter shall 警告ログ（`LogCategory.Adapter`、`slotId` / `avatarKey` / `settingKey` / `type` を含む）を出力し、`slot/{id}/error` は publish しない（縮退で UI を阻害しない、CS-11 整合）。
5. If `SlotSettingValuePayload.Type` が `SettingType` の未知列挙値であった場合, the RAC Main Output Adapter shall `Unknown` 扱いで警告ログを残し、適用をスキップする（前方互換、`character-selection-tab` Contracts の `SettingType` 注記に整合）。
6. The RAC Main Output Adapter shall `slot/{id}/settings/{key}` ハンドラ実行中の例外を `try/catch` で捕捉し、`SlotManager.ApplyWithFallback` の規律に整合する形で `SlotErrorCategory.ApplyFailure` を `ISlotErrorChannel` 経由で再発火させる（または直接 `slot/{id}/error` で `ApplyFailed` を publish）。ディスパッチャは継続する。
8. The RAC Main Output Adapter shall アバター差替（Requirement 2 第 4 項）が行われたとき、旧アバターに紐付いていた保留 setting バッファを破棄し、新アバターでは新規にバッファを開始する（バッファのキーは `slotId` × `settingKey` × `avatarKey` の三つ組）。
9. The RAC Main Output Adapter shall 連続受信に対して上流 coalesce（D-7）に依存し、本 spec 内で独自スロットリングを実装しない（`character-selection-tab` Requirement 5 第 4 項と対称）。

---

### Requirement 4: Slot 離散操作（command event）の処理

**Objective:** 配信オペレーターとして、UI の `Reset` / `Reload` ボタン操作で Slot がリセット・再読込される動作を得たい。そうすれば配信中に「リップシンクが詰まった」「BlendShape が壊れた」等の事故から素早く復帰できる。

**Note:** 本要件は `slot/{id}/command` event の処理を定義する。`PresetApply` は UI 側で個別 assignment / setting state に展開される設計（CS-12）のため、本 spec では受信したらログ出力のみで no-op とする。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall `IOutputCommandDispatcher.RegisterEventHandler<SlotCommandPayload>(CharacterTopics.SlotCommand(slotId))` で `slot/{id}/command` を購読する。
2. When `SlotCommandPayload.Kind == "Reset"` を受信したとき、the RAC Main Output Adapter shall `SlotManager.RemoveSlotAsync(slotId)` を呼び、Slot 状態を `Empty` として `slot/{id}/status` を publish する（Requirement 2 第 2 項と同一動作）。
3. When `SlotCommandPayload.Kind == "Reload"` を受信したとき、the RAC Main Output Adapter shall 現在割当中の `AvatarKey` を保持したまま `RemoveSlotAsync` → `AddSlotAsync` の連続操作を実行し、その間 `slot/{id}/status = Assigning` → `Assigned` を publish する。Slot が `Empty` 状態のときは no-op + 警告ログとする。
4. When `SlotCommandPayload.Kind == "PresetApply"` を受信したとき、the RAC Main Output Adapter shall 情報レベルのログを残して no-op とする（プリセット適用は UI 側が個別 assignment / setting state に分解して送信する設計のため、本 spec では受信不要）。`Argument` フィールドは無視する。
5. If `SlotCommandPayload.Kind` が **未知の値** であった場合, the RAC Main Output Adapter shall 警告ログ（`slotId` / `kind` / `argument`）を残してスキップする（前方互換）。
6. The RAC Main Output Adapter shall command event を FIFO 順で処理することを上流契約（D-7 / D-10）に依存して保証する（独自キューを実装しない）。
7. The RAC Main Output Adapter shall command ハンドラ実行中の例外を `try/catch` で捕捉し、`slot/{id}/error`（`ErrorCode = Unknown`、`Detail` に `kind`）を publish する。

---

### Requirement 5: アバター設定スキーマ request への応答

**Objective:** 配信オペレーターとして、UI で Slot の設定パネルを開くと「そのアバターが提供する設定項目」が自動的に列挙される体験を得たい。そうすればアバターごとの専用 UI を手書きせずに、利用者プロジェクトが提供するスキーマだけで設定 UI が成立する。

**Note:** 本要件は `avatars/{key}/schema` request への応答を定義する。スキーマの権威は利用者プロジェクトに移譲し、本 spec は `IAvatarSchemaProvider` 拡張点を提供する（RA-7）。応答ハンドラは Unity メインスレッド上で同期実行される（RA-10、`IOutputCommandDispatcher.RegisterRequestHandler` の制約）。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall `IOutputCommandDispatcher.RegisterRequestHandler<AvatarSchemaRequestPayload, AvatarSettingsSchemaPayload>(CharacterTopics.AvatarSchema(avatarKey))` を avatar 単位に動的登録する（catalog 受信時に追加・削除する）。
2. When `AvatarSchemaRequestPayload` が受信されたとき、the RAC Main Output Adapter shall `IAvatarSchemaProvider.Resolve(payload.AvatarKey)` を同期呼び出しし、結果を `AvatarSettingsSchemaPayload`（`AvatarKey` + `Settings`）として返却する。
3. If `IAvatarSchemaProvider.Resolve` が **null または未解決** を返した場合, the RAC Main Output Adapter shall `AvatarSettingsSchemaPayload { AvatarKey = payload.AvatarKey, Settings = Array.Empty<SettingSchemaEntry>() }` を返却する（空スキーマフォールバック、RA-7）。
4. The RAC Main Output Adapter shall `IAvatarSchemaProvider.Resolve` の同期実行時間を計測し、5 秒の上流タイムアウト（D-8 等価）に対して 4 秒を超えた場合に診断ログ（`SchemaProvider.Slow`）を残す（重い Provider 実装の警告）。
5. If `IAvatarSchemaProvider.Resolve` が **例外を送出した** 場合, the RAC Main Output Adapter shall `try/catch` で捕捉し、空スキーマを返却したうえで診断ログ（`LogCategory.Adapter` / `SchemaProvider.Failed` / `avatarKey` / `exception`）を残す。UI 側 request はタイムアウトせず正常に空応答を受け取る。
6. The RAC Main Output Adapter shall 既定の `IAvatarSchemaProvider` として「Addressables から `{avatarKey}.schema`（ScriptableObject 派生型）を **同期 LoadAssetAsync().WaitForCompletion()** で取得し、ScriptableObject の公開フィールドから `SettingSchemaEntry` 配列を構築する」実装を提供する（同期化は UI 側 request の同期戻り型制約 RA-10 に整合）。利用者プロジェクトはこれを差し替え可能にする。
7. The RAC Main Output Adapter shall 既定 Provider が `{avatarKey}.schema` を解決できない場合は空スキーマフォールバックを採用し、診断ログに `SchemaProvider.Fallback(avatarKey)` を残す。

---

### Requirement 6: catalog（slots / avatars）の発行

**Objective:** UI 側として、メイン出力に「現在この Slot 群と Avatar 群が用意されている」状態を最新かつ自動で受信したい。そうすれば UI は本 spec の挙動を信頼してプレイヤーカード一覧とアバターカタログを描画できる。

**Note:** 本要件は `slots/catalog` / `avatars/catalog` の publish を定義する。`SlotManager.OnSlotStateChanged` ストリームと `IAvatarKeyResolver.AvatarKeys` を 1 本化して IPC へ橋渡しする（RA-9）。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall PlayMode 開始時（`Start` 完了 + IPC 受信開始後）に `slots/catalog`（`SlotCatalogPayload`）を初回 publish する。Slot が 0 件の場合も空配列で publish する。
2. The RAC Main Output Adapter shall PlayMode 開始時に `IAvatarKeyResolver.AvatarKeys` を取得して `avatars/catalog`（`AvatarCatalogPayload`）を初回 publish する。0 件の場合も空配列で publish する。
3. When `SlotManager.OnSlotStateChanged` で `Created` / `Active` / `Disposed` 状態への遷移が観測されたとき、the RAC Main Output Adapter shall `slots/catalog` を最新の `SlotCatalogEntry` 配列で再 publish する（coalesce 対象、上流 D-7 に依存）。
4. When `IAvatarKeyResolver.Refresh()` が完了したとき（または利用者プロジェクトが Addressables カタログを更新したとき）、the RAC Main Output Adapter shall `avatars/catalog` を再 publish する。
5. The RAC Main Output Adapter shall `SlotCatalogEntry.SlotId` / `DisplayName` / `OrderHint` を RAC `SlotHandle` から構築し、`OrderHint` は `SlotManager.GetSlots()` の返却順を 0 始まりインデックスとして埋める。
6. The RAC Main Output Adapter shall `AvatarCatalogEntry.AvatarKey` / `DisplayName` を `IAvatarKeyResolver.AvatarKeys` の各エントリから構築し、`DisplayName` が解決できないエントリには `AvatarKey` をフォールバックで使う。
7. The RAC Main Output Adapter shall `slots/catalog` および `avatars/catalog` の publish に失敗した場合、診断ログを残しつつ次回更新で再試行する（UI クラッシュ・描画停止を発生させない）。

---

### Requirement 7: エラー伝播と RAC `ISlotErrorChannel` 連携

**Objective:** 配信運用者として、メイン出力側で発生した RAC エラー（アバター読込失敗、モーション初期化失敗、Apply 失敗）が UI 側にプレイヤーカードのエラー状態として可視化される動作を得たい。そうすれば配信中の障害が即座にオペレーターへ伝わる。

**Note:** 本要件は CS-11 の出力側具現化に該当する。RAC `ISlotErrorChannel` のストリームを購読し、`slot/{id}/error` event に翻訳する（RA-8）。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall `RegistryLocator.ErrorChannel.Errors` を購読し、`SlotError` を受信したら **メインスレッド**（`.ObserveOnMainThread()` を介する）で `slot/{slotId}/error` event に翻訳して publish する。
2. The RAC Main Output Adapter shall `SlotErrorCategory` を `SlotErrorPayload.ErrorCode` に下記マッピングで翻訳する：
   - `InitFailure` → `KeyNotFound`（Provider 解決失敗を示す例外型を含む場合）または `MotionPipelineInit`（MoCap Source 初期化失敗を示す例外型）/ それ以外 `Unknown`
   - `ApplyFailure` → `ApplyFailed`
   - `RegistryConflict` → `Unknown`（`Detail` に "RegistryConflict" を含める）
   - `VmcReceive` → `Unknown`（本 spec のスコープ外、ログのみ）
3. The RAC Main Output Adapter shall `SlotErrorPayload.Detail` に `category` / `exception.GetType().Name` / `message`（最大 512 文字までトリム）を JSON 風に詰めて転送する。
4. If エラーが発生した Slot が `slots/catalog` 上で **既に Disposed** であった場合, the RAC Main Output Adapter shall publish 自体は実施するが、上流 coalesce 上 UI 側に届く保証はないことをログに警告する。
5. The RAC Main Output Adapter shall いかなるエラー経路でも、メイン出力サーフェス（Display 2+）に `OnGUI` / `IMGUI` / `UIDocument` でエラー UI を描画しない（`output-renderer-shell` Requirement 5.6 と整合、構造的禁止）。
6. The RAC Main Output Adapter shall 自身の例外捕捉ハンドラ内で発生した二次例外は最終 `catch` で握り潰し、Unity Console への警告ログのみ残す（Dispatcher 継続を最優先）。
7. The RAC Main Output Adapter shall `slot/{id}/error` 送信元では `slot/{id}/status = Error` を併せて publish し、UI 側がステータス購読のみでも画面表示が縮退するようにする。

---

### Requirement 8: 拡張点とテストダブル受入れ

**Objective:** 利用者プロジェクト開発者として、Addressables 規約・MoCap ソース選定・アバター設定適用ロジック・スキーマ Provider を自プロジェクトに合わせて差し替えたい。そうすれば本 spec をそのままコピーするだけで自社アバター実装に適合できる。

**Note:** 本要件は RA-3 / RA-4 / RA-6 / RA-7 の拡張点を要件レベルで固定し、本 spec 単独検証（spec オーナー視点）の構造も同時に担保する。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall `IAvatarKeyResolver` インタフェース（`Resolve(string avatarKey) → AvatarProviderDescriptor?`、`AvatarKeys → IReadOnlyList<AvatarCatalogEntry>`、`Refresh()`、`OnAvatarKeysChanged`）を公開し、既定実装として「Addressables の `{avatarKey}` Prefab + `BuiltinAvatarProviderConfig` 動的生成」を提供する（RA-3）。
2. The RAC Main Output Adapter shall `IAvatarSchemaProvider` インタフェース（`Resolve(string avatarKey) → AvatarSettingsSchemaPayload?`）を公開し、既定実装として「Addressables `{avatarKey}.schema` ScriptableObject 同期取得」を提供する（RA-7）。
3. The RAC Main Output Adapter shall `IAvatarSettingsAdapter` インタフェース（`Apply(GameObject avatar, string settingKey, SettingType type, JsonElement value) → AdapterApplyResult`、列挙: `Applied` / `UnknownKey` / `OutOfRange` / `Failed`）を公開し、既定実装として「全キー `UnknownKey`（警告 + 無視）」を返すフォールバック実装を提供する（RA-6）。
4. The RAC Main Output Adapter shall `IMoCapSourceConfigFactory` インタフェース（`Build(string slotId) → MoCapSourceDescriptor`）を公開し、既定実装として「Stub MoCap Source（RAC 同梱の `BuiltinAvatarProviderConfig` と組になる no-op Source）」を返す（RA-4）。
5. The RAC Main Output Adapter shall `RacMainOutputAdapterBootstrapper.OverrideServices` 経由で全拡張点を差し替え可能にし、テスト時にメモリダブル（`InMemoryAvatarKeyResolver` / `InMemoryAvatarSchemaProvider` / `RecordingAvatarSettingsAdapter` / `StubMoCapSourceConfigFactory`）を注入できる構造とする。
6. The RAC Main Output Adapter shall `IOutputCommandDispatcher` 自体もテスト時に差し替え可能とし、`InMemoryDispatcher`（topic → handler 辞書 + `Emit*` API）でハンドラ呼び出しを再生できるようにする（`output-renderer-shell` 自体への変更は加えない）。
7. The RAC Main Output Adapter shall `IClock` を受け取り、PlayMode 計測・タイムアウト判定・診断ログのタイムスタンプに利用する（テスト時 `ManualClock` 注入可）。
8. The RAC Main Output Adapter shall 上記拡張点はすべて Composition Root の依存注入経路でのみ受け入れ、`static`／グローバルサービスロケータ参照を **直接** 持たない（RAC `RegistryLocator` のみ例外で、本 spec が wraps する形で利用する）。

---

### Requirement 9: スタンドアロン / Editor PlayMode 両対応

**Objective:** 配信運用者として、ビルドしたスタンドアロンと Unity Editor PlayMode で本アダプタの挙動が同一であることを得たい。そうすれば Editor で配信前リハーサルが完結する。

**Note:** D-9 を継承し、Edit モードで常駐しない・PlayMode 反復に耐える・ドメインリロード跨ぎ状態を持たない方針を要件化する。

#### Acceptance Criteria

1. When Unity アプリケーションがスタンドアロンビルドとして起動し、`OutputSceneBootstrapper` の `Start` が完了したとき、the RAC Main Output Adapter shall ハンドラ登録・RAC 起動・catalog 初回 publish を自動実施する。
2. When Unity Editor が PlayMode に入ったとき、the RAC Main Output Adapter shall スタンドアロン時と同一手順で起動する（D-9）。
3. When Unity Editor が PlayMode を終了したとき、the RAC Main Output Adapter shall 登録した `OutputCommandHandlerRegistration` を全て Dispose し、`SlotManager.Dispose()` を呼んで RAC GameObject を解放し、診断ログのバッファをフラッシュする。
4. While PlayMode の開始と停止が繰り返される間, the RAC Main Output Adapter shall ハンドラの二重登録、Slot GameObject の重複生成、`ISlotErrorChannel` 購読のリークを発生させず、毎回クリーンに再初期化する。
5. The RAC Main Output Adapter shall Edit モードでは実行時ロジック（ハンドラ登録、`SlotManager` 起動、購読開始）を一切起動しない。
6. The RAC Main Output Adapter shall ドメインリロードに跨る状態維持を試みず、PlayMode 開始のたびに新規 `SlotManager` を生成する（RAC `RegistryLocator.ResetForTest()` の自動リセットに整合、RA-11）。
7. The RAC Main Output Adapter shall スタンドアロン時と Editor PlayMode 時で、IPC 受信から RAC 反映までのレイテンシ特性とエラー復帰挙動を同一に保つ。

---

### Requirement 10: 観測性・診断可能性

**Objective:** 開発者・配信運用者として、本 spec で発生する不具合が「IPC 受信起因」「RAC 呼出起因」「Addressables 起因」「Settings Adapter 起因」「Schema Provider 起因」のいずれかを即座に切り分けたい。そうすれば本番配信中でも迅速に対応できる。

**Note:** 本要件の診断出力は `output-renderer-shell` Requirement 5（メイン出力サーフェスへ描画しない）に従い、Unity Console または `IDiagnosticsLogger` 経由で UI 側へのみ流す。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall ハンドラ登録・RAC 起動・catalog 初回 publish の各段階の開始・完了・失敗をログ出力する。
2. The RAC Main Output Adapter shall `slot/{id}/assignment` 受信時に対象 `slotId` / `avatarKey` / 結果（Assigned / Empty / Error）/ 経過時刻をログ出力する。
3. The RAC Main Output Adapter shall `slot/{id}/settings/{key}` 受信時に `slotId` / `settingKey` / `type` / `applyResult` をデバッグレベルでログ出力する（連続値での冗長を避けるためデバッグ専用）。
4. The RAC Main Output Adapter shall `slot/{id}/command` 受信時に `slotId` / `kind` / `argument` を情報レベルでログ出力する。
5. The RAC Main Output Adapter shall `avatars/{key}/schema` request 受信時に `avatarKey` / 応答件数 / 同期処理時間をログ出力する（`SchemaProvider.Slow` / `SchemaProvider.Fallback` / `SchemaProvider.Failed` 含む）。
6. The RAC Main Output Adapter shall `ISlotErrorChannel` 由来の `SlotError` を受信して publish するとき、`category` / `slotId` / `exception` を必ずログ出力する。
7. The RAC Main Output Adapter shall 診断スナップショット API（`IRacMainOutputAdapterDiagnostics.Capture()` → `RacAdapterDiagnosticsSnapshot { RegisteredHandlerCount, ActiveSlotCount, ErrorSlotCount, LastErrorAtUnixMs, LastErrorMessage, AvatarCatalogSize }`）を提供し、`output-renderer-shell` の `IOutputDiagnostics` を補完する形で外部から読み取り可能にする。
8. The RAC Main Output Adapter shall ログレベルを外部から切替可能にする（`IDiagnosticsLogger` の設定で連動）。
9. The RAC Main Output Adapter shall いかなる経路でもメイン出力サーフェス（Display 2+）に `OnGUI` / `IMGUI` / `UIDocument` を生成しないことを構造的に保証する（asmdef レベルで UI Toolkit 描画 API を使わない方針）。

---

### Requirement 11: 本 spec 単独での検証可能性

**Objective:** spec オーナーとして、UI 側 `character-selection-tab` の実装やリアルアバターアセットがそろう前に本アダプタを検証したい。そうすれば Wave 3c の 3 アダプタを並行開発する際に、モックを介して本 spec の RAC 駆動と IPC 契約を独立に検証できる。

**Note:** 本要件は `output-renderer-shell` Requirement 8（自己ループによるディスパッチャ検証）と RAC `RegistryLocator.OverrideProviderRegistry` を活用する。

#### Acceptance Criteria

1. The RAC Main Output Adapter shall 実アバターアセットおよび UI 側 `character-selection-tab` 実装が無くても、`InMemoryDispatcher` から topic ごとに payload を `Emit*` するだけで本アダプタの全経路（assignment / settings / command / schema）が実行できる構造とする。
2. The RAC Main Output Adapter shall RAC `SlotManager` をモックアウトせず **実体を使い**、`RegistryLocator.OverrideProviderRegistry` で **テスト用 Stub Provider**（`InMemoryProviderRegistry` + 任意の Stub `IAvatarProvider`）を注入することで Slot ライフサイクルを検証できる構造とする。
3. The RAC Main Output Adapter shall `IAvatarKeyResolver` / `IAvatarSchemaProvider` / `IAvatarSettingsAdapter` / `IMoCapSourceConfigFactory` のメモリダブル実装を本 spec の `Tests.Runtime` asmdef に同梱する。
4. The RAC Main Output Adapter shall Unity Editor PlayMode での手動検証手順（最小サンプルシーン `RacAdapterPlayModeSample.unity`、モック UI として `InMemoryDispatcher` を駆動する Composition Root を含む）を提供する。
5. When 本 spec 単体のテスト実行が行われたとき、the RAC Main Output Adapter shall 次の挙動を検証するテストケースを提供する：assignment による Slot 生成、null assignment による Slot 解除、別 avatarKey への差替（Remove → Add 直列）、未解決 avatarKey でのエラー発火、settings の受信と Adapter 呼出、保留バッファの flush、command Reset / Reload、schema request 応答、空スキーマフォールバック、`ISlotErrorChannel` からの error 翻訳、PlayMode 反復後のリーク不在。
6. The RAC Main Output Adapter shall テスト時に時刻（catalog 再 publish タイマ等）を制御可能にするための `IClock` 抽象を受け入れる構造を備える。
7. The RAC Main Output Adapter shall `Tests.Runtime` asmdef を独立に持ち、`VTuberSystemBase.RacMainOutputAdapter.Runtime` の `internal` シンボルへ `InternalsVisibleTo` で限定公開する。

---

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案、要件レベル厳選、上流 spec 決定の積極的継承）
- **本 spec 固有の決定**: 12 件（RA-1〜RA-12）
- **継承**:
  - `core-ipc-foundation` の D-1（単一 Unity アプリ + LocalHost）、D-3（メインスレッド配信）、D-5（JSON / WebSocket）、D-7（state coalesce / event FIFO）、D-9（PlayMode のみ常駐）、D-10（PublishState / PublishEvent）、D-11（1 MB 上限）
  - `output-renderer-shell` の OR-1（メイン出力描画にエラー UI を出さない）、OR-2（state 競合 last-write-wins）、Requirement 3.1〜3.8 / 4.1〜4.9 / 5.6（描画禁止契約）、Requirement 8.2（自己ループ検証）
  - `character-selection-tab` の CS-1（RAC 本体はメイン出力側）、CS-2（Slot ライフサイクル所有は RAC）、CS-3（empty 状態許可）、CS-4（Addressables key 識別）、CS-5（スキーマ権威は RAC 側）、CS-6（連続値 state / 離散 event）、CS-7（topic 粒度）、CS-11（不可用アバターは empty + 警告、他 Slot 継続）
- **主要な発見（本 spec 固有）**:
  - 既存の `character-selection-tab` Contracts asmdef（GUID `1e7b25ecbf9f4963b5275a52b2623640`）を **共有参照** することで 1 ソース契約を維持。新規 Contracts asmdef を作らない（RA-2）。
  - RAC v0.2.0 は in-place 差替 API を持たないため、アバター切替は `RemoveSlotAsync` → `AddSlotAsync` の連続操作で実装し、その間 `slot/{id}/status = Assigning` を publish する（RA-5）。
  - スキーマ Provider は `IOutputCommandDispatcher.RegisterRequestHandler` の同期戻り型制約に従い同期 API を要求する（RA-10）。利用者プロジェクトの Provider 実装が遅い場合は警告ログで露見させる。
  - 設定キーごとの適用ロジックは利用者プロジェクトの拡張点（`IAvatarSettingsAdapter`）に移譲し、本 spec は未知キーを警告 + 無視で縮退する（RA-6）。
  - エラー伝播は RAC `ISlotErrorChannel` を購読してメインスレッドへ移譲後 `slot/{id}/error` event に翻訳する。`SlotErrorCategory` → `ErrorCode` のマップを RA-8 で固定。
  - catalog 再 publish は `SlotManager.OnSlotStateChanged` ストリームを観測することで UI 側からのポーリングを排除（RA-9）。
  - MoCap 受信は本フェーズのスコープ外（VMC は別パッケージ）であり、既定で Stub Source を採用する（RA-4）。

- **残留リスク（設計フェーズで継続検討）**:
  - R-RA-1: 同期 LoadAssetAsync の `WaitForCompletion()` がメインスレッドをブロックする時間（特にアバター数が多い `avatars/{key}/schema` 応答時）。設計フェーズで Provider 側の事前ロード戦略を確定し、ハンドラ実行時間の上限を XMLDoc で明示する。
  - R-RA-2: `IAvatarKeyResolver` の Addressables カタログ更新タイミングと `avatars/catalog` 再 publish の整合性。Addressables の `LoadResourceLocationsAsync` 完了を契機にする実装が現実解か、ScriptableObject ベースのカタログ宣言を採用するかを設計フェーズで確定。
  - R-RA-3: `Reload` コマンドが `RemoveSlotAsync` → `AddSlotAsync` の連続操作で行われる際の "瞬間消失" を UI 側でどう吸収するか。`Assigning` 状態を細分化（`Removing` / `Reloading`）するか、Detail 文字列で識別するかを設計フェーズで確定。
  - R-RA-4: 同一 `slotId` への並行 assignment / command を直列化するキューイング戦略。`SlotManager` 自体は同期処理だが、UniTask の `await` を含む経路では再入の可能性がある。設計フェーズで `SemaphoreSlim` or `OperationQueue` の採否を確定。
  - R-RA-5: `IAvatarSettingsAdapter` の既定実装が「全キー UnknownKey」では実用に不十分。本 spec パッケージに最小サンプル（VRM Humanoid 向け Adapter のリファレンス実装）を Samples~ に同梱するかを設計フェーズで決定。
  - R-RA-6: `RegistryLocator` のグローバル状態が複数の出力アダプタ spec（RAC / Stage / Camera）で衝突する可能性。本 spec は `OverrideProviderRegistry` をテスト時のみ使い、本番では Locator のグローバル既定値を尊重する方針だが、他アダプタ spec の方針と整合させる必要あり。
  - R-RA-7: `slot/{id}/error` の `Detail` フィールド長（512 文字トリム）が UI 側 JSON シリアライズ時に問題を起こさないかの確認。`core-ipc-foundation` の 1 MB 上限に対して十分余裕があるが、設計フェーズで実例を確認。
  - R-RA-8: PlayMode 反復時の `ISlotErrorChannel` 購読リーク検証。`UniRx Subject` の `OnCompleted` 呼出を `SlotManager.Dispose` が行う実装に依存しており、本 spec の `Dispose` で重複 `OnCompleted` を呼ばないよう注意が必要。
  - R-RA-9: VMC パッケージ（`com.hidano.realtimeavatarcontroller.mocap-vmc`）導入時の互換性。`IMoCapSourceConfigFactory` 拡張点で吸収する設計だが、設計フェーズで VMC 側の API を再確認する。
  - R-RA-10: スタンドアロンビルド時の `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 経由 RAC 自動登録と本 spec の Composition Root の起動順序。Unity が保証する順序通りに動作することをテストで確認する。
