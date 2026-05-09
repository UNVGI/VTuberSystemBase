# VTuberSystemBase 統合開発計画

`docs/requirements.md` と `docs/spec-breakdown.md` で定義された 6 spec を、現在のリポジトリ状態（基盤 3 パッケージ実装済み・タブ 3 spec 未着手）から **動作する VTuber システム** として結節するための統合開発計画である。

- 版数: 1.0
- 作成日: 2026-05-10
- 前提: `docs/requirements.md` v1.0 / `docs/spec-breakdown.md` v1.0 を読了済み

---

## 1. リポジトリの現在地

### 1.1 同梱パッケージ実装状況（`Packages/`）

| パッケージ | 役割 | 実装 | tasks 進捗 |
| --- | --- | --- | --- |
| `com.hidano.vtuber-system-base.core-ipc-foundation` | UI ↔ メイン出力の WebSocket+JSON 通信基盤、メインスレッド配信 | あり | **33/33 完**（optional 含む） |
| `com.hidano.vtuber-system-base.output-renderer-shell` | メイン出力側のシーン骨格（Roots / 既定カメラ / Light / Global Volume）、`IOutputCommandDispatcher`、`IDisplayRoutingService` 抽象、`IOutputDiagnostics` | あり | **20/21 完**（残 1 は optional の観測性ログ拡張） |
| `com.hidano.vtuber-system-base.ui-toolkit-shell` | Display 1 の UI Toolkit ルート、3 タブ枠（タブの中身は `EmptyTabShell.uxml` で空）、共通 UI（Vsb 系）、`IUiCommandClient` / `IUiSubscriptionClient` / `IAsyncAssetLoader` | あり | **49/49 完** |

> Wave 1〜2 は実装完了。`ui-toolkit-shell` の 3 タブ枠は **空シェル状態**。タブの中身を入れる Wave 3 がここから始まる。

### 1.2 外部依存（`Packages/manifest.json` で取り込み済み）

| 依存パッケージ | 用途 | 連携 spec |
| --- | --- | --- |
| `com.hidano.realtimeavatarcontroller` 0.2.0 | MoCap Slot 管理 / アバター差替 | character-selection（メイン出力側） |
| `com.hidano.scene-view-style-camera-controller` 1.0.1 | Editor Scene ビュー相当のカメラ操作 | stage-lighting / camera-switcher |
| `com.hidano.ucapi4unity` 0.1.0-preview.1 | UCAPI Flat Record 共通フォーマット | camera-switcher |
| `com.hidano.uosc` 1.0.0 | OSC 送受信 | camera-switcher |
| `com.hidano.runtime-display-selector` 0.1.1 | 物理ディスプレイ識別と切替、Klak Spout センダー統合、JSON Persistence | Wave 3e（output-renderer-shell の `IDisplayRoutingService` 差し替え） |
| `com.hidano.ulipsync-asio` / `com.vrmc.*` / `jp.keijiro.klak.spout` | リップシンク / VRM / Spout 出力 | character-selection 経由のメイン出力アダプタ将来拡張 |

### 1.3 spec 進捗状況（`.kiro/specs/`）

| spec | requirements | design | tasks | 実装パッケージ |
| --- | --- | --- | --- | --- |
| core-ipc-foundation | ✅ approved | ✅ approved | ✅ generated / 完 | あり |
| output-renderer-shell | ✅ approved | ✅ approved | ✅ generated / 完 | あり |
| ui-toolkit-shell | ✅ approved | ✅ approved | ✅ generated / 完 | あり |
| character-selection-tab | ✅ approved | ✅ approved | ✅ generated / **未着手** | なし |
| stage-lighting-volume-tab | ✅ approved | ✅ approved | ✅ generated / **未着手** | なし |
| camera-switcher-tab | ✅ approved | ✅ approved | ✅ generated / **未着手** | なし |
| runtime-display-selector-integration | ❌ 未作成（小規模差替えのため Wave 3e に統合） | ❌ | ❌ | RDS 本体は v0.1.1 として manifest 取り込み済み |

未着手タスク総数: **約 100**（character 29 / stage-lighting 44 / camera-switcher 27）。

---

## 2. 重要な発見：メイン出力側アダプタの spec 不在

