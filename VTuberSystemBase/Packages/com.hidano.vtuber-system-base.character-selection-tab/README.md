# com.hidano.vtuber-system-base.character-selection-tab

VTuberSystemBase の Display 1 UI 側 character-selection-tab。MoCap アクター（RealtimeAvatarController の Slot）とアバターの対応付け、個別設定、プリセット管理 UI を提供する。

## アーキテクチャ層 (依存方向)

```
Abstractions (UiToolkitShell facades, CoreIpc.Abstractions)
    ↓
Contracts (本パッケージ Runtime/Contracts: topics + payload DTO)
    ↓
View (UXML / USS / VisualElement query helpers)
    ↓
State (CharacterTabStateStore, domain value types)
    ↓
Services (AvatarThumbnailResolver, DynamicSettingControlFactory, PresetStoreLogic, JsonPresetStorage, PresetRestoreOrchestrator, InteractionGuard, SystemClock)
    ↓
Presenters (SlotList / AvatarCatalog / AssignmentFlow / SettingsPanel / PresetManager / TabDiagnostics)
    ↓
Composition Root (CharacterTabBootstrapper)
```

逆方向 import は禁止。Composition Root を除き、Presenter から他 Presenter を直接参照しないこと（State Store / IPC Binder 経由でやり取りする）。

## asmdef 一覧

- `VTuberSystemBase.CharacterSelectionTab.Contracts` (`Runtime/Contracts`): IPC topics + payload DTO。`CoreIpc.Abstractions` のみ参照、エンジン非依存。
- `VTuberSystemBase.CharacterSelectionTab.Runtime` (`Runtime/`): View / State / Services / Presenters / Composition Root。`CoreIpc.Abstractions`, `UiToolkitShell.Runtime`, `UiToolkitShell.CommonUi`, `Unity.Addressables` を参照。
- `VTuberSystemBase.CharacterSelectionTab.Tests.Runtime` (`Tests/Runtime`): EditMode テストとテストダブル群。`UNITY_INCLUDE_TESTS` 限定。
- `VTuberSystemBase.CharacterSelectionTab.Editor` (`Editor/`): Editor 補助。Editor 限定。

## 禁止される依存

`output-renderer-shell` 実装、他タブ spec 実装、`core-ipc-foundation` 具体実装、RAC 本体への直接 C# 参照は禁止。これらは IPC コントラクトの先で別 spec / 別 asmdef に閉じ込められる。

## ui-toolkit-shell との統合

`ui-toolkit-shell` 側 `UiToolkitShellSkinProfile` の `CharacterTabVisualTreeAsset` および `CharacterTabStyleSheets` に本パッケージ同梱の `CharacterTab.uxml` / `CharacterTab.uss` を参照させる運用。

## DefaultAvatarThumbnail のセットアップ

`Runtime/View/DefaultAvatarThumbnail.png` は `AvatarThumbnailResolver` がアバター固有サムネイル解決失敗時にフォールバックとして使用する Sprite。利用者プロジェクトは下記 2 点を行うこと:

1. Unity Editor の Addressables Groups でこの Sprite を `CharacterTabConfig.DefaultThumbnailAddressableKey`（既定 `vtuber-system-base/character/default-avatar-thumbnail`）として登録する。
2. ビルド後 / PlayMode 開始時に `CharacterTabBootstrapper` が `DefaultThumbnailValidator` 経由で 1 回のみプローブロードを行う。失敗時は `IDiagnosticsLogger` にエラーが記録され、診断パネルからも判別可能となる。

異なる固定キーを採用したい場合は `CharacterTabConfig` を override したインスタンスを `CharacterTabBootstrapper` のコンストラクタ引数に渡す（Addressables 側の登録キーも合わせて変更すること）。

## PlayMode サンプル

PlayMode 上で本タブのフル機能（プレイヤーカード描画 → アバター選択 → 割当確定 → 設定スライダー操作 → プリセット切替）を手動確認するための補助コンポーネントとして `CharacterTabPlayModeSampleDriver`（`Tests/Runtime/CharacterTabPlayModeSampleDriver.cs`）を同梱している。`UNITY_INCLUDE_TESTS` 限定で有効。

### サンプルシーン作成手順

1. Unity エディタの Hierarchy で空シーンを作成し `Assets/Tests/Scenes/CharacterTabPlayModeSample.unity` として保存する（ローカル検証用、リポジトリには含めない）。
2. `GameObject > UI Toolkit > UI Document` を追加する。
3. UIDocument の `Source Asset` には未指定でも可。利用者プロジェクトの PanelSettings を割り当てる。
4. 同 GameObject に `CharacterTabPlayModeSampleDriver` を AddComponent する。
5. Driver の `Tab Uxml` フィールドに本パッケージ同梱の `Runtime/View/CharacterTab.uxml` をドラッグ。
6. `Style Sheets` リストに同梱の `Runtime/View/CharacterTab.uss` を追加。USS 差し替えを試したい場合は利用者プロジェクト側の追加 USS を後段に積む。
7. PlayMode を実行するとモックの 3 Slot + 3 アバターが表示され、カード→候補→Activate→設定スライダーの一連を実行可能。

### USS スキン差し替え検証

- `UiToolkitShellSkinProfile.CharacterTabStyleSheets` に利用者 USS を加え、PlayMode で再起動し配色や余白の上書きが反映されることを確認する。
- 上書きが反映されない場合は USS の specificity（`vsb-` prefix の階層）と読み込み順を確認する。

### PlayMode 反復検証

`PresetFlushHookTests.BootstrapperIteration_5Times_NoResourceLeak` で構築 → Activate → Deactivate → Dispose を 5 回反復し、購読・Addressables ハンドル・Track 済み資源のリークが発生しないことを自動検証している。手動でも PlayMode 開始/停止を 5 回繰り返し、Console に Init.Disposed が 5 回出ることを確認する。
