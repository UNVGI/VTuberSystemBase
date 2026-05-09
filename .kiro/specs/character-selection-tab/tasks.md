# Implementation Plan

> 本タスクは design.md の「Components and Interfaces」「System Flows」「File Structure Plan」「Requirements Traceability」に基づく。依存方向は Abstractions → View → State → Services → Presenters → Composition Root を厳守する。TDD を基本とし、各 Presenter / Service 実装前にテストダブルと失敗テストを先行整備する。

## 1. Foundation: パッケージ雛形と共通抽象の整備

- [x] 1.1 UPM パッケージ骨格と asmdef 境界の確立
  - UPM パッケージ（`jp.hidano.vtuber-system-base.character-selection-tab`）の `package.json`・Runtime/Editor/Tests ディレクトリ・README を作成し、design.md の File Structure Plan に従うフォルダ階層を用意する。
  - Runtime asmdef を `VTuberSystemBase.CharacterSelectionTab.Runtime` として作成し、参照先を `UiToolkitShell.Runtime` / `UiToolkitShell.CommonUi` / `VTuberSystemBase.CoreIpc.Abstractions` / `com.unity.addressables`（間接）に限定、`output-renderer-shell` 実装・他タブ spec・core-ipc 具体実装・RAC 本体への直接参照を構造的に禁止する。
  - Tests.Runtime asmdef、Editor asmdef、および `InternalsVisibleTo` 設定を整え、Unity 6.3 で空プロジェクトからコンパイル可能な状態にする。
  - 観測可能な完了条件: パッケージを Unity 6.3 プロジェクトに配置したときコンパイルエラーなしでロードされ、禁止参照を加えるとコンパイルエラーとなることを確認できる。
  - _Requirements: 1.7, 10.7_

- [x] 1.2 ランタイム DTO とドメイン値型の定義
  - `SettingValue`（Float/Int/Bool/Color/Enum/Vector3 の discriminated union 相当 struct）とその `ToJson` / `FromJson` を実装する。
  - `SlotSnapshot`, `SlotStatus`, `InFlightOperationKind`, `InFlightToken`, `InFlightOutcome`, `StateChangeScope`, `ConnectionStatusCode`, `AvatarCatalogEntry`, `SettingSchemaEntry`, `SettingType` を定義する。
  - `CharacterTabConfig`（PresetDebounce=500ms, AssignmentTimeout=5s, SchemaRequestTimeout=5s, InteractionIdleThreshold=200ms, PresetScopeId, DefaultThumbnailAddressableKey の既定値付き）を実装する。
  - 観測可能な完了条件: 各値型のラウンドトリップ（生成 → JSON → 復元）テストが緑色で通る。
  - _Requirements: 4.10, 5.2, 5.6_

- [x] 1.3 IPC トピックビルダとペイロード DTO 群の実装
  - `CharacterTopics` の定数と `SlotAssignment` / `SlotStatus` / `SlotSettingValue` / `SlotSettingsPrefix` / `SlotCommand` / `SlotError` / `AvatarSchema` ビルダを実装し、`Safe(value)` で ASCII 英数字 + `- _ .` 以外を percent-encode する。
  - `SlotCatalogPayload`, `SlotCatalogEntry`, `AvatarCatalogPayload`, `SlotAssignmentPayload`, `SlotStatusPayload`, `SlotSettingValuePayload`, `SlotCommandPayload`, `SlotErrorPayload`, `AvatarSchemaRequestPayload`, `AvatarSettingsSchemaPayload`, `SettingSchemaEntry` DTO を `[Serializable]` + `init` プロパティで定義する。
  - 未知列挙値・未知フィールドは前方互換で無視する方針を DTO 実装で担保（列挙には `Unknown` 値または文字列持ち回し）。
  - 観測可能な完了条件: `CharacterTopicBuilderTests` が空文字・特殊文字・通常値に対して期待通りのトピック文字列を返し、各 payload DTO の JSON シリアライズ/デシリアライズ往復テストが通る。
  - _Requirements: 2.1, 3.1, 4.2, 4.3, 4.4, 5.3, 5.8, 7.2, 8.1, 9.2, 9.3, 9.4_

- [ ] 1.4 テストダブル群の整備（TDD 基盤）
  - `FakeUiCommandClient`（PublishState/PublishEvent/RequestAsync 記録、応答ペイロード投入用 API 付き）を実装する。
  - `FakeUiSubscriptionClient`（topic → コールバック登録、テストから `Emit(topic, payload)` で状態 state / event を注入可能）を実装する。
  - `FakeAsyncAssetLoader`（key に対して成功 Sprite / `KeyNotFound` / `TypeMismatch` を設定でき、scope 単位解放を検証可能）を実装する。
  - `InMemoryPresetStorage`（`IPresetStorage` を満たし破損シミュレーション API を持つ）、`ManualClock`（`IClock` + `Advance(TimeSpan)` + イベント発火）、`FakeConnectionStatus`（`Connected`/`Disconnected` 切替と `OnStatusChanged` 発火）、`FakeDiagnosticsLogger`（ログ履歴取得）を実装する。
  - 観測可能な完了条件: 各 Fake のセルフテストが通り、後続タスクから注入可能な形で Tests.Runtime asmdef から参照できる。
  - _Requirements: 11.1, 11.2, 11.3, 11.6, 11.7_

