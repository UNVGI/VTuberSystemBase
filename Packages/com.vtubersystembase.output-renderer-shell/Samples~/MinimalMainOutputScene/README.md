# Minimal Main Output Scene サンプル

`com.vtubersystembase.output-renderer-shell` のメイン出力シェル単体起動を検証するための最小サンプルです。
`OutputSceneBootstrapper` 1 つだけを配置したシーンを PlayMode で開き、Display 2 への配信向け器
（ルート階層・デフォルトカメラ・デフォルト Light・空の Global Volume・ディスパッチャ・診断）が
Requirement 1〜9 の主要挙動どおりに完成することを確認します。

## サンプルに含まれるもの

- `MainOutput.unity` — `OutputSceneBootstrapper` 1 つのみを配置した最小シーン。
- 本 `README.md` — 手動検証手順と運用ガイダンス。

UPM Samples からインポートする場合：

1. Unity Package Manager で `VTuber System Base Output Renderer Shell` を選択。
2. *Samples* タブから **Minimal Main Output Scene** を *Import* する。
3. インポート先は
   `Assets/Samples/VTuberSystemBase Output Renderer Shell/<version>/Minimal Main Output Scene/`。

## 手動検証手順

### 1. 全フェーズ完了ログの確認（Req 9.1）

1. `MainOutput.unity` を開いて PlayMode 開始。
2. Unity Console で次のログ系列が出力されることを確認：
   - `[Info][OutputShell][OutputSceneBootstrapper] phase complete: create scene roots -> RootsCreated`
   - `[Info][OutputShell][OutputSceneBootstrapper] phase complete: create default camera -> CameraReady`
   - `[Info][OutputShell][OutputSceneBootstrapper] phase complete: create default light -> LightReady`
   - `[Info][OutputShell][OutputSceneBootstrapper] phase complete: create global volume -> VolumeReady`
   - `[Info][OutputShell][OutputSceneBootstrapper] phase complete: ensure ipc server -> IpcServerReady`
   - `[Info][OutputShell][OutputSceneBootstrapper] phase complete: create dispatcher -> DispatcherReady`
   - `[Info][OutputShell][OutputSceneBootstrapper] phase complete: activate display routing -> DisplayRouted`
3. `Diagnostics.CurrentPhase` が `Complete` に到達していること。

### 2. Display 2 のフォールバック挙動確認（Req 2.4 / OR-1）

#### Display 2 が物理接続されている場合

- スタンドアロンビルドで起動：Display 2 にメイン出力が全画面表示されること。
- Editor PlayMode：Game View のみ描画される（`Display.Activate` は Editor 上は no-op、Req 6.8）。

#### Display 2 が接続されていない場合

- `BuiltInDisplayRoutingService` がフォールバックを発火し、次のログが出ること：
  - `[Warning][OutputShell][BuiltInDisplayRoutingService][topic=display-routing] Requested Display index 1 not available (count=1); falling back to Display 0.`
- Diagnostics の `CurrentDisplayAssignment.IsFallbackActive == true`。

### 3. PlayMode 反復クリーンアップ（Req 6.4）

1. PlayMode 開始 → Console ログで `Complete` まで到達するのを確認。
2. PlayMode 停止。Hierarchy に `StageRoot` / `CharactersRoot` / `LightsRoot` / `CamerasRoot` /
   `VolumeRoot` のいずれも残存していないことを確認。
3. 上記を 3 回繰り返してエラーや残存 ScriptableObject が無いことを確認。

## 運用ガイダンス：メイン出力サーフェスへの描画禁止（Req 5.2 / 5.4 / 9.6）

Unity 既定のエラーダイアログ／クラッシュダイアログ／Development Build オーバーレイが
メイン出力（Display 2+）側で表示される可能性があります。配信事故防止のため、Player
Settings で次の点を確認してください。

- **Project Settings → Player → Resolution and Presentation**
  - *Display Resolution Dialog* は `Disabled` にする（昔の Unity でのみ有効）。
- **Project Settings → Player → Other Settings**
  - *Stack Trace Logging*：`Error` を `ScriptOnly` 以外にする場合、Console 経由のエラーが
    Development Build オーバーレイに描画される可能性に注意（Development Build 時のみ）。
  - *Use Player Log* を有効にしてログをファイルへ落とす。
- **Project Settings → Quality → V Sync Count**：Display 2 側 OS が固定リフレッシュレートで
  動作するよう設定（フォールバックで Display 0 と異なるレートになると配信側でカクつく）。
- **Build Settings → Development Build**：本番配信時はチェックを外す。Development Build の
  プロファイラ／ステータスバーがメイン出力に重畳する事案を回避する。

`OutputSceneBootstrapper` は本契約に従い、配下のいかなる GameObject にも以下のコンポーネントを
アタッチしません：

- `OnGUI` を実装する `MonoBehaviour`
- `UnityEngine.UIElements.UIDocument` / `PanelSettings`
- IMGUI ベースのデバッグオーバーレイ

診断は **Unity Console**（`Debug.Log*` 経由）または UI 側 spec（spec #3 ui-toolkit-shell）の
オペレーター UI（Display 1）に転送するのみで、メイン出力サーフェスのピクセルには影響しません。

## 後続 spec が拡張する箇所

- `IOutputSceneRoots.Stage` / `Characters` / `Lights` / `Cameras` / `Volumes` 配下に
  各タブ spec（character-selection-tab / stage-lighting-volume-tab / camera-switcher-tab）が
  Prefab・Light・Override を追加します。
- `IOutputCommandDispatcher.RegisterStateHandler` などを呼び出して IPC コマンドを購読します。
- 本 spec はあくまで **器** を提供するため、サンプル単体での見映えは黒背景の単一 Directional Light が
  当たった空ステージのみとなります。
