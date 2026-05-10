# Requirements Document

## Project Description (Input)

stage-lighting-volume-output-adapter

`stage-lighting-volume-tab` が送信する IPC コマンド（state / event / request）を受信し、メイン出力側（Display 2+ の `output-renderer-shell` のシーン）の `LightsRoot` / `StageRoot` / `VolumeRoot` / `CamerasRoot` に対して **実シーンの GameObject 生成・編集・破棄** および **URP Global Volume への Override 適用** を行うメイン出力側アダプタ spec。

スコープ:

- `stage-lighting-volume-tab` の `Contracts` asmdef（`StageLightingTopics` / `*Dto` / `IPreviewHostService` / `StagePreviewHostLocator`）を参照し、`output-renderer-shell` の `IOutputCommandDispatcher.RegisterStateHandler<T>` / `RegisterEventHandler<T>` / `RegisterRequestHandler<TReq,TRes>` に各 topic ハンドラを登録する。
- `stage/active`（state, UI→出力）受信 → Addressables 経由でステージ Prefab を `Addressables.InstantiateAsync` し `StageRoot` 配下に配置。旧ステージは `Addressables.ReleaseInstance` で必ず解放。
- `stage/catalog`（state, 出力→UI）送信 → 利用者プロジェクトが Addressables Group に登録した Stage アセット一覧を `StageCatalogDto` として publish する。
- `light/command`（event, UI→出力）add / remove → `LightsRoot` 配下に `Light` コンポーネント付き GameObject を生成・破棄。lightId はメイン出力側で採番（`Guid.NewGuid().ToString("N")`）し `light/added` event で UI 側へ返却する。
- `light/{lightId}/{property}`（state, UI→出力）受信 → 該当 Light GameObject の `Light` / `Transform` プロパティを反映（type / rotation / color / intensity / range / spotAngle / displayName）。
- `lights/list`（state, 出力→UI）送信 → 現在の Light 一覧を `LightListDto` として publish する。
- `volume/override/{type}/enabled`（state, UI→出力）受信 → Global VolumeProfile に当該 `VolumeComponent` 派生型を `profile.Add<T>()` で動的追加または `enabled` 切替。
- `volume/override/{type}/{param}`（state, UI→出力）受信 → 該当 `VolumeComponent` の `VolumeParameter<T>` をリフレクションベースのパラメータセッタで反映。`overrideState` を true に設定する。
- `volume/overrides/metadata`（request, UI→出力）受信 → `VolumeManager.instance.baseComponentTypeArray` を列挙し、各型のフィールドメタデータを `VolumeOverrideSchemaDto` として返す。
- `preview/command`（event, UI→出力）受信 → プレビューカメラ（`StagePreviewHost` MonoBehaviour）の有効化・無効化・視点リセットを行い、`preview/state`（state, 出力→UI）で結果を publish する。
- 同一プロセス内の RenderTexture 共有 → `IPreviewHostService` を `StagePreviewHostLocator.Register(this)` でグローバル登録し、UI 側の `PreviewRenderTextureAccessor` から参照解決される責務を担う。

非目標:

- UI 側 `stage-lighting-volume-tab` の UI 実装（別 spec の責務）。
- IPC トランスポートそのもの（`core-ipc-foundation` の責務）。
- メイン出力カメラ（配信に載るカメラ）の操作（`camera-switcher-output-adapter` の責務）。
- キャラクター（アバター）の生成・操作（`rac-main-output-adapter` の責務）。
- 永続化ファイル I/O（UI 側 `JsonPresetStorage` の責務）。

対応要件: `docs/requirements.md` の §3.2, §5.2、`docs/integration-plan.md` の Wave 3c および §3.1「Stage / Lighting / Volume」結節点表。
上位計画: `docs/integration-plan.md` Wave 3c。`stage-lighting-volume-tab` および `output-renderer-shell` に依存。

上流決定の継承:

- D-1: 単一 Unity アプリ + LocalHost ループバック
- D-3: 受信コールバックは Unity メインスレッド（`output-renderer-shell` の `IOutputCommandDispatcher` が継承担保）
- D-4: UI 側クライアント / メイン出力側サーバ（本 spec はサーバ側＝出力側）
- D-7: state は coalesce、event は FIFO、request は correlationId 1:1
- D-10: `PublishState` / `PublishEvent` / `Request` の使い分け
- OR-2: state 競合は last-write-wins
- SL-2: Light GameObject の所有者はメイン出力側（本 spec が責務を負う）
- SL-3: lightId はメイン出力側採番、`light/added` event で UI 側へ返却
- SL-4: ステージ識別は Addressables 安定 key
- SL-5: Light add/remove は `PublishEvent`、Light プロパティは `PublishState`
- SL-6: topic 粒度（`light/{lightId}/{property}` / `volume/override/{type}/{param}`）
- SL-7: Volume Override メタデータは Request/Response、`VolumeManager.baseComponentTypeArray` を列挙

採用パッケージ: `com.unity.addressables` 2.x、URP（`UnityEngine.Rendering.Universal`）、`com.unity.render-pipelines.core`。
環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応。
言語: 日本語で生成（CLAUDE.md の規約に従う）。

## Open Questions and Decisions (Dig)

