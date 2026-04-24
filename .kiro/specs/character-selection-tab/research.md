# Research & Design Decisions — character-selection-tab

---
**Purpose**: `character-selection-tab` spec の設計判断に至る調査ログ・代替案検討・外部依存調査を集約する。`design.md` は本 research の結論に基づくが、`design.md` 単体で自己完結するよう結論は design.md 側にも明示する。
---

## Summary

- **Feature**: `character-selection-tab`
- **Discovery Scope**: Extension（上流 spec `core-ipc-foundation` / `output-renderer-shell` / `ui-toolkit-shell` の確立済み契約に重ねる Wave 2 タブ spec）
- **Key Findings**:
  - RealtimeAvatarController（以下 RAC）の公開 README は GitHub 上で直接確認できず、具体 API シグネチャは未確定。本 spec はこのリスクを「UI 側は RAC に直接依存しない（CS-1）・設定スキーマは IPC 経由で取得（CS-5）」の構造で吸収し、RAC バインディングは `output-renderer-shell` 側の RAC アダプタ spec（本 spec の派生または Wave 3 追加 spec）に委譲する。
  - `ui-toolkit-shell` が公開する `IUiCommandClient` / `IUiSubscriptionClient` / `IAsyncAssetLoader` / `ITabLifecycleHandle` の 4 つの Facade で本タブに必要な能力が揃っており、本タブは独自の IPC/Addressables/タブライフサイクル機構を持たない。
  - `core-ipc-foundation` の envelope・topic・kind・相関 ID 設計を UI 側へそのまま受け流す設計となっており、本タブのトピック命名規約（R-1）を確定することが契約安定化の鍵。
  - Slot 単位 state トピック（`slot/{slotId}/assignment`, `slot/{slotId}/settings/{key}`）と集約 event トピック（`slot/{slotId}/command`）の粒度は `core-ipc-foundation` の coalesce セマンティクス（D-7）と相性が良く、設定項目単位の coalesce が干渉しない。
  - 永続化経路は CS-9（デバウンス即時保存）と CS-10（起動時は通常 state 経路で再送）を採用し、メイン出力側に復元用特別ハンドラを追加しない（YAGNI）。

## Research Log

### Topic 1: RealtimeAvatarController の公開 API 可視性

- **Context**: CS-2 / CS-5 により Slot ライフサイクルと設定メタデータの所有は RAC 側。UI 側は IPC 経由で動的に情報取得する契約。RAC の公開 API シグネチャが `design.md` のトピック命名・payload 設計に影響し得るため、可能な範囲で一次情報を確認する。
- **Sources Consulted**:
  - `https://github.com/Hidano-Dev/RealtimeAvatarController` — リポジトリトップ。README は最小限で、API 一覧・クラス一覧・Slot 抽象・アバタープロバイダ抽象などは記載されていない。
  - `https://raw.githubusercontent.com/Hidano-Dev/RealtimeAvatarController/main/Packages/com.hidano.realtimeavatarcontroller/package.json` — 404 で取得不可（パス不一致または private）。
  - WebSearch（"RealtimeAvatarController com.hidano.realtimeavatarcontroller Unity VTuber slot avatar provider"）— 該当パッケージの二次情報ヒットなし。
- **Findings**:
  - 現時点で公開ドキュメントから RAC の具体 API（`Slot` 型、アバタープロバイダ interface、設定メタデータ getter 名）を抽出できない。
  - requirements.md の CS-2 / CS-5 / R-2 / R-10 は、この不確定性を既に設計上のリスクとして記録済みである。
- **Implications**:
  - 本 spec の `design.md` は RAC の型名・メソッド名を直接参照しない。UI 側が送受信する `payload` の論理スキーマ（Slot 識別子、アバター識別子、設定項目の型・レンジ・既定値）のみを定義する。
  - RAC と IPC payload 間の具象マッピングは、`output-renderer-shell` に紐づく RAC アダプタ（本 spec の out of boundary）が行う。本 spec はそのアダプタが実装すべき IPC コントラクトを一方的に公開する。
  - 設定メタデータのフィールドセット（`name`, `type`, `min`, `max`, `default`, `label`, `unit`, `options`）を仮置きし、RAC v0.1.0 の実メタデータで埋められない項目は `null` 許容で運用する。

