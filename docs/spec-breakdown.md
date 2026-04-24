# kiro spec 切り分け計画

`docs/requirements.md` の要件を、kiro の spec-driven development で扱いやすい粒度に
分割した計画である。各 spec は `/kiro:spec-init` の単位に対応する。

- 版数: 1.0
- 作成日: 2026-04-24
- 対象: `.kiro/specs/` 配下に展開する予定の spec 群

---

## 1. 切り分け方針

以下の原則で分割する。

1. **依存方向が単方向になるように切る**: A が B に依存するなら、B を先に完成させられる境界を引く。
2. **1 spec = 1 パッケージ相当**: UPM パッケージにした際、そのまま 1 パッケージ／1 アセンブリになる粒度を目安とする。
3. **後回し要素を隔離**: 並行開発中の RuntimeDisplaySelector 依存部分は独立 spec に切り出し、他を止めないようにする。
4. **UI と描画を分ける**: Display 1（UI）側と Display 2+（メイン出力）側は物理・論理ともに別プロセス扱いのため、spec も分ける。
5. **「基盤」と「機能」を分ける**: IPC や UI シェルなど、複数機能が乗る土台は独立 spec にする。

---

## 2. spec 一覧（依存順）

| # | spec 名 | 層 | 依存 spec | 本フェーズ |
| --- | --- | --- | --- | --- |
| 1 | `core-ipc-foundation` | 基盤 | - | 必須 |
| 2 | `output-renderer-shell` | メイン出力側 | 1 | 必須 |
| 3 | `ui-toolkit-shell` | UI 側 | 1 | 必須 |
| 4 | `character-selection-tab` | UI 機能 | 2, 3 | 必須 |
| 5 | `stage-lighting-volume-tab` | UI 機能 | 2, 3 | 必須 |
| 6 | `camera-switcher-tab` | UI 機能 | 2, 3 | 必須 |
| 7 | `runtime-display-selector-integration` | メイン出力側 | 2 | **後回し** |

依存関係図:

```
                         ┌──────────────────────┐
                         │ 1. core-ipc-         │
                         │    foundation        │
                         └──────────┬───────────┘
                        ┌───────────┴───────────┐
              ┌─────────▼────────┐   ┌──────────▼─────────┐
              │ 2. output-       │   │ 3. ui-toolkit-     │
              │    renderer-     │   │    shell           │
              │    shell         │   └──────────┬─────────┘
              └─┬──┬──┬──┬───────┘              │
                │  │  │  │                      │
                │  │  │  └──┬──────┬──────┬─────┤
                │  │  │     │      │      │     │
                │  │  │  ┌──▼──────▼──┐ ┌─▼─────▼────┐ ┌──▼─────────┐
                │  │  │  │ 4. char-   │ │ 5. stage-  │ │ 6. camera- │
                │  │  │  │    select  │ │    light   │ │    switcher│
                │  │  │  └────────────┘ └────────────┘ └────────────┘
                │  │  │
                │  │  └──► 7. runtime-display-selector-integration（後回し）
```

---

## 3. 各 spec の概要

### spec #1: `core-ipc-foundation`（土台 / 基盤）

**目的**
UI プロセス（Display 1）とメイン出力プロセス（Display 2+）の間を LocalHost 経由で疎通させる共通基盤を提供する。将来の LAN タブレット UI・WebUI 差し替えを見据え、トランスポートとメッセージ層を抽象化する。

**スコープ**
- LocalHost 通信の抽象インタフェース定義（送受信・購読・Request/Response）
- 具体トランスポートの実装 1 本（TCP / WebSocket のいずれかを設計フェーズで確定）
- メッセージスキーマとシリアライゼーション（JSON / MessagePack のいずれかを確定）
- ビルド時・Editor PlayMode 時の両方で動作する接続マネージャ
- 接続断・再接続の基本ハンドリング

**本フェーズでの非目標**
- LAN／WebUI からの接続（インタフェースだけ用意し、実装は将来フェーズ）
- 認証／暗号化