本セクションは本 spec 固有の設計上の決定事項を記録する。上流 spec（`core-ipc-foundation`, `output-renderer-shell`, `ui-toolkit-shell`, `stage-lighting-volume-tab`）の決定（D-1〜D-11, OR-1, OR-2, UI-1〜UI-7, SL-1〜SL-14）はすべて暗黙に継承される。本 spec 固有の決定は以下のとおり。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| SLOA-1 | Contracts asmdef の所属 | **`stage-lighting-volume-tab` パッケージ内の Contracts asmdef を参照する** （新規 Contracts asmdef は作らない）。本 spec の Runtime asmdef は `VTuberSystemBase.StageLightingVolumeTab.Contracts` を `references` に追加する。 | UI 側と出力側で「単一の契約ソース」を共有することが SL の design.md（Boundary Commitments）の明示的前提。Wave 3a で Contracts asmdef 切り出しが完了済み。新規 Contracts を作ると DTO 重複と双方の同期コストが発生する。 | 低 |
| SLOA-2 | パッケージ命名 | **`com.hidano.vtuber-system-base.stage-lighting-volume-output-adapter`** とする。`character-selection-tab` / `stage-lighting-volume-tab` と命名規約を揃える。 | リポジトリ既存パッケージとの一貫性。`com.hidano.*` は基盤 3 パッケージ専用、`jp.hidano.*` は Wave 3 以降のタブ・アダプタ系に統一。 | 低 |
| SLOA-3 | URP 依存の扱い | **`UnityEngine.Rendering.Universal` および `Unity.RenderPipelines.Core.Runtime` を Runtime asmdef の参照に明示追加する**。Contracts asmdef は URP 直接依存を持たないため、URP API 呼出は本 spec 内に閉じ込める。 | URP の `VolumeManager`, `VolumeProfile.Add<T>()`, `VolumeComponent`, `VolumeParameter<T>` を扱うため必須。Contracts は Unity 標準型（`RenderTexture` まで）に留まっており分離が成立している。 | 中（URP メジャー更新で API 変更を被る可能性。`docs/integration-plan.md` §7.3 のリスク 1 と整合し、`manifest.json` でバージョン固定して再検証トリガとする） |
| SLOA-4 | Stage Prefab の解放戦略 | **新ステージ Instantiate 完了後、旧ステージ GameObject を破棄する前に必ず `Addressables.ReleaseInstance(oldGameObject)` を呼ぶ**。`Object.Destroy` のみではアセットハンドルがリークする。`stage/active` 受信時の処理は (1) 新ステージの `InstantiateAsync` 開始 → (2) 完了後に旧ステージ Release → (3) `stage/current` state 更新 → (4) `stage/loaded` event publish の順とする。 | `Addressables.InstantiateAsync` は内部で AssetBundle ロード参照カウントを増やすため、`Object.Destroy` のみではアンロードトリガが立たない。Unity Addressables 2.x の公式仕様。 | 高（解放漏れは長時間運用でメモリリーク。実装フェーズで `IInstantiationProvider` のテストを必須化する） |
| SLOA-5 | Stage 切替中の中間状態 | **新ステージのロードが完了するまでは旧ステージを破棄しない（lazy swap）**。ロード中は `stage/current` を「旧ステージのまま」で publish し続け、完了後に新ステージへ更新する。失敗時は `stage/load-failed` event を publish して旧ステージのまま継続する。 | 配信中の切替で「ステージ無し」の真っ暗な瞬間が生じることを構造的に回避（SL-11）。配信適合性に直結。 | 中（同時に複数の load 要求が来た場合の調停ロジックが必要。最後の要求のみ採用する FIFO 直列化方針を実装フェーズで確定） |
| SLOA-6 | Light GameObject の階層配置 | **`LightsRoot` 直下に `Light_{lightId}` 命名の GameObject を作成する**。Hierarchy 上で識別しやすくし、診断ログでも GameObject 名から lightId を逆引き可能にする。lightId は `Guid.NewGuid().ToString("N")` で 32 桁 hex。 | ハンドラ実装で `LightsRoot.Find($"Light_{lightId}")` の単純検索で対象 GameObject を取得できる。診断時に Hierarchy を見れば即座に状態が分かる運用上の利点。 | 低 |
| SLOA-7 | Volume Override の動的追加 | **`Profile.Add<T>(overrideState: true)` で初回追加し、以後は `Profile.TryGet<T>(out var component)` で取得して `component.active = enabled`、`component.<param>.value = ...` および `component.<param>.overrideState = true` を直接設定する**。設定変更は `VolumeManager.instance.ResetMainStack()` を呼ばずに済む（`VolumeProfile.components` への変更は次フレームで自動反映）。 | URP の Volume パイプラインは `Profile.components` リストを参照するため、`AddComponent<T>` 相当の `Add<T>` で追加するだけで反映される。`overrideState` を立てないと URP が「未オーバーライド」として扱い反映されない点に注意。 | 中（リフレクションベースのパラメータセッタは URP の `VolumeParameter<T>` ジェネリック展開に依存。実装フェーズで複数パラメータ型のラウンドトリップテストを必須化） |
| SLOA-8 | Volume Override メタデータ送信戦略 | **`VolumeManager.instance.baseComponentTypeArray` を起動時に 1 度走査してメタデータを構築し、`volume/overrides/metadata` request を受けたら同じ結果を即返す**。利用者プロジェクトが独自 `VolumeComponent` を追加していても、`VolumeManager` の登録で自動列挙される。 | URP の `VolumeManager` は静的に baseComponentTypeArray を保持し、利用者 VolumeComponent も `[VolumeComponentMenu]` 等で登録される。動的追加は想定しない（Unity 起動後の型登録は通常起こらない）。 | 中（利用者プロジェクトが Addressables 経由で動的に AssetBundle 内 VolumeComponent をロードする極端なケースは未対応。設計フェーズで「アダプタ起動時固定」と明記） |
| SLOA-9 | プレビューカメラ実装の責務 | **本 spec が `StagePreviewHost` MonoBehaviour を提供し、`CamerasRoot` 配下にプレビュー専用 Camera + RenderTexture を配置する**。`SceneViewStyleCameraController` を取り付け、`IPreviewHostService` を実装して `StagePreviewHostLocator.Register(this)` でグローバル登録する。RenderTexture は既定 1280×720（タブ design.md の 640×360 から増量）、フォーマット `RenderTextureFormat.ARGB32`、配信用メイン出力カメラとは culling mask 共通だが targetDisplay 不使用（RT 専用）。 | UI 側 `PreviewPanelController` は `StagePreviewHostLocator.Current` 経由で RT を取得して `VisualElement.style.backgroundImage` に貼る。配信に載るメイン出力カメラとは別 Camera で、配信映像に影響しない（SL-1, Requirement 2.5）。 | 中（プレビューカメラ追加で GPU 負荷がメイン出力カメラに加算される。`docs/requirements.md` §6.1 のフレームレート要件と整合性検証が必要） |
| SLOA-10 | `IPreviewHostService` の RT 解像度可変対応 | **`RenderTextureChanged` event は RT が再生成された場合（解像度変更・破棄等）にのみ発火し、内容更新（毎フレームの描画）では発火しない**。UI 側はこの event を購読し、null 受信時にキャッシュ参照を破棄する。 | RT は配信中は再生成されないが、エディタのウィンドウサイズ変更や明示的 dispose 時には null へ遷移する。UI 側がキャッシュを保持し続けると破棄済み RT への参照で例外。 | 低 |
| SLOA-11 | プレビューカメラの enable/disable 経路 | **`preview/command`（op="set-enabled", Enabled=false）受信で `Camera.enabled = false` を設定し描画停止、`Enabled=true` で再開する**。タブ非アクティブ化時に UI 側から送信される（Requirement 2.6）。視点リセットは `op="reset-view"` で `SceneViewStyleCameraController.ResetView()` 相当を呼ぶ。 | GPU 負荷の動的削減（`docs/requirements.md` §6.1）。`Camera.enabled` の操作は同期で軽量。 | 低 |
| SLOA-12 | エラーハンドリング戦略 | **すべてのハンドラは `try/catch` で例外を捕捉し、診断ログ + 該当 topic に対応するエラー event（`light/error` / `stage/load-failed`）を publish する**。例外を `IOutputCommandDispatcher` まで伝播させない（描画継続のため、Requirement 5.5 of `output-renderer-shell` 継承）。`LightErrorDto.ErrorCode` は `"limit_exceeded"` / `"internal_error"` / `"not_found"` を採用。 | メイン出力描画停止は配信事故。UI 側は error event 経由で UI 縮退を実施する（SL の Requirement 9）。 | 低 |
| SLOA-13 | ハンドラ登録の解除 | **本 spec の `StageLightingVolumeOutputAdapterBootstrapper` は `OutputSceneBootstrapper` のライフサイクルに乗り、`OnDestroy`（PlayMode 終了）で全 `IDisposable` 登録トークンを Dispose する**。`StagePreviewHostLocator.Unregister(this)` も忘れずに呼ぶ。 | `output-renderer-shell` の `IOutputCommandDispatcher` は登録解除トークン経由でハンドラ解除する設計（OR Requirement 3.3）。ハンドラリーク防止と PlayMode 反復時のクリーン再初期化（Requirement 11.4 of SL 継承）。 | 中（`Locator.Unregister` 漏れで UI 側が破棄済み Host 参照を握り続けるリスク → `IPreviewHostService.RenderTextureChanged(null)` を OnDestroy 時に発火して通知） |
| SLOA-14 | リフレクションベースの VolumeParameter 設定 | **`VolumeComponent` の public `FieldInfo` を列挙して `VolumeParameter<T>` 派生フィールドを取得し、`VolumeOverrideParamValueDto.Kind` に応じて `value` プロパティをリフレクション経由で代入する**。型マッピングは `Bool→BoolParameter`, `Int→IntParameter / NoInterpIntParameter / ClampedIntParameter`, `Float→FloatParameter / ClampedFloatParameter / MinFloatParameter / MaxFloatParameter`, `Color→ColorParameter`, `Vector2/3/4→Vector2Parameter/Vector3Parameter/Vector4Parameter`, `Enum→<EnumName>Parameter`。 | URP は `VolumeParameter<T>` ジェネリック型を多数（30 種以上）派生させているため、case 文での個別対応はメンテ負担が大きい。`FieldInfo.SetValue` + `param.value` 直接操作で汎用化できる。 | 中（IL2CPP の strip 警告対策が必要。`link.xml` で `UnityEngine.Rendering` namespace を保護する。実装フェーズで Standalone ビルド検証必須） |
| SLOA-15 | 起動時の `stage/catalog` 公開 | **本 spec の Bootstrapper は起動完了時（`OutputSceneBootstrapper` の Init 完了直後）に `stage/catalog` を 1 度 publish する**。Addressables の `LoadResourceLocationsAsync(label: "stage")` で「stage」ラベル付き Addressables location を全件取得し、`StageCatalogEntryDto` 配列にして送信する。利用者プロジェクトは Addressables Group の Asset に「stage」ラベルを付与する規約。 | `stage-lighting-volume-tab` の Requirement 3.1 が `stage/catalog` を state として購読する設計。出力側は起動時に 1 度送れば、UI 側は `IUiSubscriptionClient` の "再送" 機能（最終 state 受信）で取得できる。 | 中（「stage」ラベル規約は利用者プロジェクトの責務として README に明記。違反時は空カタログ返却 + 警告ログ） |
| SLOA-16 | サムネイル Addressables key の規約 | **`StageCatalogEntryDto.ThumbnailAddressableKey` は `{StageAddressableKey}.thumbnail` 形式の慣例とする**。実 Addressables key の存在確認は本 spec では行わず、UI 側 `IAsyncAssetLoader` のロード失敗時に UI 側でフォールバックする（既存の `character-selection-tab` の `DefaultAvatarThumbnail` フォールバックと同パターン）。 | UI 側が Addressables 解決の責務を持つ既存設計（UI-6）と整合。出力側でサムネイル存在検証を入れると 2 重チェックになる。 | 低 |
| SLOA-17 | 単一プロセス前提の Locator 利用 | **`StagePreviewHostLocator` は同一プロセス内 Singleton として利用する**。複数の `StagePreviewHost` が同時登録された場合は最新登録を優先（既存 Locator 実装の挙動に従う）し、警告ログを残す。プロセス間（LAN タブレット UI 等）では本 Locator は機能しないが、それは本 spec のスコープ外（`docs/requirements.md` §6.3 拡張性スコープ）。 | `RenderTexture` の native ハンドルは IPC 越しに送れないため、同一プロセス前提は構造的に避けられない（`stage-lighting-volume-tab` design.md SL D-1 と整合）。 | 低 |
| SLOA-18 | `volume/overrides/metadata` の SchemaVersion | **`SchemaVersion = 1` で固定送信する**。スキーマ拡張時は `core-ipc-foundation` D-7（未知フィールド無視）に従い後方互換を維持し、`SchemaVersion = 2` への増分は新フィールド追加のみで破壊的変更を行わない。 | UI 側 `VolumeSchemaCache` のキャッシュ無効化を不必要に発生させない。利用者プロジェクトの独自 VolumeComponent は `Types` リストへの追加のみで拡張できる。 | 低 |
| SLOA-19 | プレビューカメラの culling mask | **メイン出力カメラと完全同一の culling mask を採用する（layer 群を共有）**。タブ design.md SL-13「ステージ + ライティング + キャラクターすべてを映す」の要件に従う。`OutputSceneRoots.DefaultCamera.cullingMask` をコピーして設定する。 | プレビューが「実配信と同じ見た目」になることが画作りワークフローの前提。culling mask 違いで「プレビューには映るが本番に映らない」ような事故を防ぐ。 | 中（メイン出力カメラの culling mask が後から変更された場合の追従。実装フェーズで OnEnable 時の同期 or 監視機構を確定） |
| SLOA-20 | テスト戦略 | **`IOutputCommandDispatcher` のモック実装（`FakeOutputCommandDispatcher`）を経由した EditMode 単体テスト + URP の `Volume` API を実 PlayMode で検証する PlayMode テストの 2 段構成**。リフレクションパラメータセッタ（SLOA-14）は EditMode で `Bloom` / `Tonemapping` / `ColorAdjustments` の代表 3 型でラウンドトリップテストする。 | URP の `Volume` API は実 GameObject + 実 Camera が無いと動作確認できないため PlayMode 必須。一方ハンドラロジック自体（lightId 採番、Stage 切替順序、エラー event publish 等）は EditMode で十分検証可能。 | 中（PlayMode テストの実行時間が長くなりがち。タブの PlayMode テストと共通インフラを使う） |