各タブ spec の design.md を精査した結果、**3 タブ spec はすべて「Display 1 側 UI のみ」の責務に閉じている**ことが確認できた。

| タブ spec | UI 側責務（spec 内） | メイン出力側責務（spec 外） |
| --- | --- | --- |
| character-selection-tab | プレイヤーカード描画、IPC で `slot/{id}/assignment` 等を送信 | `slot/{id}/assignment` 受信 → RAC の Slot.Bind 呼出 |
| stage-lighting-volume-tab | Light/Volume CRUD UI、`light/{id}/{prop}` で送信 | `light/{id}/{prop}` 受信 → Light GameObject 生成・編集、Volume Override 適用、ステージ Prefab Instantiate |
| camera-switcher-tab | プレビュー、UCAPI シリアライズ、OSC 送信、Volume 編集 | OSC 受信 → UCAPI デコード → Camera 適用、Local Volume 適用 |

各 design.md の "Out of Boundary" には **「メイン出力側アダプタは別 spec の責務」** と明記されているが、その「別 spec」が現時点で **存在しない**。

### 2.1 これは何を意味するか

タブ spec を 3 つ完成させても、**UI から送信した IPC コマンドの受け手が存在しないため、メイン出力（Display 2）には何も反映されない**。

`output-renderer-shell` が提供しているのは：
- 空の `StageRoot` / `CharactersRoot` / `LightsRoot` / `CamerasRoot` / `VolumeRoot`
- `IOutputCommandDispatcher.RegisterStateHandler<T>` / `RegisterEventHandler<T>` / `RegisterRequestHandler<TReq,TRes>` の **登録口**

つまり「土台と受け口」だけで、**各タブの IPC topic に対応するハンドラ実装が抜けている**。これが統合の最大の盲点である。

### 2.2 提案：3 spec の追加

| 提案 spec 名 | 責務 | 上流 spec | 利用パッケージ |
| --- | --- | --- | --- |
| `rac-main-output-adapter` | character-selection-tab の IPC を受信し RealtimeAvatarController を駆動 | character-selection-tab / output-renderer-shell | RealtimeAvatarController, UniVRM, uLipSync-ASIO |
| `stage-lighting-volume-output-adapter` | stage-lighting-volume-tab の IPC を受信し Light / Volume / Stage Prefab を実シーンに反映 | stage-lighting-volume-tab / output-renderer-shell | (Unity 標準のみ) |
| `camera-switcher-output-adapter` | OSC 受信 → UCAPI デコード → Camera/Local Volume 適用 | camera-switcher-tab / output-renderer-shell | UCAPI4Unity, uOSC |

これら 3 spec は **タブ spec の `Contracts` asmdef（IPC DTO・topic 定数）をそのまま参照する** ことで、UI 側とメイン出力側の二者間の契約を 1 ソースに保つ。タブ spec の design.md が既にこの分離を前提に書かれているため、Contracts asmdef を切り出す追加実装は軽微で済む。

---

## 3. 統合アーキテクチャ概観

```
┌─────────────────────────────────────────────────────────────┐
│  Unity プロセス (Standalone or Editor PlayMode)              │
│                                                             │
│  ┌──────────────────────┐   WebSocket (127.0.0.1:61874)     │
│  │ Display 1: UI        │   JSON / state / event / req-res  │
│  │ ─ ui-toolkit-shell   │ ◄────────────────────────────────►│
│  │   ├─ Character タブ  │                                   │
│  │   ├─ Stage タブ      │                                   │
│  │   └─ Camera タブ     │                                   │
│  │                      │                                   │
│  │   タブ → IPC 送信     │                                   │
│  └──────────────────────┘                                   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ Display 2+: メイン出力（配信ソース）                │    │
│  │ ─ output-renderer-shell                              │    │
│  │   ├─ StageRoot   ◄── stage-lighting-output-adapter   │    │
│  │   ├─ CharactersRoot ◄── rac-main-output-adapter      │    │
│  │   ├─ LightsRoot  ◄── stage-lighting-output-adapter   │    │
│  │   ├─ CamerasRoot ◄── camera-switcher-output-adapter  │    │
│  │   └─ VolumeRoot  ◄── stage-lighting-output-adapter   │    │
│  └─────────────────────────────────────────────────────┘    │
│             ▲                                               │
│             │ OSC (127.0.0.1, /ucapi/camera/*/flat)         │
│             │ camera-switcher-tab → camera-switcher-output  │
└─────────────────────────────────────────────────────────────┘
```