### Topic 2: `ui-toolkit-shell` の Facade 契約棚卸し

- **Context**: 本タブが依存する上流 API を洗い出し、独自実装が不要であることを確認する。
- **Sources Consulted**:
  - `.kiro/specs/ui-toolkit-shell/design.md` — `IUiCommandClient` / `IUiSubscriptionClient` / `IAsyncAssetLoader` / `ITabLifecycleHandle` / `IConnectionStatus` / `IDiagnosticsLogger` / 共通 UI コンポーネント（`VsbSlider`, `VsbColorPicker`, `VsbNumberedList`, `VsbToggleGroup`）を確認。
  - `.kiro/specs/ui-toolkit-shell/requirements.md` — UI-3（USS セレクタ命名規約）、UI-4（共通 UI コンポーネント）、UI-5（Command 送信 API）、UI-6（Addressables 非同期ロード）、UI-7（タブ共通 UI 状態は永続化しない）。
- **Findings**:
  - `IUiCommandClient.PublishState<TPayload>` / `PublishEvent<TPayload>` / `RequestAsync<TReq,TRes>` で CS-6（state / event / request 使い分け）を完全にカバー可能。
  - `IUiSubscriptionClient.Subscribe<TPayload>(topic, kind, callback)` はメインスレッド配信（D-3 継承）、返却トークンの `Dispose` で解除。Slot 一覧・アバター候補・反映応答・エラー event を本タブ購読可能。
  - `IAsyncAssetLoader.LoadAsync<T>(key, scopeId, onCompleted)` はスコープ単位解放が可能で、タブ単位の scope（例: `"tab:character"`）に集約すればタブ破棄時の一括 `ReleaseAll(scopeId)` が適用できる。
  - `ITabLifecycleHandle` の `OnActivated` / `OnDeactivated` でタブ切替に合わせた購読制御が可能。`Dispose` で購読リークをバックストップできる。
  - 共通 UI コンポーネント 4 種は動的 UI 生成（CS-5）に十分な素材。Slider 値は `ValueChanged`（連続値）と `Committed`（最終値）の 2 系統が提供される。
- **Implications**:
  - 本 spec は新規 IPC クライアント実装・Addressables 抽象の再発明・タブライフサイクル管理の独自実装を行わない。
  - Slider の `ValueChanged` を `PublishState` に直結し、`Committed` は本 spec では特別扱いしない（`PublishState` の coalesce により最終値到達は保証される）。
  - 診断ログは `IDiagnosticsLogger` を通し、`LogCategory.TabSpec` で発信。メイン出力描画への経路が構造的に存在しないため、Requirement 9 第 7 項（メイン出力非描画）は上流契約で自動担保される。

### Topic 3: `core-ipc-foundation` の配信セマンティクスとトピック設計

- **Context**: CS-7（トピック粒度）の具体命名と、state coalesce が意図通りに働く粒度を確定する。
- **Sources Consulted**:
  - `.kiro/specs/core-ipc-foundation/design.md` — `MainThreadDispatchQueue` の state スロット辞書はトピック単位で上書き、event は FIFO キューを維持。Req 9.1 / 9.2。
  - `.kiro/specs/core-ipc-foundation/requirements.md` — 1 MB 上限、protocolVersion、未知フィールド無視。
- **Findings**:
  - state coalesce は「同一 topic 文字列」をキーに上書きする。`slot/{slotId}/settings/{key}` のように設定項目までトピックに含めると、スライダーごとに独立 coalesce が効き、別項目の中間値を巻き込まない。
  - event FIFO は同一 Slot 上の `reload` / `reset` / `preset-apply` を順序保証で配信でき、順序依存の操作が安全。
  - 1 MB 上限は本タブの通常ペイロードでは問題にならない（設定値・識別子・小さな構造のみ）。設定メタデータ取得 Request も十分収まる見込み。