---

## Requirements

## Introduction

本 spec は VTuberSystemBase における **stage-lighting-volume-tab のメイン出力側アダプタ（Stage-Lighting-Volume Output Adapter）** を定義する。Display 1 上のタブ UI が送信する IPC コマンド（`stage/active` / `light/command` / `light/{lightId}/{property}` / `volume/override/{type}/{param}` / `volume/overrides/metadata` / `preview/command` 等）を `output-renderer-shell` の `IOutputCommandDispatcher` 経由で受信し、メイン出力シーンの `LightsRoot` / `StageRoot` / `VolumeRoot` / `CamerasRoot` 配下に対して以下を実行する：

1. **ステージ Prefab の Addressables 経由ロード／アンロード／差し替え**：`stage/active` 受信で `Addressables.InstantiateAsync` し、旧ステージは `Addressables.ReleaseInstance` で必ず解放（SLOA-4）。新ステージのロード完了まで旧ステージを保持する lazy swap（SLOA-5）。`stage/catalog` を起動時に 1 度 publish（SLOA-15）。
2. **Light GameObject の動的生成・編集・破棄**：`light/command`（add/remove）event を受け、`LightsRoot` 配下に `Light_{lightId}` を Instantiate し、`light/{lightId}/{property}` state で `Light` / `Transform` プロパティを反映（SLOA-6）。lightId はメイン出力側で `Guid.NewGuid().ToString("N")` 採番、`light/added` event で UI 側へ返却（SL-3 継承）。`lights/list` を Light 配列として publish。
3. **URP Global Volume Override の動的適用**：`volume/override/{type}/enabled` state で `VolumeProfile.Add<T>()` または `TryGet<T>` 経由で `VolumeComponent` を有効化／無効化、`volume/override/{type}/{param}` state でリフレクションベースの `VolumeParameter<T>.value` 代入（SLOA-7, SLOA-14）。`volume/overrides/metadata` request で `VolumeManager.baseComponentTypeArray` 走査結果を `VolumeOverrideSchemaDto` として返す（SLOA-8）。
4. **プレビューカメラの提供と RT 共有**：`StagePreviewHost` MonoBehaviour を `CamerasRoot` 配下に配置し、`SceneViewStyleCameraController` を取り付け、`IPreviewHostService` を実装して `StagePreviewHostLocator.Register(this)` でグローバル登録する（SLOA-9）。`preview/command` event で `Camera.enabled` 切替・視点リセットを行い、`preview/state` で結果を publish（SLOA-11）。
5. **エラーハンドリング**：すべてのハンドラは `try/catch` で例外を捕捉し、`light/error` / `stage/load-failed` event を publish して UI 側に通知する（SLOA-12）。例外を `IOutputCommandDispatcher` まで伝播させず、メイン出力描画を停止させない（OR Requirement 3.6, 5.5 継承）。
6. **PlayMode 反復時のクリーン再初期化**：`OutputSceneBootstrapper` の OnDestroy で全 `IDisposable` 登録トークンを Dispose し、`StagePreviewHostLocator.Unregister` も忘れずに呼ぶ（SLOA-13）。

