# Integrated Demo — Sample Scene Walkthrough

`MainDemo.unity` を再現するための詳細なシーン構築手順と、Skin Profile / UXML / Addressables の最小サンプル仕様を提供する。

## 1. 前提

- Unity 6.3 (6000.3+)
- URP 17.x が有効化されている
- 以下 9 パッケージがプロジェクトに追加されている (manifest.json)：
  - `com.hidano.vtuber-system-base.core-ipc-foundation`
  - `com.hidano.vtuber-system-base.output-renderer-shell`
  - `com.hidano.vtuber-system-base.ui-toolkit-shell`
  - `com.hidano.vtuber-system-base.character-selection-tab`
  - `com.hidano.vtuber-system-base.stage-lighting-volume-tab`
  - `com.hidano.vtuber-system-base.camera-switcher-tab`
  - `com.hidano.vtuber-system-base.rac-main-output-adapter`
  - `com.hidano.vtuber-system-base.stage-lighting-volume-output-adapter`
  - `com.hidano.vtuber-system-base.camera-switcher-output-adapter`
  - `com.hidano.vtuber-system-base.integrated-demo` (本パッケージ)
- 必要な外部依存も追加済み：`com.hidano.realtimeavatarcontroller`, `com.hidano.scene-view-style-camera-controller`, `com.hidano.ucapi4unity`, `com.hidano.uosc`, `com.hidano.runtime-display-selector` (任意), `com.unity.addressables`, `com.unity.render-pipelines.universal`

## 2. SkinProfile アセット作成

Unity Editor で UXML / SkinProfile を作る手順。

1. `Assets > Create > VTuberSystemBase > UI Toolkit Shell > Skin Profile` で `IntegratedDemoSkinProfile.asset` を作成。
2. **Root UXML**: `Assets > Create > UI Toolkit > UI Document` で `IntegratedDemo_Root.uxml` を作成し、以下の構造を入れる：
   ```xml
   <UXML xmlns:ui="UnityEngine.UIElements">
       <ui:VisualElement name="vsb-shell-root" style="flex-grow: 1;">
           <ui:VisualElement name="vsb-tab-bar" />
           <ui:VisualElement name="vsb-tab-host" style="flex-grow: 1;" />
           <ui:VisualElement name="vsb-notification-bar" />
       </ui:VisualElement>
   </UXML>
   ```
   `IntegratedDemoSkinProfile.RootVisualTreeAsset` に `IntegratedDemo_Root.uxml` を assign。
3. **Character Tab UXML**: `IntegratedDemo_CharacterTab.uxml` を作り、以下のような region を持たせる（タブ実装側 `ViewQueryHelpers.cs` 参照）：
   ```xml
   <UXML xmlns:ui="UnityEngine.UIElements">
       <ui:VisualElement name="vsb-char-tab" style="flex-grow: 1;">
           <ui:VisualElement name="vsb-char-tab__player-cards" />
           <ui:VisualElement name="vsb-char-tab__avatar-catalog" />
           <ui:VisualElement name="vsb-char-tab__settings-panel" />
           <ui:VisualElement name="vsb-char-tab__preset-bar" />
           <ui:VisualElement name="vsb-char-tab__diagnostics" />
       </ui:VisualElement>
   </UXML>
   ```
4. **Stage Tab UXML**: `IntegratedDemo_StageTab.uxml`（element 名は `StageLightingVolumeTabPanel.cs` 参照）：
   ```xml
   <UXML xmlns:ui="UnityEngine.UIElements">
       <ui:VisualElement name="vsb-stage-tab" style="flex-grow: 1;">
           <ui:VisualElement name="preview-panel" />
           <ui:VisualElement name="preset-section" />
           <ui:VisualElement name="stage-selection-section" />
           <ui:VisualElement name="light-list-section" />
           <ui:VisualElement name="light-editor-section" />
           <ui:VisualElement name="volume-override-section" />
           <ui:Label name="active-preset-label" />
       </ui:VisualElement>
   </UXML>
   ```
5. **Camera Tab UXML**: `IntegratedDemo_CameraTab.uxml`（element 名は `vsb-cam-tab__*` パターン、タブ実装側 `ViewQueryHelpers.cs` 参照）：
   ```xml
   <UXML xmlns:ui="UnityEngine.UIElements">
       <ui:VisualElement name="vsb-cam-tab" style="flex-grow: 1;">
           <ui:VisualElement name="vsb-cam-tab__preview-active" />
           <ui:VisualElement name="vsb-cam-tab__preview-multi" />
           <ui:VisualElement name="vsb-cam-tab__camera-list" />
           <ui:VisualElement name="vsb-cam-tab__volume-editor" />
           <ui:VisualElement name="vsb-cam-tab__preset-panel" />
           <ui:VisualElement name="vsb-cam-tab__diagnostics" />
       </ui:VisualElement>
   </UXML>
   ```
