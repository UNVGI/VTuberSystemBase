# Research & Design Decisions — stage-lighting-volume-tab

## Summary

- **Feature**: `stage-lighting-volume-tab`
- **Discovery Scope**: New Feature（Wave 2 タブ spec、上流 3 spec に依存する新規 UI タブ）
- **Key Findings**:
  - URP の `VolumeManager.baseComponentTypeArray` と `VolumeParameter` の `ParameterAttribute` / 派生型を反射して **メタデータ駆動で動的 UI 生成** が可能。静的 UI 固定実装を避け、利用者プロジェクトの独自 `VolumeComponent` も同じ経路で露出できる（SL-7 の具現化）。
  - 引きカメラプレビューは **`Camera.targetTexture = RenderTexture` → UI Toolkit 側 `VisualElement.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt))`** の経路で表示する。PanelSettings の `targetDisplay = 0` による Display 1 閉じ込めは `ui-toolkit-shell` が既に構造的保証しており、プレビュー RenderTexture は本タブ UIDocument 配下に置くだけで Display 1 に閉じる。
  - Light GameObject とステージ Prefab Instantiate の所有権は **メイン出力側**（`output-renderer-shell` の下流アダプタ）にあり、本 spec は **UI と IPC 契約のみ** を所有する。メイン出力側アダプタの実装は本 spec の範囲外だが、IPC 契約（トピック命名規則、payload DTO、採番ポリシー）はこの spec で確定する。
  - 永続化は **JSON ファイル + Addressables key 文字列保存 + lightId は再採番前提で保存しない**。`character-selection-tab` の CS-8〜CS-12 と整合する `Application.persistentDataPath` 配下の単一ファイル構造を踏襲。

## Research Log

### URP Volume Override メタデータ API（SL-7、R-2 解消）