### 3.1 結節点（IPC トピック / OSC アドレス）一覧

下記が「タブ UI ⇔ メイン出力アダプタ」の契約面となる。タブ spec の `Contracts` asmdef に DTO・topic 定数を集約し、UI 側とメイン出力側の両アダプタが共有する。

#### Character

| topic | kind | 方向 | payload |
| --- | --- | --- | --- |
| `slots/catalog` | state | 出力→UI | `SlotCatalogPayload` |
| `avatars/catalog` | state | 出力→UI | `AvatarCatalogPayload` |
| `slot/{id}/assignment` | state | UI→出力 | `SlotAssignmentPayload` |
| `slot/{id}/status` | state | 出力→UI | `SlotStatusPayload` |
| `slot/{id}/settings/{key}` | state | UI↔出力 | `SlotSettingValuePayload` |
| `slot/{id}/command` | event | UI→出力 | `SlotCommandPayload`（Reset/Reload/PresetApply） |
| `avatars/{key}/schema` | request | UI→出力 | `AvatarSchemaRequestPayload` → `AvatarSettingsSchemaPayload` |
| `slot/{id}/error` | event | 出力→UI | `SlotErrorPayload` |

#### Stage / Lighting / Volume

| topic | kind | 方向 | 目的 |
| --- | --- | --- | --- |
| `stage/catalog` | state | 出力→UI | Addressables ステージカタログ |
| `stage/active` | state | UI→出力 | アクティブステージ切替 |
| `light/{id}/{prop}` | state | UI→出力 | Light の type/transform/color/intensity/range/spotAngle |
| `lights/list` | state | 出力→UI | Light 一覧 |
| `light/command` | event | UI→出力 | add / delete |
| `volume/override/{type}/{param}` | state | UI→出力 | Global Volume の Override |
| `volume/overrides/metadata` | request | UI→出力 | Override スキーマ |
| `preview/{...}` | event/state | 双方向 | プレビュー RenderTexture 制御 |

#### Camera

| topic / OSC | kind | 方向 | 目的 |
| --- | --- | --- | --- |
| `cameras/list` | state | 出力→UI | カメラ一覧 |
| `cameras/active` | state | UI→出力 | 放送中カメラ ID |
| `camera/command` | event | UI→出力 | add / delete / active-set |
| `camera/{id}/metadata/{key}` | state | UI→出力 | カメラメタデータ |
| `camera/{id}/volume/override/{type}/{param}` | state | UI→出力 | Local Volume |
| `camera/{id}/volume/overrides/metadata` | request | UI→出力 | Override スキーマ |
| `camera/preset/*` | event/state | UI↔出力 | プリセット |
| OSC `/ucapi/camera/{id}/flat` | OSC blob | UI→出力 | UCAPI Flat Record（60Hz 目安） |

---

## 4. ロードマップ

### Wave 3a: タブ spec の Contracts 分離と最小実装（並行 3）

タブ spec の design.md は既に `Contracts` asmdef 分離を想定している（特に stage-lighting / camera-switcher は明示）。先に **DTO + topic 定数だけ** を含む `Contracts` asmdef を 3 spec で切り出すと、Wave 3b と並行着手できる。

| 担当 | パッケージ | 開始条件 | 完了条件 |
| --- | --- | --- | --- |
| Char | `jp.hidano.vtuber-system-base.character-selection-tab` Contracts のみ | tasks 1.1〜1.3 | DTO/Topic 定数が UI 側・出力側からビルド参照可能 |
| Stage | `jp.hidano.vtuber-system-base.stage-lighting-volume-tab` Contracts のみ | tasks 1.1〜1.3 | 同上 |
| Cam | `jp.hidano.vtuber-system-base.camera-switcher-tab` Contracts のみ | tasks 1.1〜1.3 | 同上 |

> 各 Contracts asmdef は `core-ipc-foundation.Abstractions` のみに依存し、UI/出力どちらにも参照されない位置（パッケージ内の独立 asmdef）に置く。

