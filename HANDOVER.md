# セッション引き継ぎノート

## ◯ 今回やったこと（Wave 3d 統合 Bootstrap セッション）

- `docs/integration-plan.md` の Wave 3a → 3b → 3c → 3d をほぼ完了
- **Wave 3a**: 3 タブ Contracts asmdef 切り出し（character / stage / camera）
- **Wave 3b**: 3 タブ UI 実装（character / stage / camera タブ Bootstrapper、View、Service、Domain）
- **Wave 3c**: 3 メイン出力アダプタ実装（rac / stage-lighting-volume / camera-switcher の output-adapter）
- **Wave 3d**: 統合 Bootstrap として **`jp.hidano.vtuber-system-base.integrated-demo`** 新パッケージを起票・実装
  - `IntegratedDemoBootstrap` MonoBehaviour: シーン 1 つに置けば `Awake` で全コンポーネントを構築
  - `CoreIpcBusProvider` MonoBehaviour: `CoreIpcRuntime.Current.Bus` を 3 アダプタの異なる `ICoreIpcBusProvider` interfaces に同時提供
  - `IntegratedTabMountStrategy`: UXML clone + `NotifyTabMounted`、shell 起動完了後の `IntegratedTabBootstrapperLauncher` でタブ Bootstrapper 構築（design.md L676 規約遵守）
  - `IntegratedDemoUiShellHost`: `UiShellLifecycleDriver.Configure` 経由で SkinProfile / IPC bus を inject
  - PlayMode スモークテスト 2 件、`Samples~/IntegratedDemo/README.md` に手動シーン構築手順 + L7 受け入れチェックリスト
- `docs/integration-plan.md` を Wave 3d 完了状況で更新
- 累計 ~177 commits（基盤 → タブ → アダプタ → 統合）

### Wave 3d 統合 Bootstrap の設計上の発見

1. **`ICoreIpcBusProvider` が 3 種類存在**: rac (`CoreIpcBus` プロパティ), stage (`Bus` プロパティ), camera (interface 無し、`InjectForTesting(bus, dispatcher, sceneRoots)` で直接渡す)。1 つの MonoBehaviour で 2 interface を multi-inherit + 直接注入で対処。
2. **camera adapter の Awake 競合**: `CameraSwitcherOutputAdapterBootstrapper` は `Awake` で即時 `TryStart` し、Dispatcher が null のとき `deferring` 警告を出して再試行 API が無い。よって `OutputSceneBootstrapper.Diagnostics == Complete` を待ってから **inactive な child GameObject に `AddComponent` → `InjectForTesting` → `SetActive(true)`** の順で生成する必要がある。
3. **UI shell 内の `ITabMountStrategy.MountTabs` は CommandClient が **まだ null** の段階で呼ばれる**。つまり mount 段階でタブ Bootstrapper は構築できない。design.md L676「各タブ spec は UiShellBootstrapper 起動後に RegisterTab を呼ぶ」が正規。Strategy では UXML clone + `NotifyTabMounted` のみ実施し、shell 起動完了後に外側の Launcher でタブ Bootstrapper を構築する。
4. **stage tab Bootstrapper は内部で `RegisterTab` を呼ぶ**ため Launcher 側では呼ばない。character / camera は外から handle を渡す API なので Launcher で `RegisterTab` する。`NotifyTabMounted` は Strategy 側で先行実施しても stage Bootstrapper 内の再呼出は no-op で衝突しない（first state wins）。
5. **`UiToolkitShellSkinProfile` は `RootVisualTreeAsset` 必須 ScriptableObject**: SkinProfile アセット自体と UXML は Unity Editor で手動作成必須。Inspector に SkinProfile を割り当てない場合は UI shell 起動を skip し、メイン出力のみで起動する fail-safe を持たせた。

## ◯ 決定事項

- **Wave 3d 統合は新規パッケージ `jp.hidano.vtuber-system-base.integrated-demo` で実装**: `Assets/Scenes/IntegratedDemo/` ではなく UPM パッケージとして提供。他プロジェクトに `manifest.json` 経由で取り込み可能。
- **`.unity` シーンアセットは Agent 生成不可**: binary YAML のため、IntegratedDemoBootstrap MonoBehaviour 1 つで全結線するプログラマティック設計とし、手動シーン構築手順を README にまとめた。
- **3 タブの ITabMountStrategy 結線**: design.md L676「タブ spec は shell 起動後に RegisterTab」を遵守して、UXML mount と Bootstrapper 構築の 2 段階に分離。
- **Camera adapter は child GameObject に inactive で生成**: Awake → TryStart の即時実行を回避するため、InjectForTesting 完了後 SetActive(true) で起動順序を制御。
- **RAC Host への bus 注入は reflection**: `_outputSceneBootstrapper` / `_coreIpcBusProviderBehaviour` は private SerializeField なので Inspector 配線想定。コードからは reflection で binding。
- **SkinProfile 未設定時は UI shell skip**: メイン出力側のみ立ち上がる fail-safe 経路を確保（Display 2 配信は可能、操作 UI のみ消える）。