## 2. Core State & Services

- [ ] 2.1 CharacterTabStateStore と状態スコープ通知の実装
  - `ICharacterTabStateStore` と具象クラスを実装し、`ApplySlotCatalog` / `ApplyAvatarCatalog` / `ApplyAssignment` / `ApplyStatus` / `ApplySettingValue(isFromRemote)` / `ApplyError` / `TryBeginInFlight` / `EndInFlight` / `SetActivePreset` / `SetConnectionStatus` を提供する。
  - Slot ID 昇順での表示順安定化、未知 `slotId` への Apply は警告ログ + 無視、同一 Slot への重複 InFlight は `TryBeginInFlight=false` で拒否する挙動を実装する。
  - `OnChanged(StateChangeScope)` を必要最小スコープのみで発火、メインスレッド専有契約（ワーカー書込は `InvalidOperationException`）を enforce する。
  - 設定値については `InteractionGuard` が提供する `IsInteracting` と連動して `isFromRemote=true` のリモート値を「操作中はバッファ、終了時に最新値を確定」する仕組みを内部に備える。
  - 観測可能な完了条件: Store 単体テストで Slot 追加・割当・ステータス変更・InFlight ロック・設定値バッファ解放の各シナリオが期待 `OnChanged` スコープで発火し、スレッド規約違反で例外送出する。
  - _Requirements: 2.1, 2.3, 2.8, 2.9, 3.9, 4.5, 4.7, 4.10, 5.7_
  - _Boundary: CharacterTabStateStore_

- [ ] 2.2 (P) IClock / SystemClock と InteractionGuard の実装
  - `IClock` / `SystemClock`（`DateTimeOffset.UtcNow` 既定）を実装し、テストから `ManualClock` で差替可能にする。
  - `IInteractionGuard` を実装し、`MarkInteracting(slotId, settingKey)` / `EndInteracting` / `Tick(now)` により 200ms アイドル自動 end、`OnChanged(InteractingChangedEventArgs)` を発火する。
  - `settingKey` 単位の追跡、複数 Slot × 複数 key の同時進行を許容、単調時刻前提を実装する。
  - 観測可能な完了条件: `ManualClock` を 199ms / 200ms / 201ms 進めるテストで `OnChanged` が閾値超過時にのみ false 遷移を発火し、明示 `EndInteracting` と自動 end の両経路が確認できる。
  - _Requirements: 5.7, 11.7_
  - _Boundary: InteractionGuard, SystemClock_

- [ ] 2.3 (P) DynamicSettingControlFactory による設定 UI 動的生成
  - `IDynamicSettingControlFactory.Build(SettingSchemaEntry, SettingValue)` を実装し、Float/Int → `VsbSlider`、Bool → `Toggle`、Color → `VsbColorPicker`、Enum → `VsbToggleGroup`、Vector3 → 3 連スライダー、`Kind == "command"` → ボタンの型マッピングを行う。
  - 生成 Root に `vsb-char-tab__setting-row` クラスを付与し、子要素に label と入力コントロールを配置する。
  - `min > max`・未知 `SettingType`・必須フィールド欠落を検知したら診断ログに記録し、該当コントロールを非活性化または `null` Root でスキップ用 `SettingControl` を返す。
  - `PointerDownEvent`/`PointerUpEvent` を結線し `InteractingChanged` / `ValueChanged` / `Committed` イベントを公開する。
  - 観測可能な完了条件: Type ごとに期待する VisualElement 種別が Root 下に構築され、異常メタデータで診断ログが記録され、範囲外入力で `ValueChanged` が抑止される単体テストが通る。
  - _Requirements: 5.2, 5.6, 5.11, 7.5_
  - _Boundary: DynamicSettingControlFactory_