**対応する要件**: §3.2, §3.3, §6.3

---

### spec #2: `output-renderer-shell`（メイン出力側シェル）

**目的**
Display 2 以降に全画面表示されるメイン出力の土台を提供する。IPC 経由のコマンドを受信してシーンに反映するディスパッチャも含む。

**スコープ**
- メイン出力シーンの初期構成（ルート GameObject 階層、デフォルトカメラ、デフォルトライト、URP 設定、空の Global Volume）
- Display 2+ への全画面表示切替（暫定実装。RuntimeDisplaySelector が入るまでは `Display.displays[n].Activate()` ベースで固定挙動）
- IPC 受信 → シーン反映のディスパッチャ（各タブ spec から呼ばれる Command 受け口を整備）
- メイン出力にはエラーダイアログ・デバッグログを一切描画しない描画分離
- スタンドアロン／Editor 両対応

**本フェーズでの非目標**
- RuntimeDisplaySelector との連携（spec #7 に分離）
- キャラクター・ステージ・カメラ個別機能（spec #4-6 に分離）

**対応する要件**: §3.1, §3.3, §6.2

---

### spec #3: `ui-toolkit-shell`（UI シェル）

**目的**
Display 1 に表示する UI Toolkit ベースのメインウィンドウと、3 タブ切替機構、アセット事前ロード基盤を提供する。

**スコープ**
- UI Toolkit のルート UIDocument と 3 タブ切替のタブバー
- 3 タブ分の UIDocument を **起動時に一括ロード** する仕組み
- 表示/非表示切替のみで切り替えるタブ遷移（都度インスタンス化しない）
- 非同期 AssetBundle 読み込み基盤（別スレッド、Completion 通知）
- タブ別 UXML/USS の配置規約とスキン差し替えポイントの設計
- 共通 UI コンポーネント（スライダー、カラーピッカー、番号付きリスト等）の置き場

**本フェーズでの非目標**
- 各タブの機能実装（spec #4-6 に分離）
- UI スキンそのもののデザイン（利用者側アセット）

**対応する要件**: §3.1, §4, §6.1

---

### spec #4: `character-selection-tab`（タブ 1）

**目的**
MoCap アクター（Slot）とアバターの対応付け、および個別設定を行う UI を提供する。

**スコープ**
- RealtimeAvatarController（`com.hidano.realtimeavatarcontroller`）の組み込みと依存設定
- ゲームのキャラクター選択画面風の UI（Slot 一覧 × アバター候補）
- Slot ↔ アバターの割り当て／変更 UI
- アバター個別設定の UI（RealtimeAvatarController が提供する設定項目を露出）
- 選択・設定状態を IPC で `output-renderer-shell` に送信しメイン出力側へ反映
- 選択状態の永続化（ファイル保存／復元、形式は設計フェーズで確定）

**本フェーズでの非目標**
- アバターアセットそのもの（利用者側の責務）
- RealtimeAvatarController 本体の機能追加

**対応する要件**: §4.1, §5.1

---

### spec #5: `stage-lighting-volume-tab`（タブ 2）

**目的**
ステージデータの読み込み、Light の動的管理、Global Volume 設定による「画作り」を行う UI を提供する。引きのカメラ 1 点で全体ルックを確認する。

**スコープ**
- SceneViewStyleCameraController を用いた引きカメラの UI 組み込み
- ステージアセットの読み込み／切替 UI
- Light の動的生成・削除 UI（任意個数）
- 各 Light の角度・色・強さ・Type・Range 等を調整する GUI
- Global Volume の各 Override（Bloom, Tonemapping, ColorAdjustments 等）を編集する GUI
- 設定状態を IPC で `output-renderer-shell` に送信しメイン出力側へ反映
- Light 構成・Volume 設定の永続化（形式は設計フェーズで確定）