### Wave 3b: タブ UI 実装と「メイン出力側アダプタ」spec 起票（並行可）

| トラック | 並行度 | 内容 |
| --- | --- | --- |
| **3b-UI**（タブ UI 完成） | 3 並行（タブ独立） | character / stage-lighting / camera-switcher の残タスクを `/kiro:spec-impl` で消化 |
| **3b-Adapter spec**（出力側 spec 整備） | 3 並行（タブ独立） | `rac-main-output-adapter` / `stage-lighting-volume-output-adapter` / `camera-switcher-output-adapter` の `/kiro:spec-init` → `requirements` → `design` → `tasks` を起票 |

UI 側は `FakeIpc*` でモック検証が成立するため、出力側アダプタが揃わなくても進めて構わない（タブ spec の "本 spec 単独検証" 要件で担保済み）。

### Wave 3c: メイン出力側アダプタの実装（並行 3）

| spec | 主要責務 | 利用 |
| --- | --- | --- |
| `rac-main-output-adapter` | `slot/{id}/assignment` 等を受け、`RealtimeAvatarController` の `Slot.Bind(avatarKey)` 等を呼出 | RAC, UniVRM, uLipSync-ASIO（任意） |
| `stage-lighting-volume-output-adapter` | Light GameObject 生成・編集、`VolumeProfile.AddComponent<T>()`、ステージ Prefab `Addressables.InstantiateAsync` | URP, Addressables |
| `camera-switcher-output-adapter` | uOSC で `/ucapi/camera/*/flat` を受信、UCAPI4Unity でデコード、`CamerasRoot` 配下の `Camera` に適用 | UCAPI4Unity, uOSC |

### Wave 3d: エンドツーエンド統合シーン

`output-renderer-shell` の `MinimalMainOutputScene` と `ui-toolkit-shell` の `UiShellPlayModeSample` を統合した **`Samples~/IntegratedDemo/`** をリポジトリのリファレンスプロジェクト直下（`Assets/Scenes/MainDemo.unity`）に配置：

- `OutputSceneBootstrapper` 1 個（Display 2+）
- `UiShellLifecycleDriver` 起動経路で UI（Display 1）
- 3 タブの UI と 3 アダプタを全て登録した状態でスタンドアロンビルドが配信ソースとして成立すること

### Wave 3e: RuntimeDisplaySelector 連携と Spout 出力経路（Wave 3c と並行可）

RuntimeDisplaySelector v0.1.1 は API が確立済み（Facade `RuntimeDisplaySelector.Current`、`AssignmentOptions` / `RestoreOptions` / `LogCallback`、Win32 ディスプレイ列挙、JSON Persistence、Klak Spout センダー統合まで完備）であり、新規 spec を切るほどの規模ではない。**`output-renderer-shell` 内で `IDisplayRoutingService` のもう 1 実装を追加するだけ** で済む。

| 作業 | 内容 |
| --- | --- |
| `RuntimeDisplaySelectorRoutingService` 実装 | `IDisplayRoutingService.Activate(Camera, DisplayRoutingConfig)` から `RuntimeDisplaySelector.Current.Assign(camera, displayIndex, AssignmentOptions)` を呼び、`DisplayAssignmentInfo` を返す薄いアダプタ |
| `OutputSceneBootstrapper` の差替え | `BuiltInDisplayRoutingService`（暫定実装）から RDS 実装に切替え。設定で両者を選択可能に保つ（フォールバックパス維持） |
| Spout センダー経路 | RDS の `KlakSpoutSenderStore` を介してメイン出力カメラを Spout 名で OBS に直接送出可能にする（`Display 2` への物理出力に加える / 代替する） |
| 永続化 | RDS の `JsonAssignmentStore` を活用し、ディスプレイ割当の永続化を `output-renderer-shell` 側で持たない（重複回避） |
| 検証 | RDS 同梱の `Samples~/RuntimeSwitching` を参考に、`output-renderer-shell` の PlayMode テストに RDS 経路の回帰を追加 |

**Wave 3a〜3c とは資源競合がないため、3 タブ着手と並行して進められる。** 完了すれば配信現場で「Spout で OBS に流す」「実 2 画面で出す」の両方を選べるようになる。

### Wave 4: 将来拡張（本統合計画スコープ外）