本 spec は **メイン出力側アダプタの実装** に限定される。UI 側 `stage-lighting-volume-tab` の UIDocument・ViewModel・プリセット永続化・UI Toolkit シェル基盤・IPC トランスポート・配信用メイン出力カメラ操作・キャラクター（アバター）の生成は本 spec の責務外である。

## Boundary Context

- **In scope**:
  - `stage-lighting-volume-tab` の Contracts asmdef（`StageLightingTopics`, `*Dto`, `IPreviewHostService`, `StagePreviewHostLocator`）を参照したハンドラ実装（SLOA-1）
  - `output-renderer-shell` の `IOutputCommandDispatcher.RegisterStateHandler<T>` / `RegisterEventHandler<T>` / `RegisterRequestHandler<TReq,TRes>` への登録および登録解除トークン管理（SLOA-13）
  - `StageRoot` 配下のステージ Prefab Instantiate / Release（Addressables 2.x、SLOA-4, SLOA-5）
  - 起動時の `stage/catalog` publish（Addressables ラベル「stage」を起点、SLOA-15）
  - `LightsRoot` 配下の Light GameObject 生成・編集・破棄、`Light_{lightId}` 命名（SLOA-6）
  - lightId のメイン出力側採番（`Guid.NewGuid().ToString("N")`、SL-3 継承）
  - `lights/list` state publish（Light 配列）
  - URP Global Volume Override の動的適用（`VolumeProfile.Add<T>`、リフレクションベースのパラメータセッタ、SLOA-7, SLOA-14）
  - `volume/overrides/metadata` request handler（`VolumeManager.baseComponentTypeArray` 走査、SLOA-8）
  - `StagePreviewHost` MonoBehaviour の提供（`CamerasRoot` 配下、`SceneViewStyleCameraController` 取り付け、`IPreviewHostService` 実装、SLOA-9）
  - `StagePreviewHostLocator` への Register/Unregister 連動（SLOA-13）
  - `preview/command` event handler（`Camera.enabled` 切替、視点リセット、SLOA-11）
  - `preview/state` state publish
  - エラー event の publish（`light/error`, `stage/load-failed`、SLOA-12）
  - `OutputSceneBootstrapper` のライフサイクルへの組み込み（PlayMode 開始/停止に追従、Edit モードで非常駐）
  - スタンドアロンビルドと Unity Editor PlayMode の両対応（D-9 継承）
  - 本 spec 単体検証（Fake `IOutputCommandDispatcher` + 実 URP Volume API 経由の PlayMode 検証）
- **Out of scope**:
  - **UI 側 `stage-lighting-volume-tab` の UIDocument / ViewModel / View / プリセット永続化 / UI Toolkit シェル統合**（別 spec の責務）
  - **配信に載るメイン出力カメラの操作・切替**（`camera-switcher-output-adapter` の責務、本 spec はプレビューカメラのみ扱う）
  - **キャラクター（アバター）の生成・操作**（`rac-main-output-adapter` の責務）
  - **`core-ipc-foundation` のトランスポート・シリアライゼーション・接続管理**
  - **`output-renderer-shell` のシーン骨格・IPC サーバ起動・ディスプレイ振り分け**（本 spec はディスパッチャ登録の側）
  - **`stage-lighting-volume-tab` の `Contracts` asmdef そのものの定義**（既存）
  - **永続化ファイル I/O**（UI 側 `JsonPresetStorage` の責務）
  - **Addressables Group の構成・ステージ Prefab そのものの中身**（利用者プロジェクトの責務）
  - **独自 `VolumeComponent` の追加**（利用者プロジェクトの責務、本 spec はメタデータ列挙のみ）
- **Adjacent expectations**:
  - `core-ipc-foundation` がメインスレッド配信（D-3）と state coalesce / event FIFO / request 相関（D-7）を保証していること
  - `output-renderer-shell` が `IOutputSceneRoots`（`Stage`, `Lights`, `Cameras`, `Volumes`, `GlobalVolumeProfile`, `DefaultCamera`）を提供していること
  - `output-renderer-shell` の `IOutputCommandDispatcher` が `RegisterStateHandler<T>` / `RegisterEventHandler<T>` / `RegisterRequestHandler<TReq,TRes>` を提供し、ハンドラ解除トークン（`IDisposable`）を返すこと
  - `stage-lighting-volume-tab` の Contracts asmdef が `StageLightingTopics` 定数 + DTO 群 + `IPreviewHostService` + `StagePreviewHostLocator` を提供していること（Wave 3a で完了済み）
  - 利用者プロジェクトが Addressables Group に「stage」ラベル付きで Stage Prefab を登録し、サムネイル Addressables（`{stageKey}.thumbnail`）を併せて登録していること（SLOA-15, SLOA-16）
  - 利用者プロジェクトが必要に応じて独自 `VolumeComponent` を `[VolumeComponentMenu]` で URP に登録していること（SLOA-8）
  - `SceneViewStyleCameraController` パッケージ（v1.0.1）が Unity 6.3 で利用可能で、`Camera` GameObject に AddComponent でアタッチして使えること

---

### Requirement 1: パッケージ構造とアセンブリ境界

**Objective:** spec 実装者として、本 spec を `output-renderer-shell` のシーンに後付けするメイン出力側アダプタとして UPM パッケージで構造化したい。そうすればタブ spec / 基盤 spec / 他のアダプタ spec と独立に開発・更新でき、利用者プロジェクトに UPM 経由で取り込み可能になる。

#### Acceptance Criteria

1. The Output Adapter shall UPM パッケージ `com.hidano.vtuber-system-base.stage-lighting-volume-output-adapter` として `Packages/` 配下に配置されること（SLOA-2）。
2. The Output Adapter shall Runtime asmdef `VTuberSystemBase.StageLightingVolumeOutputAdapter.Runtime` を 1 つだけ提供し、参照は `VTuberSystemBase.StageLightingVolumeTab.Contracts` / `VTuberSystemBase.OutputRendererShell.Runtime` / `VTuberSystemBase.CoreIpc.Abstractions` / `Unity.RenderPipelines.Universal.Runtime` / `Unity.RenderPipelines.Core.Runtime` / `Unity.Addressables` / `SceneViewStyleCameraController.Runtime` に限定する（SLOA-1, SLOA-3）。
3. The Output Adapter shall `stage-lighting-volume-tab` の `Runtime` asmdef（UI 側 ViewModel/View）を一切参照しないこと（UI と出力の双方向参照を避けるため）。
4. The Output Adapter shall 新規 Contracts asmdef を作成せず、`stage-lighting-volume-tab` の既存 Contracts asmdef（`VTuberSystemBase.StageLightingVolumeTab.Contracts`）を契約共有源として参照すること（SLOA-1）。
5. The Output Adapter shall `package.json` で Unity 6.3 を最低バージョンとして宣言し、依存パッケージ（URP、Addressables、SceneViewStyleCameraController、stage-lighting-volume-tab）を明記する。
6. The Output Adapter shall `.meta` ファイルの GUID を都度ランダム 32 桁 hex 生成（連続パターン禁止）で発行する（CLAUDE.md ルール準拠）。
7. The Output Adapter shall Tests asmdef（EditMode + PlayMode）を提供し、Runtime asmdef とは別 asmdef で隔離する。