**本フェーズでの非目標**
- ステージアセットそのもの（利用者側の責務）
- 複雑なライティングプリセット管理（単純な保存・復元まで）

**対応する要件**: §4.1, §5.2

---

### spec #6: `camera-switcher-tab`（タブ 3）

**目的**
SceneViewStyleCameraController によるカメラ操作 → UniversalCamerawork 規格でのデータ化 → OSC 送信 → メイン出力側での受信・適用、という一連のパイプラインを提供する。差し替え前提の最小機能で構成する。

**スコープ**
- SceneViewStyleCameraController を用いたカメラ操作 UI
- UCAPI Flat Record へのシリアライズ（UCAPI4Unity 経由）
- OSC による LocalHost 送信（アドレスパターン・メッセージ構造を定義）
- メイン出力側（`output-renderer-shell`）での OSC 受信とカメラへの適用
- 簡易的なスイッチャー UI（現在アクティブなカメラの選択・切替）
- カメラごとの Local Volume（カメラ Volume）編集 UI
- カメラ切替と連動した Volume 切替

**本フェーズでの非目標（次フェーズへ）**
- トランジション（ディゾルブ、補間）
- PVW/PGM のマルチカメラ同時管理
- 外部ハードウェアスイッチャー連携
- タイムライン録画・リプレイ

**対応する要件**: §4.1, §5.3

---

### spec #7: `runtime-display-selector-integration`（**後回し**）

**目的**
RuntimeDisplaySelector の開発完了後に、Display 1/2+ の物理ディスプレイ選択を RDS 経由に置き換える。

**スコープ**
- `output-renderer-shell` の暫定ディスプレイ切替ロジックを RDS API に差し替え
- Display 1/Display 2+ 割り当ての UI（必要であれば）
- 運用時のディスプレイ切替操作のワークフロー設計

**依存**
- [RuntimeDisplaySelector](https://github.com/Hidano-Dev/RuntimeDisplaySelector) の API が確定・リリースされること
- 本 spec 着手までは、`output-renderer-shell` 側でインタフェース（Display 切替サービスの抽象）のみ用意しておく

**対応する要件**: §2.2, §3.1, §7, §8.2

---

## 4. 推奨される実行順序

本フェーズで並行・逐次実行する推奨順序。

```
[Wave 1] spec #1 core-ipc-foundation
            │
            ▼
[Wave 2] spec #2 output-renderer-shell   ‖ spec #3 ui-toolkit-shell   （並行可）
            │                                │
            └─────────────┬──────────────────┘
                          ▼
[Wave 3] spec #4 character-selection-tab ‖ spec #5 stage-lighting    ‖ spec #6 camera-switcher   （並行可）
                          │
                          ▼
[後回し] spec #7 runtime-display-selector-integration
```

- **Wave 1**: 基盤を先に固める。ここが甘いと後段全てが揺れる。
- **Wave 2**: 受け皿（メイン出力）と UI 容器を並行で立ち上げる。IPC コントラクトは Wave 1 で合意済みのため、互いをスタブ化しながら独立に進められる。
- **Wave 3**: 3 タブは互いに独立しているため、担当を分けて並行実行可能。
- **後回し**: RDS の開発進捗に合わせて後追い。

---

## 5. 次に取るアクション

各 spec を実際に kiro ワークフローに載せるには、以下のコマンドを spec ごとに順次実行する。

```
/kiro:spec-init "core-ipc-foundation: UI ↔ メイン出力の LocalHost 通信基盤..."
/kiro:spec-requirements core-ipc-foundation
/kiro:spec-design core-ipc-foundation
/kiro:spec-tasks core-ipc-foundation
/kiro:spec-run core-ipc-foundation
```

（以下、spec #2〜#6 を同様に。#7 は RDS 完成後。）

Wave 1 が完了してから Wave 2 を開始するのが安全だが、各 spec の `spec-design` までを先行して並行着手し、`spec-tasks` 以降を依存順に回すと全体の待ち時間を短縮できる。