- **Implications**:
  - 本 spec のトピック命名: `slots/catalog`（一覧 state, last-write-wins で全件）、`slot/{slotId}/assignment`（割当 state）、`slot/{slotId}/settings/{key}`（設定値 state）、`slot/{slotId}/command`（event, payload で操作種別）、`slot/{slotId}/status`（メイン出力側からの反映状態 state）、`avatars/catalog`（アバター候補一覧 state）、`avatars/{avatarKey}/schema`（個別設定スキーマ request/response トピック）、`slot/{slotId}/error`（RAC 由来エラー event）。
  - トピック文字種は `core-ipc-foundation` が ASCII alphanumeric + `/` + `-` + `_` を推奨。ID 部分（`slotId`, `avatarKey`）は呼び出し側が安全化する責務。
  - プリセット CRUD / 切替は「複数 state コマンドの束」として通常経路で送信し、IPC 側に特別ハンドラを追加しない（CS-10 と整合）。

### Topic 4: Slot ↔ アバター割当の UX パターン

- **Context**: Requirement 4 の UX フロー（ゲームのキャラクター選択画面風）の設計判断。
- **Sources Consulted**:
  - ゲーム UI のキャラクター選択画面の一般的パターン（コンソール系格ゲー / MMO のプレイヤー選択）：カード + グリッド、方向入力で候補遷移、決定で確定。
  - Unity UI Toolkit の pointerdrag イベントと `ManipulatorBase` による擬似ドラッグ UX。
- **Findings**:
  - UI Toolkit 標準のドラッグ API は安定している（`PointerDownEvent` / `PointerMoveEvent` / `PointerUpEvent`）。ただし Addressables ロードと併用するとドラッグ中の視覚プレビューが重くなりがち。
  - 「カード選択 → 候補選択 → 確定」のワンストップ型のほうが、ドラッグ実装コストとエラー動線の両面で運用上無難。
- **Implications**:
  - 本 spec では 2 ステップ操作（カード選択 → 候補選択 → 確定）を一次 UX とする。将来のドラッグ対応は拡張で対応可能。
  - 同時進行操作（Requirement 4 第 7 項）は Slot ごとに操作キューを持ち、進行中は該当 Slot のみロック。他 Slot は並行操作可能。

### Topic 5: 永続化フォーマット・配置