---

### Requirement 2: ハンドラ登録と Bootstrap ライフサイクル

**Objective:** 配信運用者として、メイン出力シーン起動時に本アダプタが自動で `IOutputCommandDispatcher` にハンドラを登録し、PlayMode 終了時にきれいに解除されることを期待したい。そうすれば再生・停止を繰り返しても購読リーク・ハンドラ重複が発生せず、開発フローと本番運用の両方で安定する。

**Note:** 本要件は SLOA-13 を具現化し、`output-renderer-shell` Requirement 3.3（登録／解除 API）と整合する。

#### Acceptance Criteria

1. The Output Adapter shall `OutputSceneBootstrapper` のライフサイクル（Awake/Start/OnDestroy）に組み込まれ、`Start` 完了時点（`OutputSceneInitPhase.DispatcherReady` 以降）でハンドラ登録を完了させる。
2. The Output Adapter shall 以下のハンドラを `IOutputCommandDispatcher` に登録すること：
   - `RegisterStateHandler<StageCommandDto>(StageLightingTopics.StageActive, ...)` — `stage/active` 受信ハンドラ
   - `RegisterEventHandler<StageCommandDto>(StageLightingTopics.StageCommand, ...)` — `stage/command`（load/unload）受信ハンドラ
   - `RegisterEventHandler<LightCommandDto>(StageLightingTopics.LightCommand, ...)` — Light add/remove
   - `RegisterStateHandler<LightPropertyValueDto>(StageLightingTopics.LightProperty(lightId, prop), ...)` — Light 単一プロパティ（lightId 採番後に動的登録）
   - `RegisterStateHandler<bool>(StageLightingTopics.VolumeOverrideEnabled(typeFullName), ...)` — Volume Override enabled
   - `RegisterStateHandler<VolumeOverrideParamValueDto>(StageLightingTopics.VolumeOverrideParam(typeFullName, paramName), ...)` — Volume Override param
   - `RegisterRequestHandler<EmptyDto, VolumeOverrideSchemaDto>(StageLightingTopics.VolumeOverrideSchema, ...)` — Volume Override metadata
   - `RegisterEventHandler<PreviewCommandDto>(StageLightingTopics.PreviewCommand, ...)` — Preview command
3. The Output Adapter shall ハンドラ登録時に `IDisposable` 登録解除トークンを保持し、`OnDestroy` 時に逆順で全 Dispose する。
4. The Output Adapter shall `OnDestroy` 時に `StagePreviewHostLocator.Unregister(this)` を呼び、`IPreviewHostService.RenderTextureChanged(null)` を発火して UI 側のキャッシュ参照を破棄させる（SLOA-13）。
5. The Output Adapter shall PlayMode 開始・停止を 5 回繰り返してもハンドラ登録重複・GameObject リーク・Addressables ハンドルリーク・`StagePreviewHostLocator` 滞留が発生しないこと（D-9 継承）。
6. The Output Adapter shall Edit モードでは常駐せず、`Application.isPlaying == true` のときのみ初期化処理を実施する（D-9 継承）。
7. The Output Adapter shall ドメインリロードに跨る状態維持を試みず、PlayMode 開始のたびに新規初期化する（D-9 継承）。
8. If 初期化中に `IOutputSceneRoots` または `IOutputCommandDispatcher` が解決できなかった場合, the Output Adapter shall 診断ログにエラーを記録したうえで何も登録せず終了し、メイン出力描画を阻害しない。
9. The Output Adapter shall 初期化完了後に `stage/catalog` を 1 度 publish して UI 側 `StageCatalogState` の購読初期値を満たす（SLOA-15）。

---

### Requirement 3: ステージ Prefab の Addressables 経由ロード／アンロード

**Objective:** 配信オペレーターとして、UI 側でステージを切り替えたときに、新ステージのロード完了までは旧ステージが映り続け（途中で真っ暗にならず）、完了と同時にシームレスに切り替わる体験を得たい。そうすれば配信中の切替事故を構造的に回避できる。

**Note:** 本要件は SLOA-4, SLOA-5, SLOA-15 を具現化し、SL Requirement 3 を出力側で受ける契約を定義する。

#### Acceptance Criteria

1. The Output Adapter shall 起動時に Addressables の `LoadResourceLocationsAsync(label: "stage")` で「stage」ラベル付きアセット一覧を取得し、`StageCatalogEntryDto`（AddressableKey + DisplayName + ThumbnailAddressableKey=`{key}.thumbnail`）の配列にして `stage/catalog` state を publish する（SLOA-15, SLOA-16）。
2. When `stage/active`（state, `StageCommandDto.Op="load"` または `"unload"`）を受信したとき、the Output Adapter shall 以下の lazy swap 順序でステージ切替を実行する（SLOA-5）：
   1. 新ステージのロード（`Addressables.InstantiateAsync(addressableKey, parent: StageRoot)`）を開始する。
   2. ロード完了（`AsyncOperationHandle<GameObject>.Completed`）を待つ。
   3. 旧ステージが存在する場合、`Addressables.ReleaseInstance(oldGameObject)` を呼ぶ（必須、SLOA-4）。
   4. 新ステージ参照を内部に保持し、`stage/current` state を新 AddressableKey で publish する。
   5. `stage/loaded` event を `StageCurrentDto` で publish する。
3. When `stage/active`（`Op="unload"`）を受信したとき、the Output Adapter shall 旧ステージが存在すれば `Addressables.ReleaseInstance(oldGameObject)` を呼んで GameObject を破棄し、`stage/current` を `AddressableKey=null` で publish する。
4. If 新ステージのロードが失敗した場合, the Output Adapter shall 旧ステージを保持したまま、`stage/load-failed` event を `StageLoadFailedDto`（AddressableKey + ErrorCode + Message）で publish する。`stage/current` は更新しない（SL-11 継承）。
5. While 既にロード中の `stage/active` 要求が完了する前に新たな要求が到着した場合, the Output Adapter shall 直前の要求の完了を待ってから新要求を処理する直列化（FIFO）を保証する。中間で複数要求が積まれた場合、最後の要求のみ採用してもよい（最終 state の last-write-wins と整合）。
6. The Output Adapter shall ステージ Prefab を `StageRoot.transform` の **直下** に Instantiate し、Position/Rotation を Identity（Vector3.zero / Quaternion.identity）で配置する。スケール変更・親子付け替えは行わない。
7. The Output Adapter shall PlayMode 終了時に保持中のステージ GameObject を `Addressables.ReleaseInstance` で解放する（リーク防止、SLOA-4）。
8. If Addressables「stage」ラベルが利用者プロジェクトに登録されていない場合, the Output Adapter shall 空の `StageCatalogDto.Items` を publish し、警告ログを残す。本アダプタの他機能（Light, Volume, Preview）の動作は阻害しない。
9. The Output Adapter shall Addressables 関連の例外を try/catch で捕捉し、診断ログに記録、`stage/load-failed` event の publish のみで描画継続を維持する（SLOA-12）。
10. The Output Adapter shall `stage/active` および `stage/command` の両方の topic を Stage 切替操作として受け付け、payload `StageCommandDto` の `Op` フィールドに従って分岐する（タブ spec 側の topic 命名揺らぎへの吸収）。