- [ ] 2.4 (P) AvatarThumbnailResolver と既定サムネイルフォールバック
  - `IAvatarThumbnailResolver.LoadThumbnail(avatarKey, scopeId, onCompleted)` / `Release` / `ReleaseAll` を実装し、内部では `IAsyncAssetLoader.LoadAsync<Sprite>("{avatarKey}.thumbnail", scopeId, ...)` を呼ぶ。
  - 成功時 `AvatarThumbnailResult { Success=true, IsFallback=false }`、`KeyNotFound` / 型不一致時は `DefaultAvatarThumbnail.asset`（Addressables 固定 key）を返して `IsFallback=true` とし、診断ログに `Thumbnail.Fallback(avatarKey, reason)` を記録する。
  - 重複ロードは上流の重複抑止に委譲し、同一 `(avatarKey, scopeId)` の多重要求でもシングルハンドルに集約されることを検証する。
  - `DefaultAvatarThumbnail.asset`（本 spec パッケージ同梱）を Addressables に登録する手順と検証コードを含める。
  - 観測可能な完了条件: `FakeAsyncAssetLoader` でキー未登録を返すと Default Sprite と `IsFallback=true` が呼出側に届き、診断ログに記録される単体テストが通る。
  - _Requirements: 3.4, 3.4a, 6.2, 6.3, 6.4, 6.5, 6.6, 9.5_
  - _Boundary: AvatarThumbnailResolver_

- [ ] 2.5 (P) JsonPresetStorage のファイル I/O 実装
  - `IPresetStorage` を実装する `JsonPresetStorage` を作成し、既定パス `Application.persistentDataPath/character-selection-tab/presets/`、`{presetId}.json`（UTF-8 JSON, GUID ファイル名）+ `_active.json` の配置規約を採用する。
  - `LoadAllAsync` / `LoadActivePresetIdAsync` / `SaveAsync`（一時ファイル → `File.Move` アトミック書込） / `DeleteAsync` / `SetActiveAsync` / `CheckHealthAsync` を実装する。
  - JSON パース失敗時は同ディレクトリに `{presetId}.json.bak.{unixms}` としてリネームし、診断ログ `Preset.Load(..., corrupted=true)` を記録、`StorageHealthReport.CorruptedCount / BackedUpFiles` に反映する。
  - 書込失敗（ディスク満杯・権限）時は例外を `StorageFailure` に包んで返す。
  - 観測可能な完了条件: 実ファイルシステム（一時ディレクトリ）での CRUD 往復、破損 JSON を差し込んだ際の `.bak.<ts>` リネーム、`_active.json` 更新が Editor テストで検証できる。
  - _Requirements: 8.7, 8.9, 8.10, 9.6_
  - _Boundary: JsonPresetStorage_

- [ ] 2.6 PresetStoreLogic によるプリセット CRUD とデバウンス
  - `IPresetStoreLogic` を実装し、`ListPresets` / `GetActivePreset` / `CreateAsync` / `RenameAsync` / `DuplicateAsync` / `DeleteAsync` / `SetActiveAsync` / `MarkSlotAssignmentChanged` / `MarkSettingValueChanged` / `FlushPendingAsync` と `OnSaved` / `OnLoaded` イベントを公開する。
  - 名称検証: trim 済み非空、重複不可（`DuplicateName`）、アクティブプリセット削除拒否（`CannotDeleteActive`）。
  - 変更マークから 500ms デバウンス（`IClock`）後に `IPresetStorage.SaveAsync` を呼び、書込中の追加変更はフラッシュ完了後に再デバウンスする。
  - 書込失敗時は `OnSaved(success=false)` 発火 + 次回変更時リトライ、UI クラッシュを発生させない。
  - 保存対象は「Slot ↔ アバター割当」「各 Slot の設定値」のみに限定し、Slot 個数・RAC グローバル設定・タブ共通 UI 状態は保存しない。
  - 観測可能な完了条件: `ManualClock` で 499ms / 500ms 進めたときの書込タイミング、複数変更のまとめ書き、破損時のバックアップ反映、書込失敗時のリトライがテストで検証できる。
  - _Requirements: 8.1, 8.1a, 8.1d, 8.2, 8.3, 8.7, 8.9, 8.10, 9.6_
  - _Boundary: PresetStoreLogic_

## 3. IPC Integration Layer

- [ ] 3.1 CharacterTabIpcBinder による購読集約と送信薄ラッパ
  - `ICharacterTabIpcBinder.SubscribeAll` / `UnsubscribeAll` で `slots/catalog` / `avatars/catalog` / `slot/+/assignment` / `slot/+/status` / `slot/+/settings/+` / `slot/+/error` を登録し、`ISubscriptionToken` を辞書で保持する。
  - 新規 Slot 発見時に `AddDynamicSubscriptions(slotId)`、Slot 削除時に対応トークンを Dispose するロジックを実装する。
  - 受信コールバックでは `CharacterTabStateStore.Apply*` に振り分け、`slot/{id}/error` 受信時は `ApplyError` + 該当 InFlight の `Failed` 解除、`slot/{id}/status` 受信時は `ApplyStatus` + InFlight 解除を行う。
  - 送信 API として `PublishAssignment(slotId, avatarKey?)`（state）、`PublishSettingValue`（state）、`PublishSlotCommand`（event, Reset/Reload/PresetApply）、`RequestAvatarSchemaAsync`（request, 5 秒タイムアウト）を提供する。
  - デシリアライズ失敗到達時は警告ログのみで破棄（上流で破棄される前提の保険）。
  - 観測可能な完了条件: `FakeUiSubscriptionClient` から各トピック state/event を流すと Store の該当メソッドが期待スコープで呼ばれ、送信 API 呼出で `FakeUiCommandClient` に期待 topic + payload が記録される統合テストが通る。
  - _Requirements: 2.1, 3.1, 3.9, 4.2, 4.3, 4.4, 4.6, 4.9, 5.3, 5.8, 7.1, 7.2, 7.4, 7.8, 9.2, 9.3, 9.4_
  - _Depends: 2.1_