- **Context**: CS-8 の保存対象、CS-9 のタイミング、CS-12 のプリセットを踏まえた物理フォーマット。
- **Sources Consulted**:
  - Unity `Application.persistentDataPath`（Windows Standalone: `%USERPROFILE%\AppData\LocalLow\<company>\<product>`, PlayMode: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\<project>`）。
  - `System.Text.Json`（.NET Standard 2.1 内蔵、`core-ipc-foundation` で採用済み）。
  - Unity `ScriptableObject` vs 生 JSON：Editor 編集性 vs 書き換え耐性。
- **Findings**:
  - 配信現場の運用で手動編集が発生するケースはまれで、自動保存できれば十分。JSON は可読性と保守性のバランスが良い。
  - `ScriptableObject` は PlayMode 中の保存 I/O との親和性が悪い（Editor 専有のアセットライフサイクル）。ランタイム保存は生 JSON のほうが素直。
  - 単一ファイルにプリセット配列を持つ形だと、プリセット 1 件の破損で全体が巻き込まれるリスクがある。プリセット 1 ファイルを採用すれば破損は 1 プリセットに限定される。
- **Implications**:
  - `Application.persistentDataPath/character-selection-tab/presets/` にプリセット 1 件 1 ファイル、UTF-8 JSON で保存。アクティブプリセット名は `presets/_active.json` に別ファイルで保持。
  - 破損検出時は当該ファイルを `*.bak.<timestamp>` にリネームし、他ファイルは読込継続。
  - 保存配置は利用者プロジェクトで差し替え可能にするため、`IPresetStorage` 抽象を導入してファイルシステム実装をデフォルト提供とする（Requirement 11 第 3 項のテスト用メモリダブルも同抽象で受ける）。

### Topic 6: Addressables ベースのサムネイル解決

- **Context**: CS-13（ストアドサムネイル）を Addressables から解決する規約。
- **Sources Consulted**:
  - Unity Addressables の key 解決（`Addressables.LoadAssetAsync<Sprite>("key")`）。
  - `ui-toolkit-shell` の `IAsyncAssetLoader.LoadAsync<T>(key, scopeId, onCompleted)`（`T : UnityEngine.Object`）。
- **Findings**:
  - Unity UI Toolkit の `Image` 要素は `Sprite` と `Texture2D` を受け付ける。サムネイルは `Sprite` 型で統一するとアスペクト比制御が容易。
  - Addressables の key は利用者側規約で衝突しやすいため、本 spec では `{avatarKey}.thumbnail` のサフィックス規約を使用する（CS-13 で言及）。
  - サムネイル未解決時のフォールバックアセットは本 spec パッケージ同梱の `DefaultAvatarThumbnail.png` を提供する（R-CS-13-1 の決着）。
- **Implications**:
  - 本 spec は `IAvatarThumbnailResolver` 抽象を導入し、既定実装で `{avatarKey}.thumbnail` を Addressables から解決する。未解決時は同梱 Sprite にフォールバック。
  - テスト時のモック（Requirement 11 第 6 項）は同抽象を差し替える。

### Topic 7: 設定メタデータの動的 UI 生成

- **Context**: CS-5 により UI 側は設定スキーマを取得して共通 UI コンポーネントから動的生成する。具体的な型マッピングを確定する。
- **Sources Consulted**:
  - `ui-toolkit-shell` の共通 UI コンポーネントライブラリ（Slider / ColorPicker / NumberedList / ToggleGroup）。
  - 一般的な設定スキーマ記述（JSON Schema, OpenAPI の `type`/`enum`/`minimum`/`maximum`）。
- **Findings**:
  - 本フェーズで扱う設定項目型は `float`, `int`, `bool`, `color`, `enum`, `vector3`（表情バイアス、ボーンスケール、ブレンドシェイプ重み等）で十分カバーできる想定。文字列編集系（自由入力）は YAGNI。
  - RAC がどこまでメタデータを提供できるかは未確定（Topic 1）。不足した項目（単位・ツールチップ）は `null` で受けて UI 上はデフォルト挙動（単位非表示等）。
- **Implications**:
  - 型マッピング:
    - `float` / `int` → `VsbSlider`（`min`, `max`, `step` 付き）
    - `bool` → `Toggle`（UI Toolkit 標準）
    - `color` → `VsbColorPicker`
    - `enum` → `VsbToggleGroup`（`options` を keys に）
    - `vector3` → 3 つの `VsbSlider` を水平配置（`VectorFieldControl` として内部で組む）
  - 未知の型（例: `curve`）は対応する UI を生成せず、`DiagnosticsLogger.Log(Warning, ...)` で診断し、他項目の UI 生成は継続（Requirement 5 第 11 項の要件）。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| MVP / 明示 Presenter | View（UXML）と Presenter（C#）を分離し、Presenter が Command 発行と購読を担う | テスト性が高い、共通 UI との組合せが自然、Unity UI Toolkit の Binding と相性良好 | クラス数がやや増える | 本 spec の採用 |
| MVVM（UI Toolkit Data Binding） | UXML の `binding-path` 属性で ViewModel プロパティをバインド | UI 側ボイラープレート削減 | Unity 6.3 の Data Binding は未成熟・拡張性が未知、IPC 送受信との結線は結局必要 | 不採用（成熟度不足） |
| View 直結（UXML + 手続型 C# hookup） | View の VisualElement を直接操作、Presenter 層なし | 実装コスト最小 | 状態遷移が UI コードに分散、テスト困難、CS-5 の動的生成の複雑さを受け止めにくい | 不採用 |
| Redux 系単方向データフロー | Store + Action + Reducer で UI 状態を集中管理 | 状態トレース容易 | 本タブ規模には過剰、依存ライブラリ増加 | 不採用（YAGNI） |

## Design Decisions

### Decision: UI アーキテクチャは MVP（View + Presenter）

- **Context**: CS-5（動的 UI 生成）と CS-6（state / event 使い分け）と Requirement 5 第 7 項（state 逆流との競合解消）を含む複数要件が絡み合い、UI 状態を追跡可能かつテスト可能にする必要がある。
- **Alternatives Considered**:
  1. UI Toolkit Data Binding（MVVM）— 成熟度懸念で不採用
  2. View 直結 — テスト困難で不採用
  3. Redux 系 — YAGNI で不採用
- **Selected Approach**: 各 UI 構成単位（プレイヤーカードリスト、アバター候補グリッド、個別設定パネル、プリセット管理バー）に Presenter クラスを置き、View（UXML + `VisualElement` の Query）と `IUiCommandClient` / `IUiSubscriptionClient` / `IAsyncAssetLoader` の橋渡しを Presenter が担う。Presenter 間の共有状態は `ICharacterTabStateStore` が保持し、state 購読・UI 反映・Command 発行はすべて Presenter を通す。
- **Rationale**: 単体テストで Presenter のみをインメモリ `FakeUiCommandClient` / `FakeUiSubscriptionClient` と結線して検証可能。共通 UI コンポーネントは View 層に留まり、Presenter のロジックとは疎結合。
- **Trade-offs**: Presenter / View / State の 3 層を明示する分、シンプルなフック実装に比べてクラス数が増える。一方、Requirement 11 の単体検証可能性を構造的に担保できる。
- **Follow-up**: `ICharacterTabStateStore` のスレッド契約（メインスレッド専有）をコードレビューで担保。

### Decision: 永続化は「プリセット 1 件 1 JSON ファイル + active 記録ファイル」

- **Context**: CS-8（保存対象）、CS-9（デバウンス即時保存）、CS-12（プリセット CRUD）、R-4（フォーマット確定）。
- **Alternatives Considered**:
  1. 単一 JSON に全プリセット配列 — 破損時の巻き添えリスク
  2. SQLite / LiteDB — 依存増、可読性低下
  3. ScriptableObject — PlayMode 保存の不整合
- **Selected Approach**: `Application.persistentDataPath/character-selection-tab/presets/{presetId}.json` に 1 プリセット 1 ファイル、`_active.json` に現アクティブプリセット ID を保持。500ms デバウンスで書込、アプリ終了時フラッシュ。
- **Rationale**: 破損耐性、Git 管理外（ユーザーデータ領域）、`System.Text.Json` で依存追加なし。
- **Trade-offs**: プリセット数が非常に多くなるとファイルシステム負荷が上がるが、配信運用では数件〜十数件程度を想定し十分。
- **Follow-up**: プリセット数上限（R-CS-12-2）は設計上は上限なし、I/O 性能上は 100 件程度が目安であることを運用ドキュメント側で示す。

### Decision: 復元は通常 state 経路で再送（CS-10 確定）

- **Context**: 起動時復元の経路選択。
- **Alternatives Considered**:
  1. メイン出力側に「復元 API」を追加し一括送信 — メイン出力側ハンドラが二系統になる
  2. 起動時に state を本 spec 側の内部状態にだけ反映し、後からメイン出力が追いつくのを待つ — メイン出力との状態ズレが発生
  3. 通常 state 経路で再送（CS-10 確定） — メイン出力側は通常運用と同一ハンドラで処理可能
- **Selected Approach**: IPC 接続確立後、本タブが保存ファイルを読込み、各 Slot 割当と設定値を順次 `PublishState` で送信。イベント種別の操作は CS-12 のプリセット切替と同じで、通常 state の束として扱う。
- **Rationale**: メイン出力側のハンドラ実装を 1 本化、テスト性向上。
- **Trade-offs**: 大量プリセット切替時に一時的に大量の state 送信が発生するが、coalesce により最終値のみ反映される（D-7）。
- **Follow-up**: 復元中の部分失敗（Requirement 8 第 11 項）で失敗 Slot の診断出力が明示されること。

### Decision: 操作中コントロールと state 逆流の競合解消

- **Context**: Requirement 5 第 7 項（R-6）。オペレーターが Slider をドラッグ中にメイン出力側から state 逆流すると、コントロールが跳ねる。
- **Alternatives Considered**:
  1. 逆流を常に反映 — UX 破綻（跳ね）
  2. 逆流を常に無視 — 他クライアント変更が反映されない
  3. 「操作中の項目のみ逆流抑止、操作終了（Committed）後は反映」 — 採用
- **Selected Approach**: 各設定コントロールに `IsInteracting` フラグを持たせ、`PointerDownEvent` で true、`PointerUpEvent` または一定時間（200ms）操作なしで false。`IsInteracting == true` の間は同 topic の state 逆流をバッファリングし、false になった時点で最新値のみ適用。
- **Rationale**: `PublishState` の coalesce 契約と同じ哲学（最終値のみ反映）を UI 受信側にも適用。
- **Trade-offs**: バッファ状態の管理コストは増えるが、設定項目単位のローカル状態で収まるため限定的。
- **Follow-up**: 200ms のアイドル判定値は実運用でチューニング可。

### Decision: RAC アダプタ（メイン出力側）は別 spec で扱い、本 spec は IPC コントラクトのみ公開

- **Context**: Topic 1 の RAC API 可視性の不確実性。
- **Alternatives Considered**:
  1. 本 spec が RAC アダプタまで含む — RAC API 変更で本 spec が不安定化
  2. メイン出力側 RAC アダプタを別 spec で扱う — 採用
- **Selected Approach**: 本 spec は UI 側の責務（タブ UI + IPC 送信 + 永続化）に徹し、メイン出力側で IPC を受信し RAC を操作する層は別 spec（仮称 `rac-main-output-adapter`、本 spec と同じ Wave 2 または Wave 3 で追加）で担う。本 spec は IPC コントラクトを一方的に公開する。
- **Rationale**: CS-1 と本 spec の Boundary Commitments に明確に合致。RAC バージョン追従の影響範囲を限定。
- **Trade-offs**: 全体動作検証は別 spec 完了を待つ必要があるが、Requirement 11 のモック検証で本 spec 単独は完全検証可能。
- **Follow-up**: IPC コントラクト変更が発生した場合は、上記別 spec にも revalidation をかける必要がある（Revalidation Trigger として記録）。

## Risks & Mitigations

- **R-CS-1**: RAC API 変更追従（Topic 1）— IPC コントラクトでメイン出力側 RAC アダプタと切り離し、影響範囲を限定（Decision 5）。
- **R-CS-2**: トピック命名規約の後方互換性（R-1）— `protocolVersion` を上流契約から継承し、トピック命名に version suffix を避ける（機能は kind + payload schema で拡張）。
- **R-CS-3**: 設定メタデータ取得のタイムアウト（R-3）— デフォルト `RequestOptions.Timeout = 5s`（D-8）を踏襲。UI 上は「ロード中」表示で経過を見せる。
- **R-CS-4**: 永続化ファイル破損 — `*.bak.<timestamp>` リネーム + 初回起動フォールバック（Requirement 8 第 7 項）。
- **R-CS-5**: 大量プリセット切替時の state 送信集中 — coalesce により最終値収束が保証される。UI 側では切替中表示で操作抑止。
- **R-CS-6**: 不可用アバター警告の UX — `ui-toolkit-shell` の `NotificationBarController` 方針と協調、プレイヤーカード上の警告バッジは `vsb-player-card__warning` で記述、スキン差し替え可能（UI-3 継承）。
- **R-CS-7**: 同時進行中の UI 応答性 — Slot ごとのロックは当該 Slot の UI 要素にのみ適用、他 Slot の操作・他タブ切替には影響させない。
- **R-CS-8**: サムネイル解決キーの衝突 — 利用者プロジェクトの Addressables key 規約に依存。診断ログでキー衝突を検出可能にする。

## References

- [RealtimeAvatarController GitHub](https://github.com/Hidano-Dev/RealtimeAvatarController) — 採用パッケージ（現時点で詳細 API 未公開）
- [Unity Addressables Documentation](https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/index.html) — 非同期ロードと key 管理
- [Unity UI Toolkit VisualElement](https://docs.unity3d.com/Manual/UIE-VisualElementAndVisualTreeAsset.html) — UI 実装ベース
- [System.Text.Json Documentation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview) — 永続化 JSON シリアライザ
- [.kiro/specs/core-ipc-foundation/design.md](../core-ipc-foundation/design.md) — IPC 配信セマンティクス（D-3, D-7, D-10, D-11）
- [.kiro/specs/ui-toolkit-shell/design.md](../ui-toolkit-shell/design.md) — `IUiCommandClient` / `IUiSubscriptionClient` / `IAsyncAssetLoader` / `ITabLifecycleHandle`
- [.kiro/specs/output-renderer-shell/design.md](../output-renderer-shell/design.md) — ディスパッチャ（`IOutputCommandDispatcher`）、state/event 受信契約