---

### Requirement 4: Light GameObject の動的生成・編集・破棄

**Objective:** 配信オペレーターとして、UI から任意個数の Light を追加・削除し、各 Light の type / 角度 / 色 / 強さ / range / spotAngle を編集すると、メイン出力（Display 2+）に即座に反映される体験を得たい。そうすれば「画作り」のイテレーションが高速に回る。

**Note:** 本要件は SLOA-6, SLOA-12 と SL-2, SL-3, SL-5, SL-6 を具現化する。

#### Acceptance Criteria

1. When `light/command` event を `Op="add"` で受信したとき, the Output Adapter shall 以下を実行する：
   1. lightId を `Guid.NewGuid().ToString("N")` で採番する（SL-3 継承）。
   2. `LightsRoot.transform` 配下に `Light_{lightId}` 命名の新規 GameObject を作成し、`Light` コンポーネントを `AddComponent<UnityEngine.Light>()` する（SLOA-6）。
   3. `LightInitialDto` の値（Type, Rotation, Color, Intensity, Range, SpotAngle, DisplayName）を `Light` コンポーネントおよび `Transform.localRotation` に反映する。
   4. `light/added` event を `LightAddedDto`（LightId + Initial）で publish する。
   5. `lights/list` state を最新 Light 配列で publish する。
   6. 当該 lightId の各プロパティ（intensity / color / rotation / type / range / spotAngle / displayName）の `RegisterStateHandler` を動的登録する。
2. When `light/command` event を `Op="remove"` で受信したとき, the Output Adapter shall 以下を実行する：
   1. 対象 lightId の GameObject を `LightsRoot.Find($"Light_{lightId}")` で取得し、`Object.Destroy(go)` で破棄する。
   2. 当該 lightId のプロパティハンドラ登録トークンを Dispose する。
   3. `lights/list` state を更新後 Light 配列で publish する。
3. When `light/{lightId}/intensity` state を受信したとき, the Output Adapter shall 当該 Light の `Light.intensity` を payload 値で更新する。
4. When `light/{lightId}/color` state を受信したとき, the Output Adapter shall 当該 Light の `Light.color` を payload 値（`ColorDto.R/G/B/A` を `Color` に変換）で更新する。
5. When `light/{lightId}/rotation` state を受信したとき, the Output Adapter shall 当該 Light の `Transform.localRotation` を payload 値（`Vector3Dto` を `Quaternion.Euler` で変換）で更新する。
6. When `light/{lightId}/type` state を受信したとき, the Output Adapter shall 当該 Light の `Light.type` を `LightTypeDto`→`LightType` のマッピング（Directional/Point/Spot/Area）で更新する。
7. When `light/{lightId}/range` state を受信したとき, the Output Adapter shall 当該 Light の `Light.range` を payload 値で更新する。
8. When `light/{lightId}/spotAngle` state を受信したとき, the Output Adapter shall 当該 Light の `Light.spotAngle` を payload 値で更新する。
9. When `light/{lightId}/displayName` state を受信したとき, the Output Adapter shall 当該 Light の GameObject 名を `Light_{lightId}` のまま維持する（命名は lightId 固定、表示名は内部メタデータとして保持）が、`lights/list` の `LightListItemDto.DisplayName` には反映する。
10. If 対象 lightId が存在しない state を受信した場合, the Output Adapter shall 警告ログのみ記録して当該 state を破棄し、例外を投げない。
11. If Light add 処理中に例外が発生した場合, the Output Adapter shall `light/error` event を `LightErrorDto`（LightId=null, ErrorCode="internal_error", Message=...）で publish し、メイン出力描画を継続する（SLOA-12）。
12. The Output Adapter shall PlayMode 終了時に保持中の全 Light GameObject を `Object.Destroy` で破棄し、ハンドラ登録トークンを Dispose する。
13. The Output Adapter shall 初期化完了直後に `lights/list` を空配列（`Items=[]`）で 1 度 publish して UI 側の購読初期値を満たす。

---

### Requirement 5: URP Global Volume Override の動的適用

**Objective:** 配信オペレーターとして、UI から Bloom や Tonemapping 等の URP Volume Override を on/off / param 編集すると、メイン出力（Display 2+）に即座に反映される体験を得たい。そうすれば配信のポストエフェクトを画作りで詰められる。

**Note:** 本要件は SLOA-7, SLOA-8, SLOA-14 と SL Requirement 6 / SL-7 を出力側で受ける契約を定義する。

#### Acceptance Criteria

1. When `volume/overrides/metadata` request を受信したとき, the Output Adapter shall `VolumeManager.instance.baseComponentTypeArray` を列挙し、各型について以下を含む `VolumeOverrideSchemaDto` を返す：
   - `SchemaVersion = 1`（SLOA-18）
   - `Types`: 各型につき `TypeFullName`, `DisplayName`（`[VolumeComponentMenu]` 由来 or 型短名）, `Params` 配列
   - `Params` 各エントリ: `ParamName`（フィールド名）, `Kind`（`ParamKind` 列挙）, `DisplayName`, `DefaultValue`, `Range`（min/max または `EnumValues`）
2. The Output Adapter shall 起動時に `VolumeManager.baseComponentTypeArray` を 1 度走査し、`Dictionary<string typeFullName, Type type>` のマッピングと `VolumeOverrideSchemaDto` を構築してキャッシュする（SLOA-8）。
3. When `volume/override/{typeFullName}/enabled` state を受信したとき, the Output Adapter shall 以下を実行する：
   - 対象 `VolumeProfile`（`OutputSceneRoots.GlobalVolumeProfile`）に当該型の `VolumeComponent` が未追加の場合、`profile.Add<T>(overrideState: true)` で追加する（SLOA-7）。
   - 既追加の場合は `profile.TryGet<T>(out var component)` で取得する。
   - `component.active = enabled`（payload bool 値）に設定する。
4. When `volume/override/{typeFullName}/{paramName}` state を `VolumeOverrideParamValueDto` 値で受信したとき, the Output Adapter shall 以下を実行する：
   - 対象 `VolumeComponent` が未追加なら `Add<T>(overrideState: true)` で追加する。
   - 当該 `VolumeComponent` の `paramName` フィールドをリフレクションで取得（`FieldInfo` 経由、SLOA-14）。
   - `VolumeOverrideParamValueDto.Kind` に応じて：
     - `Bool` → `BoolParameter.value = BoolValue`
     - `Int` → `IntParameter / NoInterpIntParameter / ClampedIntParameter.value = IntValue`
     - `Float` / `ClampedFloat` → `FloatParameter / ClampedFloatParameter / MinFloatParameter / MaxFloatParameter.value = FloatValue`
     - `Color` → `ColorParameter.value = new Color(R, G, B, A)`
     - `Vector2` / `Vector3` / `Vector4` → 対応 `Vector*Parameter.value = VectorValue` から変換
     - `Enum` → 対応 enum 値を `EnumValue` 文字列から `Enum.Parse` で取得して代入
   - `VolumeParameter<T>.overrideState = true` を必ず設定する（SLOA-7）。