- [ ] 3.2 PresetRestoreOrchestrator の実装と接続確立連動
  - `IPresetRestoreOrchestrator.ReplayActivePresetAsync` を実装し、アクティブプリセットの Slot ごとに `PublishState slot/{id}/assignment` を送信、続いて `slot/{id}/settings/{key}` を順次送信する。
  - `IConnectionStatus.OnStatusChanged` を購読し `Connecting → Connected` 遷移で 1 度自動起動、再接続時もアクティブプリセットで整合を取る。
  - 保存された `avatarKey` が `AvatarCatalog` に存在しない場合、当該 Slot のみ `null` 送信（empty） + 警告ログ、`UnresolvedAvatarKeys` に追加し、他 Slot は継続する。
  - 初回起動（保存ファイルなし）は即時完了、部分失敗時は `OnProgress(RestoreProgressEvent)` で `FailedSlots` を通知、他 Slot の復元は継続する。
  - 観測可能な完了条件: `FakeConnectionStatus` を `Disconnected → Connected` に遷移させた時に `FakeUiCommandClient` にアクティブプリセットの Slot 数だけ `PublishState` が記録され、未解決アバターのシナリオで該当 Slot のみ empty + 診断ログが発火する統合テストが通る。
  - _Requirements: 7.8, 8.5, 8.6, 8.8, 8.11_
  - _Depends: 2.1, 2.6, 3.1_

## 4. View Assets (UXML / USS)

- [ ] 4.1 (P) キャラクタータブ Root UXML / USS の実装
  - `CharacterTab.uxml` を作成し、プレイヤーカード領域・アバター候補領域・設定パネル領域・プリセットバー・診断領域を含むレイアウトを構築する。
  - `CharacterTab.uss` を作成し、`ui-toolkit-shell` の USS セレクタ命名規約（`vsb-` プレフィクス + BEM 風 `vsb-char-tab__*`）を遵守し、スキン差し替え経路（UI-3）から見た目を変更可能にする。
  - `ViewQueryHelpers.cs` を作成し、VisualElement Query の定型化ヘルパを提供する。
  - 観測可能な完了条件: Unity Editor 上で UXML Preview が必須要素を描画し、USS が `vsb-*` クラス名でスタイリングされていることを確認できる。
  - _Requirements: 1.1, 1.2, 1.8_
  - _Boundary: View/CharacterTab_

- [ ] 4.2 (P) PlayerCard / AvatarCatalogItem / SettingRow / PresetBar 各 UXML テンプレートの実装
  - `PlayerCard.uxml`：Slot 識別子ラベル、アバター表示名、設定ボタン、reset/reload ボタン、警告バッジ、empty/assigned/error 状態切替用 USS クラスフック（`vsb-player-card--empty` / `--assigned` / `--error`）を含む。
  - `AvatarCatalogItem.uxml`：サムネイル画像、表示名、選択ハイライト用クラス、ロード中プレースホルダ要素を含む。
  - `SettingRow.uxml`：設定項目 1 行のテンプレ（label + スロット用コンテナ、`DynamicSettingControlFactory` が子を挿入）。
  - `PresetBar.uxml`：プリセット一覧ドロップダウン、作成・リネーム・複製・削除・アクティブ化ボタン、アクティブプリセット名表示、重複名称バリデーションメッセージ領域を含む。
  - 観測可能な完了条件: 各 UXML が Unity Editor でプレビュー可能で、必須要素が全て `name` 属性付きで Query 可能である。
  - _Requirements: 1.1, 1.2, 2.2, 2.4, 2.7, 3.2, 3.3, 5.2, 8.1a, 8.1b_
  - _Boundary: View/Templates_

