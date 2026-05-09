# jp.hidano.vtuber-system-base.character-selection-tab

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

## PlayMode サンプル

`Tests/PlayMode/CharacterTabPlayModeSample.unity`（task 8.2 で作成）でモック UI シェル構成のフル機能確認が可能。