5. If `typeFullName` が `VolumeManager.baseComponentTypeArray` に存在しない場合, the Output Adapter shall 警告ログのみ記録し state を破棄する（例外を投げない）。
6. If `paramName` が当該 VolumeComponent のフィールドに存在しない場合, the Output Adapter shall 警告ログのみ記録し state を破棄する。
7. If `VolumeOverrideParamValueDto.Kind` と実際の `VolumeParameter<T>` 型が不整合な場合, the Output Adapter shall 警告ログを記録し当該 param を破棄する（部分適用、SLOA-12）。
8. The Output Adapter shall リフレクションパラメータセッタを IL2CPP ビルド対応のため `link.xml` で `UnityEngine.Rendering` namespace の strip を抑止する設定を提供する（SLOA-14）。
9. The Output Adapter shall `volume/override` 系の処理を try/catch で囲み、例外時は警告ログ + 該当 state 破棄でメイン出力描画を継続する（SLOA-12）。
10. The Output Adapter shall PlayMode 終了時に GlobalVolumeProfile から本 spec で追加した `VolumeComponent` をクリアする（`profile.components.Clear()` または個別 `Remove` 呼出し）。`output-renderer-shell` が提供する空 VolumeProfile の状態に戻す。

---

### Requirement 6: プレビューカメラと RenderTexture 共有

**Objective:** 配信オペレーターとして、Display 1 のタブ UI 内に表示されるプレビュー（引きカメラ）が、メイン出力と同じシーン状態（ステージ + Light + キャラクター）を映していて、配信用カメラとは別の視点で動かせる体験を得たい。そうすれば配信中でも配信映像に影響せず画作りができる。

**Note:** 本要件は SLOA-9, SLOA-10, SLOA-11, SLOA-19 を具現化し、SL Requirement 2 を出力側で受ける契約を定義する。

#### Acceptance Criteria

1. The Output Adapter shall `StagePreviewHost` MonoBehaviour を実装し、`CamerasRoot.transform` 配下に `PreviewCamera` 命名の GameObject を作成する（SLOA-9）。
2. The Output Adapter shall `PreviewCamera` GameObject に以下をアタッチする：
   - `UnityEngine.Camera` コンポーネント
   - `UnityEngine.Rendering.Universal.UniversalAdditionalCameraData` コンポーネント
   - `SceneViewStyleCameraController` コンポーネント
   - `StagePreviewHost` MonoBehaviour
3. The Output Adapter shall `Camera.targetDisplay` を **設定しない**（既定 0 のまま、RT 専用、SL Requirement 2.5）。`Camera.targetTexture` に既定 1280×720 / `RenderTextureFormat.ARGB32` の `RenderTexture` を生成して設定する（SLOA-9）。
4. The Output Adapter shall `Camera.cullingMask` を `IOutputSceneRoots.DefaultCamera.cullingMask` からコピーして設定する（SLOA-19）。
5. The Output Adapter shall `StagePreviewHost` に `IPreviewHostService` を実装し、`Awake` で `StagePreviewHostLocator.Register(this)`、`OnDestroy` で `StagePreviewHostLocator.Unregister(this)` を呼ぶ。
6. The Output Adapter shall `IPreviewHostService.CurrentRenderTexture` で現在の RT 参照を返し、`IsReady` で Awake 完了後 true を返す。
7. The Output Adapter shall RT が再生成された場合（解像度変更や明示的破棄）にのみ `RenderTextureChanged` event を発火し、毎フレームの内容更新では発火しない（SLOA-10）。
8. When `preview/command` event を `Op="set-enabled", Enabled=true` で受信したとき, the Output Adapter shall `Camera.enabled = true` を設定し描画を再開する（SLOA-11）。
9. When `preview/command` event を `Op="set-enabled", Enabled=false` で受信したとき, the Output Adapter shall `Camera.enabled = false` を設定し描画を停止する（SLOA-11、GPU 負荷削減）。
10. When `preview/command` event を `Op="reset-view"` で受信したとき, the Output Adapter shall `SceneViewStyleCameraController` の視点リセット API（`ResetView()` 等、実 API 名は実装フェーズで確定）を呼び、デフォルト視点に戻す。
11. When `preview/command` event を `Op="init"` で受信したとき, the Output Adapter shall `Camera.enabled = true` + `Awake` 後の初期視点で開始する。`Op="dispose"` で `Camera.enabled = false` + RT 破棄を行う。
12. The Output Adapter shall `preview/state` state を `PreviewStateDto`（Enabled, HostReady）で publish し、Camera enabled 切替や RT 再生成のたびに最新値で更新する。
13. If `StagePreviewHostLocator` に既に別の `IPreviewHostService` が登録されていた場合, the Output Adapter shall 警告ログを残しつつ最新登録を採用する（既存 Locator 実装の挙動に従う、SLOA-17）。
14. The Output Adapter shall PlayMode 終了時に `StagePreviewHostLocator.Unregister(this)` を呼び、`RenderTextureChanged(null)` を発火、RT を `RenderTexture.Release` + `Object.Destroy` で破棄する（SLOA-13）。
15. The Output Adapter shall プレビュー描画がメイン出力（Display 2+）の描画フレームに干渉しない設計とし、Camera は同一 URP パイプライン内で `RenderType.Base` として独立に描画する（SL Requirement 2.10 継承）。

---

### Requirement 7: エラー event の publish と縮退ハンドリング

**Objective:** 配信運用者として、本アダプタ内の異常時（Addressables ロード失敗、未知 lightId への state、リフレクションパラメータ代入失敗、Locator Singleton 衝突等）にメイン出力描画が止まらず、UI 側にも適切なエラー通知が届く体験を得たい。そうすれば部分的な障害が全体停止に発展せず、配信継続性を確保できる。

**Note:** 本要件は SLOA-12 を具現化し、`output-renderer-shell` Requirement 5.5（重大エラー時の描画維持）と SL Requirement 9（失敗・縮退ハンドリング）に整合する。

#### Acceptance Criteria

1. The Output Adapter shall すべてのハンドラ実装を `try/catch` で囲み、例外を `IOutputCommandDispatcher` まで伝播させない（OR Requirement 3.6 継承）。
2. If Addressables のステージロードが失敗した場合, the Output Adapter shall `stage/load-failed` event を `StageLoadFailedDto`（AddressableKey, ErrorCode in {"not_found", "load_failed", "instantiate_failed"}, Message）で publish する。
3. If Light 追加処理が失敗した場合（メモリ不足、内部例外等）, the Output Adapter shall `light/error` event を `LightErrorDto`（LightId=null, CorrelationId="", ErrorCode="internal_error", Message）で publish する。
4. If 既存 lightId に対する remove 要求で対象 GameObject が見つからない場合, the Output Adapter shall `light/error` event を `LightErrorDto`（LightId=対象, ErrorCode="not_found", Message）で publish し、`lights/list` を最新状態で再 publish する。
5. If Volume Override メタデータ走査中に例外が発生した場合, the Output Adapter shall 部分的なメタデータでも返せるなら返し、完全に失敗した場合は空 `Types` 配列の `VolumeOverrideSchemaDto`（SchemaVersion=1）を返す。
6. If `StagePreviewHost` の Awake で RT 生成が失敗した場合, the Output Adapter shall 警告ログを残し `IPreviewHostService.IsReady = false` のままにし、`preview/state` で `HostReady=false` を publish する。Locator への Register は実施しない。
7. The Output Adapter shall すべてのエラー event publish は `IUiCommandClient` ではなく、出力側の `core-ipc-foundation` サーバ経由の publish API で送信する（出力→UI 方向）。
8. The Output Adapter shall いかなる失敗経路においてもメイン出力（Display 2+）への OnGUI / IMGUI / 警告ダイアログ描画を行わない（OR Requirement 5 継承）。
9. The Output Adapter shall 診断ログ（Unity Console + 必要に応じて UI 側転送）に topic / lightId / typeFullName / paramName / 例外スタック等のコンテキストを構造化して出力する。