- [ ] 4.3 (P) DefaultAvatarThumbnail アセット登録
  - 本 spec パッケージ同梱の `DefaultAvatarThumbnail.asset`（Sprite）を作成し、固定 Addressables key（`CharacterTabConfig.DefaultThumbnailAddressableKey`）に登録する。
  - Bootstrap 時に preload 存在検証を行い、失敗時は診断エラーを出す仕組みをドキュメント化する。
  - 観測可能な完了条件: Addressables Analyze で Default key が解決可能であり、未解決時には起動時診断で警告が出る挙動がテストから確認できる。
  - _Requirements: 3.4a, 6.7_
  - _Boundary: View/DefaultThumbnail_

## 5. Presenters: UI 挙動の実装

- [ ] 5.1 SlotListPresenter によるプレイヤーカード描画と操作ハンドリング
  - `StateStore.OnChanged(SlotCatalog | SlotStatus | Assignment | InFlight)` を購読し、`PlayerCard.uxml` を初回 Clone + 以降は差分更新で描画する。
  - empty / assigned / error の 3 状態を USS クラス切替で可視化、Slot ID 昇順で表示順を固定する。
  - カードクリックで `AssignmentFlowPresenter.SelectSlot(slotId)` を呼び、設定ボタンで `SettingsPanelPresenter.OpenForAsync(slotId)`、reset/reload ボタンで `AssignmentFlowPresenter.RequestOperationAsync(slotId, kind)` を呼ぶ。
  - 接続未確立 / Slot Catalog 未受信時はプレースホルダカードと操作非活性、error 状態では操作系を縮退して警告バッジを表示、進行中表示（スピナー相当）を InFlight に応じて切替える。
  - 観測可能な完了条件: Store に 3 件の Slot を反映すると 3 枚のカードが Slot ID 昇順で生成され、状態変更で再 Clone が発生せず属性更新のみで切り替わる単体テストが通る。
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9, 7.1, 7.2_
  - _Depends: 2.1, 4.2_

- [ ] 5.2 AvatarCatalogPresenter によるアバター候補グリッドとサムネイル解決
  - `StateStore.OnChanged(AvatarCatalog)` を購読して `AvatarCatalogItem.uxml` を Clone/差分更新する。
  - 各アイテムで `IAvatarThumbnailResolver.LoadThumbnail(avatarKey, "tab:character", callback)` を起動し、読込中はプレースホルダ表示、完了で Sprite を適用する。
  - 候補重複 `avatarKey` は Store 側で一意化済みの前提を保険として検知し診断ログに記録する（Req 3.8）。
  - 候補ゼロ件（Addressables 未登録）時は案内メッセージカードを表示し、`AssignmentFlowPresenter` 経由の割当開始を無効化する。
  - 候補クリックで `AssignmentFlowPresenter.RequestAssignment(avatarKey)` を呼び、「Slot 選択中」モードに応じたハイライトを切替える。
  - 候補取得失敗時は再試行ボタン付きエラー表示を提示する。
  - 観測可能な完了条件: Store に候補を流すとグリッドに表示名 + サムネイルが出現し、未解決キーはデフォルトサムネイルにフォールバック、ゼロ件時は操作が非活性になることをテストで確認できる。
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.4a, 3.5, 3.6, 3.7, 3.8, 3.9, 6.3, 6.7_
  - _Depends: 2.1, 2.4, 4.2_

- [ ] 5.3 AssignmentFlowPresenter による割当 2 ステップ UX と送信
  - `SelectSlot` / `ClearSelectedSlot` / `RequestAssignment(avatarKey)` / `RequestOperationAsync(slotId, Reset|Reload)` を実装する。
  - `RequestAssignment` は `StateStore.TryBeginInFlight(Assignment)` 成功時のみ `IUiCommandClient.PublishState slot/{id}/assignment` を送信し、`IClock` でタイムアウト 5 秒タイマを起動する。
  - `RequestOperationAsync` は `PublishEvent slot/{id}/command` を送信し、`SendResult` を返却、Reload は UI をローディング表示に切替える。
  - `StateStore.ApplyStatus → OnChanged(SlotStatus)` を受けて `EndInFlight(CompletedOk)`、タイムアウトで `EndInFlight(TimedOut)` + UI に警告 + 再試行可能状態に復帰する。
  - 割当失敗イベント（`slot/{id}/error`）受信で `EndInFlight(Failed)` + Slot をエラー状態に切替え、他 Slot は継続する。
  - `avatarKey` が `AvatarCatalog` に存在しない場合は送信前に抑止し診断ログ + UI エラー表示を行う。
  - 複数 Slot 並列操作を許容し、同一 Slot の重複操作のみを抑止する（冪等な UI 状態）。
  - 観測可能な完了条件: `ManualClock` で 5 秒経過時に `TimedOut` に遷移し警告が表示される、status 受信で InFlight 解除、error 受信で Slot がエラー状態に遷移、の各テストが通る。
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 4.10, 6.1, 6.8, 7.4, 9.2, 9.3_
  - _Depends: 2.1, 2.2, 3.1_