- **Context**: Requirement 6（Global Volume Override 編集）および SL-7 は「Override 種別をリストとして管理、各 param を `VolumeParameter<T>` で、メタデータ駆動で動的 UI 生成」を要求する。URP の公開 API がどこまでメタデータを露出するか・カスタム `VolumeComponent` をどう扱うかを確定する必要がある。
- **Sources Consulted**:
  - [Class VolumeManager | Scriptable Render Pipeline Core 17.x](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.1/api/UnityEngine.Rendering.VolumeManager.html)
  - [Class VolumeParameter<T> | Scriptable Render Pipeline Core 17.x](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.1/api/UnityEngine.Rendering.VolumeParameter-1.html)
  - [Unity - Manual: Volume component reference for URP](https://docs.unity3d.com/6000.2/Documentation/Manual/urp/volume-component-reference.html)
  - [Pump Up The Volume: Writing custom Volume Components in Unity](https://tryfinally.dev/unity-custom-srp-volume-components)
  - [URP / Volume.cs - how to access the override settings at runtime via script?](https://discussions.unity.com/t/urp-volume-cs-how-to-access-the-override-settings-at-runtime-via-script/773268)
- **Findings**:
  - `UnityEngine.Rendering.VolumeManager.instance.baseComponentTypeArray` で、現プロジェクトで登録されている全 `VolumeComponent` サブクラスの `Type[]` を取得可能。URP 標準（`Bloom`, `Tonemapping`, `ColorAdjustments`, `Vignette` 等）と利用者独自の `VolumeComponent` が同列に列挙される。
  - 各 `VolumeComponent` のパラメータは **`public` フィールド** として宣言され、型は `VolumeParameter<T>` の派生（`FloatParameter`, `ClampedFloatParameter`, `ColorParameter`, `BoolParameter`, `NoInterpEnumParameter<T>` など）。`System.Reflection.FieldInfo.GetFields(BindingFlags.Public | Instance)` で走査できる。
  - `VolumeParameter` 基底クラスには `overrideState`（bool、param ごとの有効化）プロパティが存在し、URP の "override checkbox" はこの値に対応する。SL-7 の「Override 種別全体の enabled」と「param ごとの overrideState」の 2 レベル制御を区別する必要がある。
  - 値のレンジは `MinFloatParameterAttribute` / `MaxFloatParameterAttribute` などの `ParameterAttribute` サブクラス（Unity URP 内部で定義）を `FieldInfo.GetCustomAttributes()` で取得できる。`ClampedFloatParameter` のようなレンジ内蔵型は `min` / `max` プロパティを直接露出する。
  - 受信側（メイン出力側アダプタ）でメタデータを構築して UI 側に JSON で返す Request/Response 経路が実装負荷・UI 側非依存性の観点で最適。UI 側は URP に依存しない（DTO のみ）。
- **Implications**:
  - メイン出力側アダプタで **メタデータビルダー** を 1 箇所作成し、`baseComponentTypeArray` → 各型の `FieldInfo[]` → `VolumeOverrideSchemaDto` への変換を集約する。カスタム型も自動的に含まれる。
  - UI 側は `VolumeOverrideSchemaDto` のみ受信し、`ParamKind`（Float / ClampedFloat / Int / Bool / Color / Enum / Vector2 / Vector3 / Vector4 / Texture）ごとに共通 UI コンポーネントライブラリのコントロールを動的マップする。
  - 未知の `ParamKind`（新しい `VolumeParameter` 派生型）は UI 側でスキップしログ出力（Requirement 6.10）。
  - `overrideState`（param 単位）は本 spec では **Override 全体の enabled に単純化** する。具体的には UI 上は「Override 自体の enabled トグル」1 個 + 各 param の値のみを編集する。メイン出力側アダプタは Override の enabled=true 時に対応する `VolumeComponent` を `VolumeProfile.components` に追加し、各 param の `overrideState = true` + 値を設定する。

### 引きカメラプレビュー（RenderTexture → VisualElement）（SL-1, SL-13, R-9 解消）

- **Context**: Requirement 2（引きカメラ UI 埋め込み）および SL-1, SL-13 は「別カメラで RenderTexture に描画 → タブ UIDocument 内パネルに表示 → メイン出力シーンのステージ・Light・キャラクターを映す」を要求。Unity 6.3 URP + UI Toolkit での実装パターンを確定する必要がある。
- **Sources Consulted**:
  - [Can I create PanelSettings and RenderTexture at runtime for using with UIToolkit?](https://discussions.unity.com/t/can-i-create-panelsettings-and-rendertexture-at-runtime-for-using-with-uitoolkit/814433)
  - [RenderTexture in Visualelement update every Frame](https://discussions.unity.com/t/rendertexture-in-visualelement-update-every-frame/928429)
  - [Unity - Manual: Image (UXML element)](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-uxml-element-Image.html)
  - [SceneViewStyleCameraController（GitHub Hidano-Dev）](https://github.com/Hidano-Dev/SceneViewStyleCameraController) — README 読取不可のためソース確認前提
- **Findings**:
  - `new RenderTexture(width, height, depth, GraphicsFormat.R8G8B8A8_SRGB)` で作成した RT を `Camera.targetTexture` に設定すると、そのカメラの描画は画面ではなく RT に転送される。UI Toolkit 側では `VisualElement.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt))` で表示可能。
  - 更新頻度: プレビュー VisualElement に RenderTexture を背景画像として設定した場合、エディタのインスペクタでは毎フレーム更新されないケースが報告されている。プロダクション PlayMode では問題ないが、UI 側が明示的に `MarkDirtyRepaint()` を呼ぶ防御策が推奨。
  - SceneViewStyleCameraController（Hidano-Dev 製、本プロジェクトで採用）は Unity Editor の Scene ビューと同等のマウス操作（右ドラッグで旋回、中ドラッグでパン、ホイールでズーム）を任意の `Camera` に付与するコンポーネント。README 読取不可のため、実装時にコードを直接確認して public API を同定する前提とする。最悪の場合でも「`MonoBehaviour.Update` でマウス入力 → Camera.Transform 更新」のシンプルな構造は逸脱しないため、`Camera` に `AddComponent<SceneViewStyleCameraController>()` で十分と想定。
  - メイン出力シーンを 2 視点（本番カメラ + プレビューカメラ）でレンダリングする場合、**プレビューカメラはメイン出力シーン内の別 GameObject として配置** し、カリングマスクとポスプロ設定をメイン出力カメラと揃える。カメラ所有権は本 spec 側（UI 側 asmdef）にある GameObject だが、**メイン出力シーン内に配置する必要がある** ため、プレビューカメラの Instantiate はメイン出力側アダプタが `PublishEvent(preview/command, {op: init})` で受けて実行する経路とする。
- **Implications**:
  - プレビューカメラは UI 側シーンではなくメイン出力側シーンの `CamerasRoot` 配下に配置される GameObject。本 spec の UI 側コードは **プレビューカメラの直接参照を持たず、`RenderTexture` だけを握る**（`RenderTexture.GetTemporary` 相当をメイン出力側で作成、RT の識別子＋ネイティブハンドルを UI 側に渡す経路は不可能なので、実装レベルでは別途 Singleton 経由での RT 参照解決が必要）。
  - **設計上の妥協**: Unity の制約上、`RenderTexture` は同一プロセス内で直接参照可能。本 spec は「UI プロセス = メイン出力プロセス = 同一 Unity プロセス」（D-1）のため、プレビューカメラ + RT を保持する `StagePreviewHost` をメイン出力側に配置し、UI 側から `StagePreviewHostLocator.GetPreviewRenderTexture()` で同期取得する経路を取る。IPC 契約としては `preview/command`（init / dispose / reset-view）と `preview/state`（active / disposed）のみ定義。
  - `ITabLifecycleHandle.OnDeactivated` で **プレビューカメラの `enabled = false`**（非描画）、`OnActivated` で `enabled = true` に戻す。これは `PublishEvent(preview/command, {op: set-enabled, value: false/true})` 経由で実現。
  - GPU 負荷軽減策: プレビュー RT の解像度は既定 640×360（16:9 の 1/6 縮小）、リフレッシュレートは `Camera.nearClipPlane` / `farClipPlane` は本番カメラと同一、`allowDynamicResolution = false` とする。利用者プロジェクトが調整できる設定値として SO に露出する。

### Light GameObject 所有権と IPC 契約（SL-2, SL-3 の具現化）

- **Context**: Light GameObject の所有権はメイン出力側（SL-2）、lightId もメイン出力側採番（SL-3）。本 spec は UI 側の契約のみを定義するが、契約のための DTO とトピック設計は明確化が必要。
- **Sources Consulted**:
  - `core-ipc-foundation/design.md`（`MessageEnvelope`, `MessageKind`, Topic 命名規則）
  - `ui-toolkit-shell/design.md`（`IUiCommandClient`, `IUiSubscriptionClient`, `MessageEnvelope<TPayload>`）
  - `output-renderer-shell/design.md`（`IOutputCommandDispatcher`, topic × kind ディスパッチ）
- **Findings**:
  - `core-ipc-foundation` は topic 文字列と payload 型の対応を型システムで enforcement しない（JSON の `JsonElement` ベース）。UI 側と下流アダプタの両方で **同一 DTO クラス（または record struct）を共有** し、`IUiCommandClient.PublishState<TPayload>(topic, payload)` でシリアライズ型を明示する形が実装負荷が最も低い。
  - DTO は「UI 側 asmdef（本 spec）と、メイン出力側アダプタ asmdef（本 spec 範囲外の別実装）」の **両方から参照される** 必要がある。これは `core-ipc-foundation` の Abstractions asmdef には属さない（本 spec 固有のドメイン型）ため、**本 spec 内で `Abstractions`（IPC DTO）と `Runtime`（UI ロジック）の 2 asmdef を分離**し、メイン出力側アダプタが Abstractions のみ参照する設計とする。これは `core-ipc-foundation` の Abstractions / Core 2 分割と同じ方針。
  - lightId の採番は **GUID string**（メイン出力側で採番、`Guid.NewGuid().ToString("N")`）。タブ一覧 state の payload に含めて UI 側に通知する。
- **Implications**:
  - 本 spec 内部構成: `VTuberSystemBase.StageLightingVolumeTab.Contracts.asmdef`（DTO + topic 定数）と `VTuberSystemBase.StageLightingVolumeTab.Runtime.asmdef`（UI 実装）の 2 分割。
  - メイン出力側アダプタは `VTuberSystemBase.StageLightingVolumeTab.Contracts.asmdef` + `VTuberSystemBase.OutputRendererShell.Runtime.asmdef` + URP/Addressables を参照して実装する別 spec（本 spec の範囲外）。
  - 本 spec の `Contracts.asmdef` は後続実装者（タブ UI 実装者・メイン出力側アダプタ実装者）の統合ポイントとして機能する。

### プリセット永続化フォーマット（SL-8, SL-9, Requirement 8、R-6 解消）

- **Context**: 「ステージ ID + Light 構成 + Volume Override 構成」を 1 単位とするプリセット CRUD + 永続化（デバウンス即時保存 + 起動時復元）。保存ファイル形式・配置・バージョニングを確定する必要がある。
- **Sources Consulted**:
  - `character-selection-tab/requirements.md`（CS-8〜CS-12 のプリセット方針を継承）
  - Unity `Application.persistentDataPath` と `System.Text.Json`（`core-ipc-foundation` で採用済み）
- **Findings**:
  - `Application.persistentDataPath` は Windows で `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\` を指す。スタンドアロンビルドと Editor PlayMode の両方で同一パスを返すため、D-9 の両対応要件と整合。
  - JSON + 明示的 `schemaVersion` フィールドによるバージョニングが、`System.Text.Json`（`core-ipc-foundation` で既に採用）と再利用可能なシリアライザを使える観点で最も工数が低い。
  - 保存対象（SL-8）:
    - プリセット配列: 各プリセットの `name`（一意）、`stageKey`（Addressables key、nullable）、`lights`（配列、各要素は `type`, `rotation`, `color`, `intensity`, `range`, `spotAngle`, `displayName` など）、`volumeOverrides`（配列、各要素は `typeFullName`, `enabled`, `params`（Dictionary<string, object>））
    - アクティブプリセット名（string）
  - 除外対象（SL-10）: 引きカメラ視点、タブ共通 UI 状態、lightId（再採番のため）
- **Implications**:
  - 保存ファイル: `{Application.persistentDataPath}/vtuber-system-base/stage-lighting-volume-tab.json`（パスは `IPresetStorage` 抽象で差し替え可能、Requirement 8.10）。
  - 破損時は同ディレクトリに `stage-lighting-volume-tab.json.corrupted-{timestamp}` としてリネーム、初回起動扱いにフォールバック（Requirement 8.7、CS-11 同思想）。
  - デバウンス値は **500 ms**（CS-9 の想定値を踏襲、R-7）。PlayMode 停止・`Application.quitting` でフラッシュ（Requirement 8.4）。

### プリセット切替セマンティクス（SL-8, Requirement 7.7、R-4 解消）

- **Context**: プリセット切替時に「一括 remove → add」か「差分適用」かを確定する必要がある（SL-8）。配信中の視覚的影響を最小化する戦略が必要。
- **Sources Consulted**:
  - `core-ipc-foundation/design.md`（`event` FIFO 配信、`state` coalesce）
  - `ui-toolkit-shell/design.md`（`IUiCommandClient.PublishEvent` / `PublishState`）
- **Findings**:
  - **一括 remove → add** は実装が単純（差分計算不要）だが、切替中に「全 Light が一瞬消えて真っ暗」な中間状態が発生する可能性がある。配信中に切替する場合は致命的。
  - **差分適用** は「既存 Light との差分計算」「同一 `displayName` か `type` マッチで同定」等の複雑性を生む。UI 実装コスト高。また lightId が再採番されるため、保存プリセット側には lightId が存在せず、同定ルールをどうしても必要とする。
  - 妥協案: **「ステージ切替は 1 回の event、Volume Override の切替は state 更新、Light は一括 remove → add だが UI 側でドラフト完了後 FIFO で一気に送る」** 方式。FIFO で 1 フレーム内に処理されれば中間「真っ暗」フレームは発生しない（メイン出力は event の FIFO 配信を同期的に処理する、`core-ipc-foundation` D-7）。
- **Implications**:
  - 切替手順（SL-8 切替時送信順序）:
    1. `volume/override/{type}/enabled` の state で不要 Override を false（coalesce）
    2. `stage/command` event で `op: load, key: newStageKey`（または `op: unload`）
    3. 現存 Light 全部に対して `light/command` event で `op: remove, lightId: ...`（FIFO）
    4. 新 Light 全部に対して `light/command` event で `op: add, initial: {...}`（FIFO、採番後続）
    5. 新 Light の lightId 返却後、プロパティ state を一気に送信（coalesce）
    6. `volume/override/{type}/enabled = true` と各 param の state を送信（coalesce）
  - 切替中は UI 上に「切替中」オーバーレイを表示し、オペレーターの追加操作を抑止する（Requirement 7.6 の部分適用警告とセット）。
  - 部分失敗（Requirement 7.6）は **失敗した個別コマンドだけ UI に警告表示、他は継続**（本 spec は rollback しない）。

### SceneViewStyleCameraController 採用検証（R-14 解消）

- **Context**: Unity 6.3 互換性、ライセンス、バージョン固定方針。
- **Sources Consulted**:
  - [SceneViewStyleCameraController（Hidano-Dev）](https://github.com/Hidano-Dev/SceneViewStyleCameraController)
- **Findings**:
  - 本プロジェクトの作者（Hidano-Dev）による公開パッケージ。ライセンス確認・バージョン固定は実装フェーズで直接リポジトリを確認する前提。
  - 本 spec の設計レベルでは **`IPreviewCameraAdapter` 抽象** を定義し、SceneViewStyleCameraController を具体実装の一つとして扱う。テスト時はモック実装に差し替え可能（Requirement 12.7）。
- **Implications**:
  - 本 spec 内部に `IPreviewCameraAdapter`（プレビューカメラへの rotate / pan / zoom / reset-view API の抽象）を定義する。
  - 本 spec は SceneViewStyleCameraController の公開 API 詳細には依存せず、`ISceneViewStyleCameraControllerAdapter` 経由でラップして利用する。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| **MVVM-Lite（選定）** | View（UIDocument / VisualElement）、ViewModel（`StageLightingVolumeTabViewModel`、購読状態の保持 + Command 送信）、Model（IPC 経由のサーバ状態） | UI Toolkit の data binding と相性が良い、UI 側のテスト可能性が高い、プリセット CRUD などの UI 状態が ViewModel 層で集約される | ViewModel / Model 境界の徹底が必要、過剰分割リスク | 本 spec は UI 側ロジックが中心のため、View と業務ロジックの分離が自然 |
| Redux / Flux | 単一 Store + Action + Reducer | 大規模 UI で一貫性を得やすい | 本 spec のスコープには過剰、ボイラープレート増 | 不採用 |
| 直接バインド（ViewModel なし） | VisualElement コードから直接 `IUiCommandClient` を呼ぶ | 軽量 | UI / ビジネスロジックが混ざり、テスト可能性が低下、プリセット管理が散在 | 不採用 |

## Design Decisions

### Decision: 本 spec 内部で Contracts / Runtime の 2 asmdef 分離

- **Context**: Light / Stage / Volume の DTO と topic 定数は UI 側とメイン出力側アダプタの両方で共有される必要がある。
- **Alternatives Considered**:
  1. 単一 asmdef — UI 側とメイン出力側アダプタが同一 asmdef に依存し、循環依存・疎結合の両方が破綻
  2. `core-ipc-foundation.Abstractions` に追加 — 本 spec 固有のドメイン型を基盤 asmdef に混入させる、基盤の独立性を損なう
  3. **Contracts / Runtime の 2 asmdef に分離（選定）** — DTO と定数を Contracts に、UI 実装を Runtime に配置
- **Selected Approach**: `VTuberSystemBase.StageLightingVolumeTab.Contracts`（DTO + topic 定数 + payload record struct）と `VTuberSystemBase.StageLightingVolumeTab.Runtime`（UI 実装、VisualElement、ViewModel）に分離。メイン出力側アダプタ（本 spec 範囲外）は Contracts のみ参照。
- **Rationale**: `core-ipc-foundation` が Abstractions / Core に分離しているパターン（Hexagonal）を踏襲。2 つの下流が型定義を共有できる自然な配置。
- **Trade-offs**: asmdef 数が +1 増えるが、参照方向が一方向で明確になる。
- **Follow-up**: 実装時、DTO のフィールド追加・削除は両下流の同時更新を要する（Revalidation Trigger として明記）。

### Decision: プレビューカメラの所有権はメイン出力側、UI 側は RenderTexture だけ参照

- **Context**: プレビューカメラはメイン出力シーンの GameObject を描画する必要があり、かつ UI Toolkit で RenderTexture を表示する必要がある。
- **Alternatives Considered**:
  1. UI 側がプレビューカメラ GameObject を所有、メイン出力シーンを LoadSceneMode.Additive で読み込む — メイン出力シーン独立性を破壊
  2. UI 側と同一シーンにメイン出力オブジェクトを配置 — `output-renderer-shell` の責務境界を破壊
  3. **プレビューカメラ GameObject はメイン出力側に配置、UI 側は RenderTexture 参照のみ（選定）** — メイン出力シーンを 2 カメラでレンダリングし、プレビュー RT を UI 側 VisualElement に表示
- **Selected Approach**: `StagePreviewHost` MonoBehaviour をメイン出力側アダプタ（本 spec 範囲外）が `CamerasRoot` 配下に配置する。本 spec は `IPreviewRenderTextureAccessor` 抽象を定義し、UI 側はこの抽象経由で現在の RT を取得して VisualElement に貼る。
- **Rationale**: D-1（単一 Unity プロセス）の前提により、同一プロセス内での Singleton ベース参照解決が可能。IPC 経由で RT 参照を送ることはできない（ネイティブハンドルはシリアライズ不能）が、同プロセスであれば `StagePreviewHostLocator` 経由の同期参照で足りる。
- **Trade-offs**: 本 spec の Runtime asmdef が `StagePreviewHostLocator`（同一プロセス内の Singleton アクセサ）に依存するため、プロセス分離型のアーキテクチャには移行できない。将来 LAN タブレット UI がプレビューを持つ場合は別経路（WebRTC 等）が必要。
- **Follow-up**: `StagePreviewHostLocator` は本 spec Contracts asmdef に置き、具体の Host 実装はメイン出力側アダプタが提供。

### Decision: Volume Override メタデータは Request/Response で 1 回だけ取得

- **Context**: URP の `VolumeComponent` 型は通常 Unity の起動時点で固定されており、実行中に動的追加されることは稀。
- **Alternatives Considered**:
  1. state トピックで常時購読 — 変更頻度が低いのに常時メモリ消費
  2. **Request/Response で初回 1 回取得（選定）** — タブ初期化時に 1 回取得、以降はキャッシュ
  3. Addressables ベースの型リスト — 型とメタデータを分離管理、過剰複雑
- **Selected Approach**: タブアクティブ化の初期化フロー内で `RequestAsync<VolumeOverrideSchemaRequest, VolumeOverrideSchemaResponse>("volume/override/schema", ...)` を 1 回実行。レスポンスは `VolumeOverrideSchemaDto`（Override 型一覧 + 各型の param 一覧 + 各 param の kind/range/default）。キャッシュは ViewModel に保持。
- **Rationale**: Requirement 6.1 の「Request でメタデータ取得」を素直に実装。メイン出力側は `VolumeManager.baseComponentTypeArray` + Reflection でメタデータを構築。
- **Trade-offs**: 実行中に利用者プロジェクトが `VolumeComponent` 派生型を動的追加した場合、UI が追従しない。→ 再取得 API（UI 側の再試行ボタン、Requirement 6.9）で対応。
- **Follow-up**: メタデータスキーマのバージョニング（`schemaVersion` フィールド）を仕込むことで、将来 URP の `VolumeParameter` 派生が増えた場合の前方互換を確保。

### Decision: Light / Volume Override の state トピックはプロパティ単位細分化

- **Context**: SL-6 の決定により、トピック粒度はプロパティ単位。具体的な命名規則と DTO 形状を確定する。
- **Selected Approach**:
  - Light プロパティ: `light/{lightId}/intensity`, `light/{lightId}/color`, `light/{lightId}/rotation`, `light/{lightId}/type`, `light/{lightId}/range`, `light/{lightId}/spotAngle`, `light/{lightId}/displayName` の 7 トピック（各 state）
  - Light 一覧: `lights/list` state（`{ items: Array<{ lightId, displayName, type }> }`）
  - Light コマンド: `light/command` event（`{ op: "add" | "remove", lightId?, initial?: LightInitialDto }`）
  - Light 追加完了: `light/added` event（`{ lightId, initial: LightInitialDto }`）
  - Stage: `stage/current` state（`{ stageKey: string | null }`）+ `stage/catalog` state（`{ items: Array<StageCatalogEntryDto> }`）+ `stage/command` event + `stage/loaded` event + `stage/load-failed` event
  - Volume Override: `volume/override/{typeFullName}/enabled` state + `volume/override/{typeFullName}/{paramName}` state + `volume/override/schema` request + `volume/command` event（将来の独立操作用、本 spec では未使用で予約）
  - Preview: `preview/command` event（`{ op: "set-enabled" | "reset-view", value?: bool }`）+ `preview/state` state（`{ enabled: bool }`）
- **Rationale**: CS-7 / SL-6 に忠実。プロパティ単位 coalesce により、別プロパティの中間値を巻き込まない。
- **Trade-offs**: topic 数が増え、購読登録コードがタブ起動時に大量発生。→ ViewModel 内部でヘルパーメソッド化して 1 行登録にする。
- **Follow-up**: topic 文字列は `StageLightingTopics` 静的クラス（Contracts asmdef）に定数として集約、typo 防止。

### Decision: プリセット永続化フォーマットは JSON + schemaVersion

- **Context**: SL-8, SL-9, Requirement 8 に従い、保存ファイル形式を確定する。
- **Selected Approach**:
  - 配置: `{Application.persistentDataPath}/vtuber-system-base/stage-lighting-volume-tab.json`（`IPresetStorage` 抽象で差し替え可能）
  - スキーマ:
    ```json
    {
      "schemaVersion": 1,
      "activePresetName": "dayStream",
      "presets": [
        {
          "name": "dayStream",
          "stageKey": "Stages/ModernCity",
          "lights": [
            {
              "displayName": "KeyLight",
              "type": "Directional",
              "rotation": [45, -30, 0],
              "color": [1.0, 0.95, 0.9],
              "intensity": 2.0,
              "range": 0,
              "spotAngle": 30
            }
          ],
          "volumeOverrides": [
            {
              "typeFullName": "UnityEngine.Rendering.Universal.Bloom",
              "enabled": true,
              "params": { "intensity": 0.3, "threshold": 1.0 }
            }
          ]
        }
      ]
    }
    ```
  - デバウンス 500 ms、`Application.quitting` / PlayMode 停止でフラッシュ
  - 破損時は `.corrupted-{unix-ms}` サフィックスでリネーム、初回起動扱い
- **Rationale**: `System.Text.Json`（`core-ipc-foundation` で既使用）で読み書き可能、人間可読、CS-8 と整合。
- **Trade-offs**: バイナリフォーマット（MessagePack 等）より若干大きいが、プリセット数は多くても数十件で問題なし。
- **Follow-up**: `schemaVersion` 2 への upgrade migration は将来必要になった時点で `SchemaMigrator` を追加。

### Decision: MVVM-Lite による UI 構造

- **Context**: Requirement 1〜12 の広範囲な UI 機能（プレビュー、ステージ、Light CRUD、Volume 動的 UI、プリセット CRUD、診断）を 1 タブに集約する。責務分離を構造化する必要がある。
- **Selected Approach**:
  - **View**: UIDocument（`StageLightingVolumeTab.uxml`）+ StyleSheet + `StageLightingVolumeTabPanel`（VisualElement ハンドル層）
  - **ViewModel**: `StageLightingVolumeTabViewModel` — ロジック層、`IUiCommandClient` / `IUiSubscriptionClient` / `IPresetStorage` / `IPreviewRenderTextureAccessor` に依存、UI に対しては observable なプロパティとイベントを公開
  - **Model / Services**: `IPresetStorage`（JSON 永続化）、`ILightListState`（lights/list state 受信の内部モデル）、`IVolumeSchemaCache`（Volume Override schema キャッシュ）、`IStageCatalogState`（stage/catalog state 受信の内部モデル）
- **Rationale**: UI Toolkit の data binding（`BindingPath` / `rebind` 等）と相性が良く、ViewModel だけでテスト可能（View を Unity のヘッドレスで動かす必要なし）。Requirement 12（単体検証可能性）と直接整合。
- **Trade-offs**: ViewModel 層の追加で LOC は増えるが、テスト工数・保守工数は減少。
- **Follow-up**: ViewModel は `Runtime` asmdef、View は `Runtime.Uxml` サブディレクトリに配置。

## Risks & Mitigations

- **R-A（高）: プレビューカメラと本番カメラの同時描画による GPU 負荷**（SL-1, SL-13 継承）— プレビュー RT 既定解像度 640×360、タブ非アクティブ時 `Camera.enabled = false`、診断 API で現在のプレビュー RT 解像度・描画回数を露出。
- **R-B（中）: SceneViewStyleCameraController の API 不明性**（R-14 継承）— 本 spec は `IPreviewCameraAdapter` 抽象で吸収。実装フェーズで直接リポジトリ参照。
- **R-C（中）: URP Volume メタデータの Reflection コスト**（R-2 継承）— メタデータは初回 Request/Response で 1 回だけ取得、以降キャッシュ。実行時は Reflection 不使用。
- **R-D（中）: プリセット切替中の視覚的中間状態**（R-4 継承）— FIFO event の同一フレーム処理 + UI 上の「切替中」オーバーレイで吸収、配信中切替は運用禁止とガイドに記載。
- **R-E（中）: lightId 再採番とプリセット整合性**（Requirement 7.8 継承）— プリセット保存時に lightId を保存せず、復元時は新 lightId で再構築。Light の安定識別は `displayName` + 配列順のみ。
- **R-F（低）: トピック名規約の typo による購読漏れ**（R-1 継承）— `StageLightingTopics` 静的クラス定数 + 単体テストで全トピックを走査。
- **R-G（低）: 永続化ファイル破損時のフォールバック UX**（R-11 継承）— 破損リネーム + 初回起動扱い + UI 通知バーに警告を設計（Requirement 8.7）。

## References

- [Unity - Manual: Volume component reference for URP](https://docs.unity3d.com/6000.2/Documentation/Manual/urp/volume-component-reference.html) — URP 標準 Volume Override 一覧
- [Class VolumeManager | SRP Core 17.x](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.1/api/UnityEngine.Rendering.VolumeManager.html) — `baseComponentTypeArray` API
- [Class VolumeParameter<T> | SRP Core 17.x](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.1/api/UnityEngine.Rendering.VolumeParameter-1.html) — VolumeParameter 基底クラスと `overrideState`
- [Pump Up The Volume: Writing custom Volume Components in Unity](https://tryfinally.dev/unity-custom-srp-volume-components) — カスタム Override の実装パターン
- [URP / Volume.cs - how to access the override settings at runtime via script?](https://discussions.unity.com/t/urp-volume-cs-how-to-access-the-override-settings-at-runtime-via-script/773268) — 実行時 Override 編集の実例
- [Can I create PanelSettings and RenderTexture at runtime for using with UIToolkit?](https://discussions.unity.com/t/can-i-create-panelsettings-and-rendertexture-at-runtime-for-using-with-uitoolkit/814433) — UI Toolkit と RenderTexture の連携
- [RenderTexture in VisualElement update every Frame](https://discussions.unity.com/t/rendertexture-in-visualelement-update-every-frame/928429) — RT 背景の更新頻度問題
- [Unity - Manual: Image UXML element](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-uxml-element-Image.html) — UI Toolkit Image 要素
- [SceneViewStyleCameraController（Hidano-Dev）](https://github.com/Hidano-Dev/SceneViewStyleCameraController) — 採用パッケージ