---

### Requirement 8: スタンドアロン／Editor PlayMode 両対応と PlayMode 反復対応

**Objective:** 開発者・配信運用者として、本アダプタがビルド後のスタンドアロン実行と Unity Editor PlayMode で同一挙動を示し、PlayMode 反復でリーク・重複が発生しない体験を得たい。そうすれば配信前リハーサルと本番運用の挙動差を最小化できる。

**Note:** D-9（PlayMode のみ常駐、Edit モード非常駐、ドメインリロード跨ぎなし）を継承する。

#### Acceptance Criteria

1. When Unity アプリケーションがスタンドアロンビルドとして起動し `OutputSceneBootstrapper` の Init が完了したとき, the Output Adapter shall ハンドラ登録 + `stage/catalog` publish + `lights/list` 初期 publish + `StagePreviewHost` Awake を自動実施する。
2. When Unity Editor が PlayMode に入ったとき, the Output Adapter shall スタンドアロンと同一手順で初期化を行う。
3. When Unity Editor が PlayMode を終了したとき, the Output Adapter shall ハンドラ登録解除トークン全 Dispose、Light/Stage GameObject 破棄、Addressables `ReleaseInstance` 呼出、`StagePreviewHost.Unregister`、RT `Release+Destroy`、Volume Override コンポーネント削除を実施し Edit モードに残留物を残さない。
4. While PlayMode の開始と停止が 5 回繰り返される間, the Output Adapter shall 以下が発生しないことを保証する：
   - 同一 topic への重複ハンドラ登録
   - `Light_{lightId}` GameObject の Hierarchy 残存
   - Addressables ハンドルリーク（`Addressables.PrintDiagnostics` で 0 件であること）
   - `StagePreviewHostLocator.Current` の非 null 残存
   - RT メモリリーク
5. The Output Adapter shall Edit モードでは実行時ロジック（ハンドラ登録、`StagePreviewHost` 起動、Volume 操作）を起動しない（D-9 継承）。
6. The Output Adapter shall ドメインリロード跨ぎでの状態維持を試みず、PlayMode 開始のたびに新規 GUID の lightId 採番、新規 RT 確保、新規メタデータキャッシュ構築を行う。
7. The Output Adapter shall スタンドアロン時と Editor PlayMode 時で、Stage 切替レイテンシ・Light プロパティ反映レイテンシ・Volume Override 反映レイテンシを構造的に同一に保つ（実装パスを `Application.isEditor` で分岐させない）。

---

### Requirement 9: 観測性・診断可能性

**Objective:** 開発者・配信運用者として、本アダプタで発生する不具合が「Stage 起因」「Light 起因」「Volume 起因」「Preview 起因」「ハンドラ登録起因」のいずれかを即座に切り分けたい。そうすれば問題切り分け時間を最小化し、本番配信中でも迅速に対応できる。

**Note:** 本要件の診断出力は `output-renderer-shell` Requirement 5（メイン出力非描画）と整合し、Unity Console と UI 側転送経路（将来拡張）の両方を想定する。

#### Acceptance Criteria

1. The Output Adapter shall ハンドラ登録／解除、`stage/catalog` 構築、Stage 切替、Light 追加/削除、Volume Override metadata 構築、Volume Override 適用、Preview 起動/破棄、エラー event publish のそれぞれについて開始・完了・失敗ログを記録する。
2. The Output Adapter shall ログメッセージに以下のコンテキストを構造化して付与する：topic / lightId / typeFullName / paramName / addressableKey / correlationId / 例外スタック（該当時）。
3. The Output Adapter shall 診断ログを Unity Console（`Debug.Log*`）にのみ出力し、メイン出力サーフェス（Display 2+）への OnGUI / IMGUI 描画を行わない（OR Requirement 5 継承）。
4. The Output Adapter shall ログレベル（Verbose / Info / Warning / Error）を外部設定で切替可能にし、既定は Info 以上を出力する。
5. The Output Adapter shall 診断 API として以下の最小情報を外部から取得可能な形で公開する：登録済みハンドラ数、現在 Stage の AddressableKey、現在 Light 数、追加済み Volume Override 型数、`StagePreviewHost.IsReady`、最終エラー時刻と内容。
6. The Output Adapter shall `IOutputDiagnostics` への状態提供（フェーズ追加 or 拡張）を行わず、本 spec 独自の診断 API（`StageLightingVolumeOutputAdapterDiagnostics`）として独立させる。

---

### Requirement 10: 本 spec 単体での検証可能性

**Objective:** 開発者として、本アダプタを `stage-lighting-volume-tab` の UI を起動せずに（Fake `IOutputCommandDispatcher` 経由で）EditMode + PlayMode テストできる体験を得たい。そうすれば UI 側 spec の進捗を待たずに開発・検証を回せる。

**Note:** SLOA-20 を具現化する。Wave 3b の並行開発トラックで本 spec の実装を UI 側と独立に進める前提。

#### Acceptance Criteria

1. The Output Adapter shall `IOutputCommandDispatcher` のモック実装（`FakeOutputCommandDispatcher`）を経由して、ハンドラ登録、state/event/request の inject、エラー event publish の検証を EditMode テストで行えるテスト構造を提供する。
2. The Output Adapter shall URP の `Volume` API を経由する Volume Override 反映テスト（`Bloom` / `Tonemapping` / `ColorAdjustments` の代表 3 型）を PlayMode で実行可能にする（SLOA-20）。
3. The Output Adapter shall Stage 切替テスト（実 Addressables ではなく `IInstantiationProvider` モックで GameObject 生成・破棄を制御可能）を提供する。
4. The Output Adapter shall Light 追加・削除・プロパティ編集テストを EditMode 単体で実行可能にする（実 `Light` コンポーネント生成は `LightsRoot` 配下で実施可能）。
5. The Output Adapter shall `StagePreviewHost` の Awake/OnDestroy / `StagePreviewHostLocator` への Register/Unregister 連動テストを PlayMode で実行可能にする。
6. The Output Adapter shall 手動検証用 PlayMode サンプルシーン（`StageLightingVolumeOutputAdapterPlayModeSample.unity`）を提供し、Fake IPC 経由で全機能を確認できる手順を README に整備する。
7. The Output Adapter shall PlayMode 反復 5 回でリークが発生しないことをテスト（`Addressables.PrintDiagnostics` / `Resources.FindObjectsOfTypeAll<Light>` / `StagePreviewHostLocator.Current` 検査）で検証する。
8. The Output Adapter shall `link.xml` の strip 抑止設定が IL2CPP スタンドアロンビルドで正しく機能することを手動検証手順としてドキュメント化する。

---