6. SkinProfile に各 Tab*VisualTreeAsset を assign。USS は任意（無くても起動する）。

## 3. シーン構築

1. `Assets/Scenes/MainDemo.unity` を新規作成。
2. シーンに空 GameObject `IntegratedDemoRoot` を追加。
3. `IntegratedDemoRoot` に以下を `Add Component` する **（順序は問わない）**：
   - `IntegratedDemoBootstrap`
   - `OutputSceneBootstrapper` （`com.hidano.vtuber-system-base.output-renderer-shell`）
4. `IntegratedDemoBootstrap` の Inspector で：
   - `Output Scene Bootstrapper` フィールドに同 GameObject の `OutputSceneBootstrapper` を assign。
   - `Config > Skin Profile` に手順 2 で作成した `IntegratedDemoSkinProfile.asset` を assign。
   - `Config > Camera Osc Host = 127.0.0.1`、`Camera Osc Port = 9000`（uOSC 既定）。
   - `Config > Camera Preset Path` は空のままで OK（persistentDataPath 既定）。
5. `OutputSceneBootstrapper` の Inspector で：
   - `Target Display Index = 1`（Display 2）
   - `Routing Provider = BuiltIn` または `RuntimeDisplaySelector`
   - Spout 経路を使う場合: `Spout Sender Name = VsbMainOutput`
6. シーンを保存。

## 4. Addressables Group の最小構成

3 タブのうち character / stage 系は Addressables から asset を読む。手動検証時は最小サンプルとして以下を用意する：

### Character (`rac-main-output-adapter` 連携)

- Group: `Avatars`
  - VRM Avatar Prefab を 1 体追加し、Addressable Address に `avatars/sample-avatar` を設定
  - Avatar Schema JSON (任意) を `avatars/sample-avatar/schema` で登録

### Stage (`stage-lighting-volume-output-adapter` 連携)

- Group: `Stages`
  - 単純な Stage Prefab (Cube + Plane) を `stages/sample-stage` で登録

### Thumbnail (任意・character タブ既定値)

- Address: `vtuber-system-base/character/default-avatar-thumbnail`
- 64x64 程度の Texture2D

## 5. PlayMode 起動

シーンを Play した直後に以下が出れば成功：

1. Console: `[CoreIpc.RuntimeBootstrap] CoreIpcRuntime initialization completed.`
2. Console: `[IntegratedDemoBootstrap] Awake wiring complete (PlayMode integration scaffold ready).`
3. Console: `OutputSceneBootstrapper: phase complete: ... -> Complete`
4. Console: `[RacMainOutputAdapterHost] Initialize complete` または相当
5. Console: `[CameraSwitcherOutputAdapter] Camera Switcher Output Adapter ready`
6. Console: `[StageLightingVolumeOutputAdapterBootstrapper] ready` 系
7. Console: `UiShellBootstrapper: shell running.`
8. Display 1 にタブバー + 3 タブ UI が出る (Character タブが初期 active)
9. Display 2+ にメイン出力（既定はカメラ + 既定ライトのみ。アバターやステージは IPC 経由で表示）

## 6. トラブルシュート

| 症状 | 原因 | 対処 |
| --- | --- | --- |
| Display 1 に何も出ない | SkinProfile 未設定 | Inspector で IntegratedDemoConfig.SkinProfile に asset を assign |
| `MarkTabFailed: ... Q failed` | タブ用 UXML に必須 region 名が無い | `*TabPanel.cs` / `ViewQueryHelpers.cs` の constants を参照して UXML を修正 |
| Console に `CoreIpcRuntime.Current is null` | RuntimeBootstrap.OnBeforeSceneLoad が走っていない（テスト時に DisableAutoBootstrap 経由で抑止） | テストビルド以外では起きない。Production の起動経路を確認 |
| Camera adapter `OutputSceneBootstrapper not initialized yet; deferring` | OutputSceneBootstrapper.Start が完了する前に CameraAdapter.Awake が走った | `IntegratedDemoBootstrap` を OutputSceneBootstrapper と同 GameObject に置き、IntegratedDemoBootstrap が先に Awake する Execution Order に設定する |
| Stage adapter `dependencies_missing` | OutputSceneBootstrapper.Dispatcher が null | `IntegratedDemoBootstrap.AdapterStartupMaxFrames` を増やすか OutputSceneBootstrapper の Awake/Start が走っているか Console で確認 |

## 7. 完了判定

`docs/integration-plan.md` §8 (1)〜(5) を README で satisfy 確認のうえ、L7 手動受け入れテストを通せば本 Wave 3d は完了。