- [ ] 5.4 SettingsPanelPresenter による動的設定 UI と値変更送信
  - `OpenForAsync(slotId)` で `CharacterTabIpcBinder.RequestAvatarSchemaAsync(avatarKey, 5s)` を呼びスキーマを取得、`DynamicSettingControlFactory.Build` で VisualElement ツリーを構築し View にアタッチ、`SlotSnapshot.SettingValues` で初期値を復元する。
  - `ValueChanged` イベントで `IUiCommandClient.PublishState slot/{id}/settings/{key}` を送信し、`InteractionGuard.MarkInteracting` を発火する。`PointerUp` または 200ms アイドルで `EndInteracting` し、バッファされたリモート state を適用する。
  - スキーマの `Kind == "command"` 項目は `PublishEvent slot/{id}/settings/{key}` で送信し連続値と別トピック扱いとする。
  - 値域違反は送信抑止 + 近傍にバリデーションエラー表示（`DynamicSettingControlFactory` と連動）、未知型項目はスキップ + 診断ログ。
  - アバター切替（`SlotSnapshot.AssignedAvatarKey` の変更）を検知すると `Close()` → `OpenForAsync` で UI を再生成する。
  - スキーマ取得タイムアウト/失敗時はエラー表示 + 再試行ボタンを提示し、他領域の動作を阻害しない。
  - ドラッグ連続操作では UI 側流量制御を最小化し上流 coalesce に委譲する。
  - 観測可能な完了条件: スキーマ → UI 生成 → 値変更 → `PublishState` 記録のラウンドトリップ、操作中の逆流抑止、アバター切替時の UI 再生成、タイムアウト時の再試行 UI 表示の各テストが通る。
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9, 5.10, 5.11, 7.4, 7.5, 9.2, 9.3_
  - _Depends: 2.1, 2.2, 2.3, 3.1_

- [ ] 5.5 PresetManagerPresenter による CRUD UI とアクティブ化
  - `RenderPresetBar` で `PresetStoreLogic.ListPresets()` を描画し、アクティブプリセット名を `PresetBar.uxml` 上に可視化する。
  - `CreatePresetAsync` / `RenamePresetAsync` / `DuplicatePresetAsync` / `DeletePresetAsync` / `ActivatePresetAsync` を実装し、`PresetOperationResult` で成功・失敗（`DuplicateName` / `NotFound` / `StorageFailure` / `InvalidName` / `CannotDeleteActive`）を返す。
  - 重複名称時は作成を拒否し UI にバリデーションエラーを表示する。アクティブプリセットの削除は拒否する。
  - `ActivatePresetAsync` 成功時に `PresetStoreLogic.SetActiveAsync` + `PresetRestoreOrchestrator.ReplayActivePresetAsync` を呼び、通常 state 経路で一括送信する。`StateStore.SetActivePreset` で UI を即時更新する。
  - プリセット切替中のメイン出力側部分失敗は `RestoreProgressEvent` を受けて該当 Slot を empty + 警告で反映、他 Slot は継続する。
  - 観測可能な完了条件: 重複名称作成で UI にエラー表示が出る、アクティブ切替で `FakeUiCommandClient` に Slot 数分の `PublishState` が記録され、アクティブ削除が拒否される各テストが通る。
  - _Requirements: 8.1a, 8.1b, 8.1c, 8.1d_
  - _Depends: 2.6, 3.2, 4.2_

- [ ] 5.6 (P) TabDiagnosticsPresenter による診断パネル描画
  - `CharacterTabDiagnostics.Capture()` から `TabDiagnosticsSnapshot`（Slot 数・割当済み数・エラー数・InFlight 件数・最終保存時刻・接続状態・アクティブプリセット ID・破損バックアップ件数）を取得し、タブ下部の診断領域に描画する。
  - `StateStore.OnChanged(Connection)` / `PresetStoreLogic.OnSaved` を 1 秒スロットルで購読して再描画する。
  - 接続断中はプレイヤーカード領域と協調してプレースホルダ表示を担う。
  - 観測可能な完了条件: StateStore / PresetStoreLogic に値を流すと診断領域の表示が閾値の揺らぎなしで更新され、1 秒以内の連続変更は 1 回の再描画に間引かれるテストが通る。
  - _Requirements: 2.9, 8.1b, 9.9_
  - _Boundary: TabDiagnosticsPresenter, CharacterTabDiagnostics_
  - _Depends: 2.1, 2.6_

## 6. Composition Root と Lifecycle 統合

