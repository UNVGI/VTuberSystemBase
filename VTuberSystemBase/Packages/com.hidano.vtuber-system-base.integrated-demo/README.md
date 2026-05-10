# VTuberSystemBase Integrated Demo (Wave 3d)

`docs/integration-plan.md` の **Wave 3d** ゴール「`MainDemo.unity` 相当の PlayMode 統合シーンで Display 1 UI と Display 2 メイン出力が同一プロセスで往復通信する」を達成するための **プログラマティックな Bootstrap MonoBehaviour と手動 .unity シーン構築 README** を提供するパッケージ。

> Unity の `.unity` シーンアセットは binary YAML で Agent からは生成不可のため、本パッケージはコードによる結線 (`IntegratedDemoBootstrap`) と、手動シーン構築の手順を README として提供する。

## 構成

| ファイル | 役割 |
| --- | --- |
| `Runtime/IntegratedDemoBootstrap.cs` | シーンに 1 つだけ配置する MonoBehaviour 統合エントリ。`Awake` で全コンポーネント (output renderer shell + 3 アダプタ + UI shell + 3 タブ) を構築。 |
| `Runtime/IntegratedDemoConfig.cs` | Inspector で設定する SkinProfile / OSC / Preset パス等の設定オブジェクト。 |
| `Runtime/CoreIpcBusProvider.cs` | `CoreIpcRuntime.Current.Bus` を 3 アダプタに供給する MonoBehaviour。stage / rac の各 `ICoreIpcBusProvider` interface を実装し、camera adapter は `InjectForTesting` 経由で結線する。 |
| `Runtime/IntegratedTabMountStrategy.cs` | UI shell の `ITabMountStrategy` 実装。3 タブ UXML を root に attach し、`NotifyTabMounted` を実行。タブ Bootstrapper 構築は shell 起動完了後の `IntegratedTabBootstrapperLauncher` に委譲。 |
| `Runtime/IntegratedDemoUiShellHost.cs` | `UiShellLifecycleDriver.Configure` への登録、shell 起動完了後の 3 タブ Bootstrapper 起動を集約する静的ヘルパ。 |
| `Samples~/IntegratedDemo/` | 手動 `.unity` シーン構築手順、Inspector 配線手順、L7 受け入れチェックリスト。 |

## 依存パッケージ

`package.json` を参照。Wave 1〜3 の 9 パッケージ (core-ipc-foundation / output-renderer-shell / ui-toolkit-shell / 3 タブ / 3 アダプタ) をすべて取り込む。

## 使い方（手動シーン構築）

1. Unity Editor で新規シーン (例: `Assets/Scenes/MainDemo.unity`) を作成。
2. 空の GameObject `IntegratedDemoRoot` を作成し、本パッケージの `IntegratedDemoBootstrap` をアタッチ。
3. 同じ GameObject に `OutputSceneBootstrapper` (`com.hidano.vtuber-system-base.output-renderer-shell`) を追加。`IntegratedDemoBootstrap.OutputSceneBootstrapper` フィールドに drag & drop。
   - Inspector で `_targetDisplayIndex = 1` (Display 2)、`_routingProvider = BuiltIn` または `RuntimeDisplaySelector` を選択。Spout 出力時は `_spoutSenderName` を設定。
4. `IntegratedDemoBootstrap` の Inspector で `IntegratedDemoConfig` を設定:
   - **SkinProfile** (必須・UI 表示時): `Assets > Create > VTuberSystemBase / UI Toolkit Shell / Skin Profile` で `UiToolkitShellSkinProfile.asset` を作成し、`RootVisualTreeAsset` (タブバー枠 UXML) を必ず割り当てる。3 タブ用 `*TabVisualTreeAsset` も作成して指定すると 3 タブが起動する。空のままだと placeholder が attach され、タブ Bootstrapper 側で Q-find に失敗して `MarkTabFailed` 経由で記録される (UI shell 自体は立ち上がる)。
   - **CameraOscHost / CameraOscPort**: camera-switcher-tab の OSC 送信先。空 `0` で `127.0.0.1` の既定。
   - **CameraPresetPath**: camera-switcher-tab のプリセット保存パス。空で `persistentDataPath/camera-presets.json` の既定。
   - **AdapterStartupMaxFrames**: メイン出力 Bootstrap が立ち上がるのを待つフレーム数 (既定 60)。
5. 必要なら同 GameObject 上の `OutputSceneBootstrapper` の Inspector も調整 (`MinLogLevel`, `FullScreenMode` など)。

> `IntegratedDemoBootstrap` は Awake 時に `CoreIpcBusProvider` / `RacMainOutputAdapterHost` / `StageLightingVolumeOutputAdapterBootstrapper` / `CameraSwitcherOutputAdapterBootstrapper` を **同 GameObject 上に AddComponent** する。Inspector に余分な MonoBehaviour が並んで見えるが正常。

### Standalone ビルド時の Display Capture / Spout 経路