## ◯ 捨てた選択肢と理由

- **「シーンアセット (.unity) を YAML で生成」** → Unity binary YAML は GUID 衝突や Component 順序が複雑で Agent 生成は危険。プログラマティック Bootstrap + README で代替。
- **「`Assets/Scenes/IntegratedDemo/` に Editor スクリプトで配置」** → AssetDatabase / SceneView 操作は Unity Editor が走っていないと動かない。Agent 環境で完結させるため UPM パッケージに集約。
- **「ITabMountStrategy 内でタブ Bootstrapper を直接構築」** → `MountTabs` 呼出時点では `CommandClient` 等のサブシステムが null。design.md 規約に反する。Launcher への 2 段階分離が正解。
- **「`OutputSceneBootstrapper` 自身を整理して `ICoreIpcBus` を private SerializeField から expose」** → 他パッケージへの書込み禁止ルール。リファクタは別 PR で。reflection と OverrideServices で対処。
- **「`UiShellBootstrapper` の独自実装で SkinProfile 必須を回避」** → 保守負担大。`SkinProfile` 必須を尊重し、未設定時は UI 起動 skip の fail-safe を提供。
- **「Camera adapter の `_autoStart` を SerializeField でコード経由設定」** → SerializeField は private で外部設定不可。inactive GameObject パターンが現実解。

## ◯ ハマりどころ

- **3 アダプタで `ICoreIpcBusProvider` interface が別々に定義されている**: 同一 MonoBehaviour で multi-inherit する形で実装し、camera は `InjectForTesting` 経由で別系統。Wave 3c 着手時の design レビューで統一しておけば良かった反省点。今後の Wave で 3 アダプタを共通の `Internal/ICoreIpcBusProvider.cs` に統一するリファクタが残課題。
- **camera adapter の Awake 即時 TryStart**: 単独テストでは `InjectForTesting` を Awake 前に呼べる前提だが、AddComponent 経由だと Awake が同期実行されるため inactive GameObject パターン必須。これは camera adapter spec 側の設計（_autoStart の Inspector 専用化）からくる制約。
- **UiShellBootstrapper の MountTabs フェーズで CommandClient が null**: design.md を読んで初めて気付いた。「MountTabs はあくまで UXML attach フェーズで、タブ Bootstrapper 構築は別段階」を理解するまで悩んだ。

## ◯ 学び

- **Bootstrap 系コンポーネントは「同 GameObject で AddComponent」前提で設計されているか「inactive GameObject + 注入」が必要かを Awake 内コードで判別すべき**。OutputSceneBootstrapper / RAC Host / Stage Adapter は前者、Camera Adapter は後者の例。
- **`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` で起動する core-ipc-foundation の RuntimeBootstrap は Awake より先に走る**ため、`CoreIpcRuntime.Current.Bus` は通常 Bootstrap.Awake 時点で利用可能。テスト時は `DisableAutoBootstrap` 必須。
- **`UiToolkitShellSkinProfile.RootVisualTreeAsset` 必須は実装上譲れない**ため、SkinProfile を Inspector で必須割当する README 設計が現実解。Tab UXML は欠けても placeholder で fallback。
- **`[DefaultExecutionOrder]` は MonoBehaviour Start 順制御に有効**。RAC Host が `[DefaultExecutionOrder(100)]` で OutputSceneBootstrapper.Start (default 0) より後に走る設計が見事。

## ◯ 次にやること（Unity Editor 起動後の手動検証フェーズ）

### P0（最優先・Unity Editor 必要）

- **手動シーン構築**: `Assets/Scenes/MainDemo.unity` を `jp.hidano.vtuber-system-base.integrated-demo/Samples~/IntegratedDemo/README.md` の手順通りに作成
  - 空 GameObject に `IntegratedDemoBootstrap` + `OutputSceneBootstrapper` を AddComponent
  - `IntegratedDemoSkinProfile.asset` を作成し、Root + 3 タブの UXML を割り当て
  - PlayMode で動作確認
- **L6 PlayMode 検証**: README §L6 のチェックリストを通す
  - Display 1 にタブバー + 3 タブ UI が表示
  - タブ切替が `style.display` のみで遷移
  - Character / Stage / Camera タブの基本操作 → IPC → Display 2 反映
- **L7 Standalone ビルド検証**: README §L7 のチェックリストを通す
  - 実 2 画面 + OBS Display Capture
  - Spout + OBS Spout Source（任意）
- **コンパイル確認**: Unity を起動して `jp.hidano.vtuber-system-base.integrated-demo` のコンパイルエラー有無を確認。エラー時は HANDOVER に追記して停止。

### P1（中優先・残オプションタスク）