- [ ] 6.1 CharacterTabBootstrapper による Composition Root と購読ライフサイクル
  - コンストラクタで `ITabLifecycleHandle` / `IUiCommandClient` / `IUiSubscriptionClient` / `IConnectionStatus` / `IAsyncAssetLoader` / `IDiagnosticsLogger` / `IPresetStorage` / `IClock`（+ テスト用 override）を受け取り、Store → Services → Presenters → IpcBinder → RestoreOrchestrator の順で構築する。
  - `CharacterTabConfig` の境界値検証（負値・ゼロ禁止）を行い、違反時は既定値フォールバック + 診断ログ。
  - `OnActivated`/`OnDeactivated`/`Dispose` に応じて Presenter のアニメーション・タイマ再開、購読は常時維持（タブ非アクティブ中もバックグラウンド最新化）、`Dispose` で Presenter カスケード解放 + `CharacterTabIpcBinder.UnsubscribeAll()` + `AssetLoader.ReleaseAll("tab:character")` + `PresetStoreLogic.FlushPendingAsync()` を実行する。
  - `IsRunning` ガードで二重 Activate を no-op、`Dispose` は冪等にする。
  - `ui-toolkit-shell` の `UiToolkitShellSkinProfile.CharacterTabVisualTreeAsset` / `CharacterTabStyleSheets` に本 spec の UXML/USS を参照させる手順を README に明記する。
  - 観測可能な完了条件: `CharacterTabBootstrapper` を構築 → Activate → Deactivate → Dispose するテストで購読が全解放され、Addressables ハンドルとタイマが残存しないことを診断スナップショットで確認できる。
  - _Requirements: 1.1, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 7.7, 8.4, 9.1_
  - _Depends: 3.1, 3.2, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

- [ ] 6.2 アプリ終了フラッシュと Standalone / PlayMode 両対応
  - `Application.quitting` と Editor の `playModeStateChanged == ExitingPlayMode` の両方で `PresetStoreLogic.FlushPendingAsync` を冪等に呼び出すフックを実装する。
  - Edit モードでは実行時ロジック（UI 初期化・IPC 購読・永続化読込）を起動しないことを `ui-toolkit-shell` の PlayMode 限定駆動契約で構造的に担保し、単体テストで検証する。
  - PlayMode 開始・停止を 5 回繰り返しても購読重複・UI 要素重複生成・ファイルロック残存がないことをテストで確認する（ドメインリロード跨ぎの状態維持を禁止）。
  - スタンドアロン時と Editor PlayMode 時で UI 挙動・割当レイテンシ・永続化挙動を同一経路に保つ。
  - 観測可能な完了条件: PlayMode 反復 5 回テストで Addressables ハンドルリークと購読リークがゼロであることを診断スナップショットから確認できる。
  - _Requirements: 8.4, 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_
  - _Depends: 6.1_

## 7. Failure Handling & Observability Integration

- [ ] 7.1 不可用アバターと RAC エラー受信時の Slot 縮退統合
  - `CharacterTabIpcBinder` が受信する `slot/{id}/error`（RAC エラー）と、`PresetRestoreOrchestrator` が検出する保存済み `avatarKey` 未解決について、Store 上の該当 Slot を empty または error 状態に遷移させ、`SlotListPresenter` が警告バッジを表示する配線を完成させる。
  - `AvatarThumbnailResolver` のフォールバックが `AvatarCatalogPresenter` で可視化され、他候補・他 Slot の動作を阻害しないことを統合テストで検証する。
  - 失敗事由（topic / slotId / avatarKey / errorCode）を含む診断ログを `LogCategory.TabSpec` に記録する。
  - 観測可能な完了条件: `slot/{id}/error` 流入で該当 Slot のみがエラー状態となり他 Slot が継続、未解決 avatarKey 復元で該当 Slot のみが empty + 警告に落ちる統合テストが通る。
  - _Requirements: 7.1, 7.2, 7.3, 7.9, 9.4, 9.5_
  - _Depends: 3.1, 3.2, 5.1, 5.2_

- [ ] 7.2 IPC 切断 / 回復時の UI 縮退と自動再取得
  - `IConnectionStatus.OnStatusChanged` を Bootstrapper で購読し、切断中は Slot 割当・設定送信 UI を非活性化（プリセット CRUD のローカル操作は継続）する配線を実装する。
  - 回復時は `CharacterTabIpcBinder` が Slot 一覧・アバター候補を再取得する購読状態を保持し、`PresetRestoreOrchestrator` がアクティブプリセットを再送する。
  - Command 送信 API が接続未確立 / サイズ上限超過エラーを返した際は診断ログ記録のみで UI クラッシュ・描画停止を発生させない。
  - いかなる失敗経路でもメイン出力（Display 2+）に警告・エラー UI を描画しない構造（`IDiagnosticsLogger` 経路以外を持たない）を検証テストで確認する。
  - 観測可能な完了条件: `FakeConnectionStatus` を `Connected → Disconnected → Connected` に遷移させると送信系 UI が切断中に非活性化し、回復時に `FakeUiCommandClient` にアクティブプリセット再送が記録される統合テストが通る。
  - _Requirements: 7.4, 7.6, 7.7, 7.8, 7.9_
  - _Depends: 6.1, 7.1_