| 候補 | 開始トリガ |
| --- | --- |
| 高機能カメラスイッチャー（PVW/PGM、トランジション、外部 HW） | 本フェーズの camera-switcher が運用検証を経る |
| WebUI / LAN タブレット UI | LocalHost 通信のリモート許可・認証・暗号化方針確定 |
| タイムライン録画・リプレイ | 配信運用での要望が定常化したら |

---

## 5. 推奨実行順序（コマンドベース）

```
[Wave 3a] Contracts 切り出し（タブ spec 内の partial 実装）
    ├─ 各タブ tasks 1.1〜1.3 のみ /kiro:spec-impl で先行消化
    └─ 完了後ただちに Wave 3b に分岐

[Wave 3b 並行]
    UI トラック:
        /kiro:spec-impl character-selection-tab
        /kiro:spec-impl stage-lighting-volume-tab
        /kiro:spec-impl camera-switcher-tab
    Adapter spec トラック:
        /kiro:spec-init "rac-main-output-adapter: ..."
        /kiro:spec-init "stage-lighting-volume-output-adapter: ..."
        /kiro:spec-init "camera-switcher-output-adapter: ..."
        （以後 requirements → design → tasks）

[Wave 3c]
    /kiro:spec-impl rac-main-output-adapter
    /kiro:spec-impl stage-lighting-volume-output-adapter
    /kiro:spec-impl camera-switcher-output-adapter

[Wave 3d]
    Assets/Scenes/MainDemo.unity 構築・スタンドアロンビルド検証

[Wave 3e（Wave 3c と並行可）]
    output-renderer-shell に RuntimeDisplaySelectorRoutingService を追加
    BuiltInDisplayRoutingService からの差替え
    Klak Spout 経由の OBS 送出経路を整備
```

> グローバル CLAUDE.md の指示により、`spec-impl` フェーズではバッチ実行 `/kiro:spec-run` を優先する。

---

## 6. 統合検証戦略

### 6.1 段階的検証ピラミッド

| 階層 | 検証範囲 | 既存資産 |
| --- | --- | --- |
| L1: 単体 | 各クラスのロジック | tasks に組み込み済み（NUnit / Unity Test Framework） |
| L2: 自己ループ | 同一プロセスで `InMemoryLoopbackTransport` 経由の往復 | `core-ipc-foundation` Samples~/MinimalLoopback |
| L3: タブ単独 | 各タブを Fake IPC で動作させる PlayMode | 各タブ tasks 11.x / 12.x が用意 |
| L4: シェル単独 | `ui-toolkit-shell` の `UiShellPlayModeSample` | 既に整備済み（タブ未実装でも空枠で起動） |
| L5: 出力単独 | `MinimalMainOutputScene` を IPC クライアント不在で起動 | 既に整備済み |
| L6: タブ+アダプタ | WebSocket で UI と出力を結節（同一プロセス内） | **新設**：Wave 3d の `IntegratedDemo` |
| L7: スタンドアロン配信 | 実 2 画面でビルドし、OBS で取り込み | **新設**：手動受け入れテスト |

### 6.2 結節点の契約検証

- タブ spec の `Contracts` asmdef を **テストプロジェクト経由で UI 側・出力側の両方からビルド可能** であることを CI で担保。
- IPC topic 命名・payload スキーマは spec の `_Boundary:_` マーカーが付いたテストで両側から read しか書きこまない構造にする。
- OSC（camera-switcher）は uOSC の `OscServer` を Editor テストでスタブし、UCAPI Flat Record の round-trip を 1000 件 / 60Hz で検証。

### 6.3 配信適合性（Display 2 への UI 漏れ厳禁）

- `output-renderer-shell` の `MainOutputNoOverlayTests` を CI 必須にし、メイン出力側に `OnGUI` / `IMGUI` / `UIDocument` が混入しないことを構造的に検証する（既に実装済み）。
- 将来追加するアダプタ実装も同テストの対象範囲に入れる（PR テンプレートで言及）。

---

## 7. リスクとオープンイシュー

### 7.1 確定済み（実装済み）

- IPC トランスポート: WebSocket (RFC 6455) + JSON / `127.0.0.1:61874`
- メッセージスキーマ: 1 MB 上限 / `protocolVersion` メジャー一致 / 未知フィールド許容
- メインスレッド配信: PlayerLoop の `PreUpdate` に `IpcDispatchStep` を挿入