- output-renderer-shell の残 1 タスク（observability ログ拡張）
- 各 spec の手動受け入れテスト（spec ごとの README 参照）
- `docs/integration-plan.md` §7.2 オープンイシューの解消（OSC ポート最終確定、Addressables Group 構成）
- 3 アダプタの `ICoreIpcBusProvider` interface 統一リファクタ（共通 internal asmdef に集約）

### P2（低優先・スコープ外）

- Wave 4: PVW/PGM、WebUI、タイムライン録画リプレイ
- 9 パッケージの OpenUPM / npm registry 公開

### P3（既知の制約）

- camera adapter の auto-start 設計は Inspector 配線専用なので、コードからの起動順序制御に inactive GameObject パターンが必要（既に IntegratedDemoBootstrap 側で対処済み）
- 3 アダプタの `ICoreIpcBusProvider` interface が別々（rac / stage は別 namespace、camera は interface なし）。 統合 Bootstrap で 1 MonoBehaviour に集約しているが、将来は共通化推奨

## ◯ 関連ファイル

### 今回のセッションで新規作成（Wave 3d 統合）

- `VTuberSystemBase/Packages/jp.hidano.vtuber-system-base.integrated-demo/`
  - `package.json` — 9 パッケージへの依存
  - `Runtime/IntegratedDemoBootstrap.cs` — シーン 1 つで全結線する MonoBehaviour（メインエントリ）
  - `Runtime/IntegratedDemoConfig.cs` — Inspector 用 SkinProfile / OSC / preset path 設定
  - `Runtime/CoreIpcBusProvider.cs` — `CoreIpcRuntime.Current.Bus` を 3 アダプタの 2 種 ICoreIpcBusProvider に提供
  - `Runtime/IntegratedTabMountStrategy.cs` + `IntegratedTabBootstrapperLauncher` — UI shell ITabMountStrategy + 後段タブ起動
  - `Runtime/IntegratedDemoUiShellHost.cs` — UiShellLifecycleDriver Configure + LaunchTabBootstrappers の集約
  - `Runtime/VTuberSystemBase.IntegratedDemo.Runtime.asmdef`
  - `Tests/PlayMode/IntegratedDemoSmokeTests.cs` — Awake で例外なく結線するスモーク
  - `README.md` — 完了判定 §8 マッピング + L7 チェックリスト
  - `Samples~/IntegratedDemo/README.md` — 手動シーン構築の詳細手順 + Sample UXML 構造

### 前セッションで作成済（Wave 3a〜3c）

- 9 パッケージの asmdef / package.json / Runtime / Tests
  - core-ipc-foundation, output-renderer-shell, ui-toolkit-shell（基盤 3）
  - character-selection-tab, stage-lighting-volume-tab, camera-switcher-tab（タブ UI 3）
  - rac-main-output-adapter, stage-lighting-volume-output-adapter, camera-switcher-output-adapter（メイン出力アダプタ 3）

### 参照（既存）

- `docs/integration-plan.md` — 統合開発計画（v1.0、Wave 3d 完了状況反映済み）
- `docs/requirements.md` — VTuberSystemBase 要件定義書（RDS の記述は古い、注意）
- `docs/spec-breakdown.md` — kiro spec 切り分け計画（v1.0、初版）
- `.kiro/specs/{character-selection-tab,stage-lighting-volume-tab,camera-switcher-tab}/design.md` — タブ実装の根拠
- `.kiro/specs/{rac-main-output-adapter,stage-lighting-volume-output-adapter,camera-switcher-output-adapter}/design.md` — アダプタ実装の根拠
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Runtime/Abstractions/` — Contracts asmdef のテンプレート（参照 GUID: `286be82527bb75547a774598be8243ab`）
- `VTuberSystemBase/Library/PackageCache/com.hidano.runtime-display-selector@406b0084630f/` — Wave 3e で使う RDS v0.1.1（Facade、Spout、Persistence、Win32 全完備）

## ◯ 完了判定 (`docs/integration-plan.md` §8) の現状

| # | 条件 | 状況 |
| --- | --- | --- |
| 1 | Wave 3b〜3c の 6 spec が tasks の必須項目を緑にしている | **構造的に達成** (オプション/Editor 要のみ残) |
| 2 | Wave 3e の RuntimeDisplaySelector 連携が組み込まれ、Spout / 物理経路の双方で出力できる | **達成** (`OutputSceneBootstrapper.RoutingProvider` で切替可) |
| 3 | `MainDemo.unity` を PlayMode で起動するだけで Display 1 に 3 タブ UI、Display 2 にキャラ + ステージ + ライト + カメラの映像が出る | **手動検証必要** (整備済み Bootstrap + 手順 README で実行可能) |
| 4 | スタンドアロンビルドが配信に載る | **手動検証必要** (Build Settings + Player Settings 手順は README 完備) |
| 5 | 9 パッケージが UPM パッケージとして取込可 | **構造的に達成** (各 `package.json` の dependencies 整備済み) |

> (3) と (4) のみが手動検証 (Unity Editor 起動と実機テスト) を要する状態。それ以外は構造的に揃っている。