- [ ] 7.3 診断 API と観測性ログの整備
  - `ICharacterTabDiagnostics.Capture()` を実装し、`TabDiagnosticsSnapshot`（Slot 数・割当済み・エラー数・InFlight 件数・最終保存時刻・接続状態・アクティブプリセット・破損バックアップ件数）を副作用なく生成する。
  - design.md の Monitoring セクションで列挙されたログ項目（Init.*, Assign.*, SettingSchema.*, Setting.Change, Preset.*, Thumbnail.*, Ipc.*, Restore.*, Connection.*）を `IDiagnosticsLogger` 経由で記録し、メイン出力サーフェスへ描画する経路を持たないことを構造的に保証する。
  - ログレベルは `ui-toolkit-shell` の設定から外部切替可能とする。
  - `CharacterTabBootstrapper.CaptureDiagnostics()` から `TabDiagnosticsSnapshot` を外部公開する。
  - 観測可能な完了条件: 主要 5 シナリオ（初期化・割当・設定変更・プリセット保存・サムネイルフォールバック）でログが期待カテゴリ・コンテキストで記録され、`Capture()` が正しい件数を返すテストが通る。
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 9.9_
  - _Depends: 6.1_

## 8. 単体検証と回帰テスト

- [ ] 8.1 Integration テスト: 初期同期・割当ラウンドトリップ・設定スキーマ・プリセット・切断回復
  - モック IPC で `slots/catalog` / `avatars/catalog` を発行 → Store 更新 → Presenter の UI 反映を 1 本で検証する統合テストを実装する。
  - `FakeUiCommandClient` に記録された割当 `PublishState` に対応する `slot/{id}/status` を `FakeUiSubscriptionClient` から流し、UI が `Assigned` 状態へ遷移するラウンドトリップテストを実装する。
  - モック `RequestAsync` がスキーマを返すと `DynamicSettingControlFactory` が VsbSlider 等を生成し View にアタッチされることを検証する。
  - `PresetManagerPresenter.ActivatePresetAsync` が `PresetRestoreOrchestrator.ReplayActivePresetAsync` を呼び全 Slot 分の `PublishState` が順次送信されることを検証する。
  - `FakeConnectionStatus` を `Connected → Disconnected → Connected` に遷移させ、切断中は送信抑止、回復時に自動再送されることを検証する。
  - 観測可能な完了条件: 上記 5 シナリオが Tests.Runtime から緑色で通る。
  - _Requirements: 11.1, 11.2, 11.5, 11.6, 11.7_
  - _Depends: 6.2, 7.2, 7.3_

- [ ] 8.2 PlayMode サンプルシーンと手動検証手順
  - `CharacterTabPlayModeSample.unity` をモック UI シェル構成で作成し、プレイヤーカード表示 → アバター選択 → 割当確定 → 設定スライダー操作までを手動で確認できる手順を README に整備する。
  - `UiToolkitShellSkinProfile.CharacterTabStyleSheets` に利用者 USS を注入する差し替え検証手順をサンプルに含める。
  - PlayMode 開始・停止 5 回の反復が手動でも確認できる手順を README に記載する。
  - 観測可能な完了条件: PlayMode でサンプルシーンを開くと本タブがモックデータでフル機能を表示し、README 記載手順で一連の割当・設定・プリセット切替が実行できる。
  - _Requirements: 11.4_
  - _Depends: 6.2_

- [ ] 8.3* パフォーマンス / 負荷検証（任意）
  - Slot 8 件 + 各設定 10 項目でのスライダー連続操作（60Hz × 5 秒）において、検証ハーネスの `Time.unscaledDeltaTime` が 16.67ms を維持することを計測する（Requirement 5.4, 5.5, 1.6 のフレーム維持要件の裏付け）。
  - プリセット 50 件保存後の起動時間（`CheckHealthAsync` + `ReplayActivePresetAsync`）が 2 秒以内に完了することを計測する（Requirement 8.5, 10.2 の挙動同一性）。
  - `CharacterTabBootstrapper` の OnActivated / OnDeactivated を 100 回繰り返しタブ切替所要時間の 95 パーセンタイルが 16ms 以下であることを計測する（Requirement 1.6）。
  - 観測可能な完了条件: 計測レポートが 3 指標を記録し、しきい値を満たすか未達かを判定可能にする。
  - _Requirements: 1.6, 5.4, 5.5, 8.5, 10.2_
  - _Depends: 8.2_