| 配信形態 | 設定 |
| --- | --- |
| 実 2 画面 + OBS Display Capture | `OutputSceneBootstrapper._routingProvider = BuiltIn`、`_targetDisplayIndex = 1`、`_fullScreenMode = FullScreenWindow`。OBS の Display Capture を Display 2 に向ける。 |
| Spout + OBS Spout Source | `_routingProvider = RuntimeDisplaySelector`、`_spoutSenderName = "VsbMainOutput"` (任意)、`com.hidano.runtime-display-selector` v0.1.1 が manifest に取り込み済みであること。OBS の Spout Source で同 Sender 名を選択。 |

### Player Settings

- `Project Settings > Player > Resolution and Presentation > Display Resolution Dialog = Disabled`
- `Run In Background = true`（OBS でフォーカスが外れても描画継続）
- `Allow fullscreen switch = true`

## L7 手動受け入れテスト チェックリスト

`docs/integration-plan.md` §6.1 L7 (スタンドアロン配信) に対応する手動受け入れ項目。Editor PlayMode で先に L6 を確認した後、Standalone ビルドで L7 を行う。

### L6: Editor PlayMode（同一プロセス）

- [ ] `IntegratedDemoBootstrap` を配置したシーンを開いて Play を押す
- [ ] Display 1 (`Game` ビュー) にタブバー + 3 タブが表示される
- [ ] タブバーで Character / Stage / Camera を切り替えると `style.display` のみで遷移し、ログに `Tab.Activated` / `Tab.Deactivated` が出る
- [ ] Character タブでアバターをクリック → `slot/{id}/assignment` が IPC で送信され、`rac-main-output-adapter` が `Slot.Bind` を呼んで Display 2+ にアバターが表示される（Addressables の Avatar Group が必要）
- [ ] Stage タブで Light を `add` → Display 2+ の `LightsRoot` 配下に Light が出現する。Volume Override の編集 → Bloom / Tonemap が反映される
- [ ] Camera タブで OSC アドレスを設定 → `/ucapi/camera/{id}/flat` が camera-switcher-output-adapter で受信される
- [ ] Display 2 の `OutputSceneBootstrapper.Diagnostics.CurrentPhase == Complete`（Console ログで確認）
- [ ] PlayMode を停止 → `UiShellLifecycleDriver.OnPlayModeStateChanged(ExitingPlayMode)` で UI shell が StopShell。Console に解放ログが順次出ること

### L7: Standalone ビルド（実 2 画面）

- [ ] Build Settings で `Assets/Scenes/MainDemo.unity` を Scenes In Build に登録
- [ ] Player Settings の `Run In Background = true`、`Display Resolution Dialog = Disabled` を確認
- [ ] Build & Run。Display 1 に UI、Display 2 にキャラ + ステージ + カメラの映像が出る
- [ ] OBS の Display Capture で Display 2 を取り込み、配信品質を確認
- [ ] (Spout 経路時) OBS の Spout Source で `_spoutSenderName` で設定した Sender 名を選択
- [ ] アプリ終了で全リソースが解放される（process kill 時のリーク無し）

### L7 補足: 動作不良時のチェック

- Display 2 が真っ黒: `_targetDisplayIndex` の値、`Display.displays.Length`、Player Settings の `Display Resolution Dialog` を確認
- IPC が届かない: Console で `CoreIpcRuntime.Current.Bus` が non-null か、各アダプタの `Adapter ready` ログが出ているかを確認
- タブ UI が出ない: `UiToolkitShellSkinProfile.RootVisualTreeAsset` を確認、`UiShellLifecycleDriver.Current?.IsRunning == true` かをログで確認

## 完了判定 (`docs/integration-plan.md` §8)

| 条件 | 状況 |
| --- | --- |
| (1) Wave 3b〜3c の 6 spec が `tasks` の必須項目を緑にしている | **達成（オプション/Editor 要のタスクのみ残）** |
| (2) Wave 3e の RuntimeDisplaySelector 連携が `output-renderer-shell` に組み込まれ | **達成**（`OutputSceneBootstrapper.RoutingProvider = RuntimeDisplaySelector` で切替可） |
| (3) `MainDemo.unity` を PlayMode で起動するだけで Display 1 に 3 タブ UI、Display 2 にキャラ + ステージ + ライト + カメラの映像が出る | **手動検証必要**（本 README §L7 を実施） |
| (4) スタンドアロンビルドが配信に載る | **手動検証必要**（本 README §L7 を実施） |
| (5) 9 パッケージが UPM パッケージとして取込可 | **構造的に達成**（各 `package.json` の dependencies + manifest 経由で取得可） |

## 注意事項

- **SkinProfile を Inspector で割り当てない場合は UI shell の起動を skip する**。メイン出力側のみ立ち上がる挙動になる。Display 1 に何も出ないが Display 2 に映像は出るので OBS 配信は可能（オペレーション UI が無いだけ）。
- **stage タブ UXML が無いと Q-find 失敗で `MarkTabFailed`** に落ちる。タブ実装側の region 名 (`preset-section`, `stage-selection-section` 等) を持つ UXML が必要。各タブの `ViewQueryHelpers.cs` または `*TabPanel.cs` の constants を参照。
- **PlayMode 開始/停止を 5 回繰り返してリーク兆候が無いこと** を `UiShellLifecycleDriver.StartInvocationCount == StopInvocationCount` で確認できる。
- **Standalone での Display Capture と Spout 経路は排他ではなく併用可能**。RDS で複数経路を同時に設定できる。