### 7.2 未確定（次に決める必要）

| 項目 | 影響範囲 | 推奨タイミング |
| --- | --- | --- |
| OSC アドレス・ポート最終確定 | camera-switcher / output-adapter | camera-switcher tab の `/kiro:spec-impl` 直前 |
| Light / Volume / カメラ プリセットの JSON スキーマ整合 | 3 タブ + 3 アダプタ | Wave 3b の design レビュー時 |
| Addressables の Group 構成（アバター / ステージ / サムネイル） | character / stage の両 spec | 利用者プロジェクト側との合意が必要 |
| メイン出力カメラへの URP `targetDisplay` 振り分け | output-renderer-shell ↔ camera-switcher-output-adapter | Wave 3c 着手時（RDS 採用時は RDS 側に委譲） |
| RDS の Spout 経路と物理ディスプレイ経路の選択方針 | 運用形態（OBS Spout 取込 vs 実 2 画面） | Wave 3e 着手時 |
| RDS Persistence と各タブ プリセットの責務分離 | RDS の `JsonAssignmentStore` と各タブ プリセットが永続化レイヤで衝突しないか | Wave 3e 着手時 |

### 7.3 観測されるリスク

- **依存パッケージのバージョンずれ**：RAC 0.2.0 / UCAPI4Unity 0.1.0-preview.1 等の preview 版を採用しているため、本フェーズで API 変更を被る可能性。`manifest.json` で固定し、各 spec の design.md で参照点を明示してある（変更時は再検証トリガ）。
- **meta GUID の使い回し**：タブ spec のパッケージ生成時に `.meta` の GUID を必ず `[guid]::NewGuid().ToString('N')` で都度生成（連続パターン禁止、グローバル CLAUDE.md ルール）。
- **`System.Text.Json` の二重参照**：core-ipc-foundation で同梱中。タブ spec 側で再同梱しないこと（`HANDOVER.md` の方針：明示参照固定 / `Microsoft.Bcl.AsyncInterfaces` は参照禁止）。

---

## 8. 完了判定

本統合計画の完了は次の 5 条件を満たすことで判定する。

1. Wave 3b〜3c の 6 spec すべてが `tasks` の必須項目を緑にしている。
2. Wave 3e の RuntimeDisplaySelector 連携が `output-renderer-shell` に組み込まれ、Spout 経路と物理ディスプレイ経路の双方で出力できる。
3. `Assets/Scenes/MainDemo.unity` を PlayMode で起動するだけで、Display 1 に 3 タブ UI、Display 2（または Spout）にキャラ + ステージ + ライト + カメラの映像が出る。
4. スタンドアロンビルドを (a) 実 2 画面 + OBS Display Capture、または (b) Spout + OBS Spout Source で実行し配信に載せられる（手動受け入れ）。
5. `core-ipc-foundation` / `output-renderer-shell` / `ui-toolkit-shell` / 3 タブ / 3 アダプタの 9 パッケージが UPM パッケージとして他プロジェクトに `manifest.json` 経由で取り込めること。

> Wave 4（PVW/PGM、WebUI、タイムライン）は本統合計画のスコープ外。完了後に独立 spec として進める。

---

## 9. 次のアクション

直近 1 セッションで取りうる手は次のいずれか。

- **A**：3 タブ spec の `Contracts` asmdef 切り出しを並行で先行（Wave 3a を実行）。
- **B**：`rac-main-output-adapter` / `stage-lighting-volume-output-adapter` / `camera-switcher-output-adapter` 3 spec の `/kiro:spec-init` を起票。
- **C**：UI 側だけ先に動くものを見たいなら、`character-selection-tab` を `/kiro:spec-run` でバッチ実装（モック注入で完結する）。
- **D**：`output-renderer-shell` に `RuntimeDisplaySelectorRoutingService` を追加（Wave 3e、他トラックと完全独立で進められる）。

「今晩で一気に片付ける」前提なら、A → B → C を並行 3 トラックで回しつつ、独立トラックとして D を走らせるのが最大スループット。Wave 3c のメイン出力アダプタ実装は B が完了次第着手可能。
