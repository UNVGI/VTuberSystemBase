# Implementation Plan — ui-toolkit-shell

本計画は TDD（Red → Green → Refactor）を基本サイクルとし、Foundation → Core → Integration → Validation の順で進行する。各タスクは 1〜3 時間で完了する粒度に分割し、全 Requirement（1.1〜11.9）と design.md の全コンポーネントに対応する。`(P)` が付与されたタスクは、先行 Foundation の成果物が揃っている前提で、境界が重複しない同一親内の他タスクと並行実行可能である。

---

## 1. Foundation: パッケージ骨格とアセンブリ境界の確立

- [x] 1.1 パッケージディレクトリと Runtime 向け asmdef を作成する
  - `Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/` 配下に Runtime / Runtime.UxmlUss / Runtime.CommonUi / Editor / Tests のルートフォルダを用意する
  - `UiToolkitShell.Runtime.asmdef` を作成し、参照先を UI Toolkit（`UnityEngine.UIElements`）、Unity Addressables、`core-ipc-foundation` の抽象 asmdef に限定する
  - `core-ipc-foundation` 具体実装 asmdef・`output-renderer-shell`・タブ spec への参照が引けない構成であることを asmdef 設定画面で確認する
  - 観測可能な完了状態: Unity Editor を開いた際に 5 つのフォルダと Runtime asmdef が認識され、Console に参照エラーが出ない
  - _Requirements: 1.5, 5.8, 5.10_

- [x] 1.2 共通 UI ライブラリと Editor / Tests 用 asmdef を分離して作成する
  - `UiToolkitShell.CommonUi.asmdef` を Runtime.CommonUi 配下に作成し、参照先を UI Toolkit のみに絞る
  - `UiToolkitShell.Editor.asmdef` を `includePlatforms = Editor` で作成し、Runtime asmdef を参照する
  - `UiToolkitShell.Tests.asmdef` を Tests 配下に作成し、Runtime / CommonUi / Editor asmdef と Unity Test Framework（`nunit.framework`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`）を参照する
  - 観測可能な完了状態: `タブ spec asmdef → CommonUi → Runtime → core-ipc-foundation 抽象` の一方向参照チェインが成立し、逆方向参照が asmdef エラーで拒否される
  - _Requirements: 1.5, 5.8, 5.10, 7.5, 10.5_

- [x] 1.3 package.json とライセンス/バージョン情報を整備する
  - `package.json` に feature 名・Unity 最低バージョン（6.3）・依存（`com.unity.addressables` 2.x, `jp.hidano.vtuber-system-base.core-ipc-foundation` 抽象）を記述する
  - `CHANGELOG.md` の雛形を置く（本タスクは実装メタデータのみ、機能コード生成を伴わない）
  - 観測可能な完了状態: Package Manager ウィンドウでローカルパッケージとして認識され、依存解決が成功する
  - _Requirements: 1.5, 7.5_

## 2. Foundation: テスト基盤とフェイク実装の整備

- [x] 2.1 IPC 抽象向けのフェイククライアント（`FakeIpcClient`）とメインスレッドディスパッチヘルパをテスト側に用意する
  - `core-ipc-foundation` の `ICoreIpcClient` 抽象に対するテストダブルを実装し、接続状態・送信結果・受信注入・相関 ID マッチングを制御可能にする
  - ディスパッチ周りの検証のため `Thread.CurrentThread.ManagedThreadId` を記録するユーティリティを共通化する
  - フェイクの使用例として 1 件のスモークテスト（起動→送信→受信 round-trip）を追加し成功させる
  - 観測可能な完了状態: Tests asmdef のテストランナーで `FakeIpcClient` を利用したスモークテストが緑になる
  - _Requirements: 10.6, 10.3_

- [x] 2.2 Addressables 抽象向けのフェイクローダ（`FakeAsyncAssetLoader`）をテスト側に用意する
  - `IAsyncAssetLoader` 抽象に対して、即時完了・遅延完了・任意失敗注入・キャンセルを制御可能なフェイク実装を追加する
  - スナップショット（`AssetLoaderSnapshot` 相当）の差替えも可能にする
  - 観測可能な完了状態: フェイクローダを使った 1 件のサンプルテスト（成功と失敗注入）が緑になる
  - _Requirements: 10.7, 4.7, 4.9_

- [x] 2.3 PlayMode 手動検証用の最小シーン `UiShellPlayModeSample` をテストアセットとして置く
  - Tests/PlayMode 下に `UiShellPlayModeSample.unity` を作成し、空の GameObject 1 つを配置する（具体コンポーネント割当はタスク 9 で実施）
  - 観測可能な完了状態: Unity Editor で当該シーンを開くとエラーなく読み込める
  - _Requirements: 10.4_

## 3. Core: 診断ロガーと診断スナップショット（最内周依存）

- [x] 3.1 (P) `IDiagnosticsLogger` 契約と `LogLevel` / `LogCategory` 列挙のテストを先に書く
  - `MinimumLevel` を下回るログが出力されないこと、カテゴリ別のメッセージが正しく配信されることを検証するテストを追加する
  - Red 状態のテストが「型未定義」で失敗することを確認する
  - 観測可能な完了状態: テスト 4〜6 件が「型未定義」で失敗する（Red）
  - _Requirements: 11.8_
  - _Boundary: Diagnostics_

- [x] 3.2 (P) 診断ロガーの実装と最小実装で Green にする
  - `IDiagnosticsLogger` / `DiagnosticsLogger` を実装し、Unity Console への `Debug.Log*` 出力と、UI 側診断領域用の in-memory リングバッファを両方持つ
  - Display 2+ への出力経路が存在しないことをコード構造で保証する（サーフェス描画 API を呼ばない）
  - 観測可能な完了状態: 3.1 のテストが全て緑になり、`MinimumLevel` 変更で出力制御できる
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8_
  - _Boundary: Diagnostics_
  - _Depends: 3.1_

- [x] 3.3 `ShellDiagnosticsSnapshot` / `IShellDiagnosticsSnapshotProvider` を TDD で実装する
  - プリロード進捗・非同期ロード件数・IPC 接続状態・アクティブタブ・購読数を集約 struct として返すテストを先に書く
  - 各サブシステムがまだ未実装の段階では、依存を注入可能にしてフェイクで検証する
  - 観測可能な完了状態: `Capture()` 呼出しが即時に参照透過な struct を返し、サブシステム差替えで値が変化するテストが緑
  - _Requirements: 3.7, 4.9, 11.9_
  - _Boundary: Diagnostics_

## 4. Core: IPC 送信・購読 Facade と接続状態

- [x] 4.1 (P) `IConnectionStatus` の契約テストを先に書く
  - `Initializing → Connecting → Connected → Disconnected → Reconnecting → FailedPermanently` の状態遷移を `FakeIpcClient` 経由で再現するテストを追加する
  - `OnStatusChanged` がメインスレッドで発火することを 2.1 のディスパッチヘルパで検証する
  - 観測可能な完了状態: 遷移テスト 5 件以上が Red 状態で失敗する
  - _Requirements: 5.9, 9.3, 9.5, 11.6_
  - _Boundary: Commands/ConnectionStatus_

- [x] 4.2 (P) `IConnectionStatus` 実装と状態反映を Green にする
  - `ConnectionStatusCode` enum・`ConnectionStatusEvent` struct と状態保持ロジックを実装する
  - `core-ipc-foundation` の接続イベントからの一方向変換（Adapter）を実装する
  - 観測可能な完了状態: 4.1 の遷移テストが全て緑、`IsConnected` の読取がメインスレッド発火の遷移通知と整合する
  - _Requirements: 5.9, 9.3, 9.5, 11.6_
  - _Boundary: Commands/ConnectionStatus_
  - _Depends: 4.1_

- [x] 4.3 `IUiCommandClient` の契約テストを先に書く
  - `PublishState` / `PublishEvent` の即時 `SendResult` 返却、`RequestAsync` の非同期 `RequestResult<TResponse>` 返却、接続未確立時 `SendError.NotConnected` 即時返却、topic バリデーション違反時 `TopicInvalid`、タイムアウト時 `RequestError.Timeout` を検証するテストを追加する
  - 失敗時に例外を外に投げない（UI クラッシュしない）契約も明示的に検証する
  - 観測可能な完了状態: テスト 8〜10 件が Red 状態
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.9, 9.4_
  - _Boundary: Commands/UiCommandClient_

- [ ] 4.4 `UiCommandClient` を実装して Green にし、送信ログを発行する
  - `SendResult` / `SendError` / `RequestResult<T>` / `RequestError` を実装し、`ICoreIpcClient` 抽象へ委譲する
  - Topic 文字種バリデーション（ASCII 英数 + `/` / `-` / `_`）を実装する
  - 送信成否・相関 ID・topic を `DiagnosticsLogger` へ `LogCategory.Ipc` で出力する
  - 直接トランスポート（WebSocket）呼び出し経路を asmdef 参照で不可能にする構造を維持する
  - 観測可能な完了状態: 4.3 のテストが全て緑、送信 1 回につき 2 件のログ（Started / Result）が Console へ出力される
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.9, 5.10, 9.3, 9.4, 11.4_
  - _Boundary: Commands/UiCommandClient_
  - _Depends: 4.3_

- [ ] 4.5 `IUiSubscriptionClient` の契約テストを先に書く
  - `Subscribe(topic, kind, callback)` で `ISubscriptionToken` を返し、`Dispose` 後は callback が呼ばれないことを検証する
  - callback がメインスレッドで発火することを検証する
  - callback 内の例外が他購読に波及せず、ログに記録されることを検証する
  - 観測可能な完了状態: テスト 5〜7 件が Red 状態
  - _Requirements: 5.6, 5.7_
  - _Boundary: Commands/UiSubscriptionClient_

- [ ] 4.6 `UiSubscriptionClient` を実装して Green にし、受信ログを発行する
  - `MessageEnvelope<TPayload>` / `MessageKind` / `ISubscriptionToken` を実装する
  - `ICoreIpcClient.Subscribe` を内包し、callback 実行を try-catch で保護する
  - 到着メッセージの種別・topic を `DiagnosticsLogger` へ `LogCategory.Ipc` で出力する
  - 観測可能な完了状態: 4.5 のテストが全て緑、購読中のメッセージ 1 件ごとに 1 件の受信ログが出る
  - _Requirements: 5.6, 5.7, 5.8, 11.5_
  - _Boundary: Commands/UiSubscriptionClient_
  - _Depends: 4.5_

## 5. Core: Addressables 非同期ロード基盤

- [ ] 5.1 `IAsyncAssetLoader` の契約テストを `FakeAsyncAssetLoader` で先に書く
  - `LoadAsync<T>(key, scopeId, callback)` が handle を即時返却すること、Completion は `callback` 経由のみで受け取ること、メインスレッド発火であることを検証する
  - 同一 key の重複 `LoadAsync` が 1 本のハンドルに集約され両 callback が呼ばれること（4.7）を検証する
  - `Release` / `ReleaseAll(scopeId)` / `Cancel` の挙動、失敗時の `LoadError` 伝搬（4.4）、`GetSnapshot()` の件数整合性を検証する
  - 観測可能な完了状態: テスト 10 件前後が Red 状態
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.6, 4.7, 4.8_
  - _Boundary: AssetLoading_

- [ ] 5.2 `AddressablesAssetLoader` を実装し、`WaitForCompletion` を使わず Green にする
  - Addressables `LoadAssetAsync<T>` + `AsyncOperationHandle<T>.Completed` を用いた実装を追加する
  - ハンドルキャッシュで同一 key の重複ロードを抑止する
  - scopeId 単位での Release 一括管理を実装する
  - 同期ブロッキング API（`WaitForCompletion`）を一切呼ばないことをコードレビュー観点に含める
  - 進行中件数・失敗件数を `ShellDiagnosticsSnapshot` に露出する
  - 観測可能な完了状態: 5.1 のテストが全て緑、100 件の並列ロード要求で UI スレッドのフレーム時間が 16.67ms を超えない（簡易計測）
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.6, 4.7, 4.8, 4.9, 11.3_
  - _Boundary: AssetLoading_
  - _Depends: 5.1_

- [ ] 5.3 Addressables 初期化ブートストラップと `BootstrapErrorCode.AddressablesInitFailed` 伝搬をテスト付きで実装する
  - `Addressables.InitializeAsync()` を包み、失敗時にエラーコード返却パスを用意する
  - ロード開始/完了/失敗/アンロードの各イベントを `DiagnosticsLogger` へ `LogCategory.AssetLoad` で出力する
  - 観測可能な完了状態: 初期化失敗注入時に `BootstrapErrorCode.AddressablesInitFailed` が返り、シェルが安全に起動中断するテストが緑
  - _Requirements: 4.1, 11.3_
  - _Boundary: AssetLoading_

## 6. Core: スキン差し替え拡張点と検証

- [ ] 6.1 (P) `UiToolkitShellSkinProfile` ScriptableObject を TDD で定義する
  - ルート / 3 タブ / 共通 UI の `VisualTreeAsset` と `List<StyleSheet>` を保持するフィールド構成をテストで固定する
  - `CreateAssetMenu` によりプロジェクトから生成できることを確認する
  - 既定スキン（`DefaultSkinProfile.asset`）を Runtime.UxmlUss 配下に同梱し、必須フィールドが埋まっていることを検証する
  - 観測可能な完了状態: Editor メニューから SO が作成でき、空プロファイルに対して `BootstrapErrorCode.SkinProfileMissing` が返るテストが緑
  - _Requirements: 6.3, 6.4, 6.7, 6.8_
  - _Boundary: Skin/SkinProfile_

- [ ] 6.2 (P) USS セレクタ命名規約（`vsb-` プレフィクス + BEM 風）と必須クラス一覧を定数化する
  - `SkinValidationRules` 静的クラスに必須セレクタ一覧を列挙する（例: `vsb-tab-root`, `vsb-tab-bar__button`, `vsb-notification-bar` 等）
  - 規約文書を本 spec 内部の C# コメント／XML Doc で明文化する
  - 観測可能な完了状態: 必須クラス一覧が単一ソースで参照でき、タブ別に分離された定数が Runtime から利用可能になる
  - _Requirements: 6.1, 6.2_
  - _Boundary: Skin/ValidationRules_

- [ ] 6.3 `SkinValidator` を TDD で実装する
  - 必須クラスが欠落した UXML を読み込ませて `SkinValidationReport.AllValid == false` と該当タブ ID を Issues に返すテストを先に書く
  - 実装後、Validator が副作用（状態変更）を持たず、呼出し元が受け取った Report に基づき `TabPanelRegistry` に失敗マーク指示を出す構造であることを検証する
  - 検証結果は `DiagnosticsLogger` に `LogCategory.Skin` で記録する
  - 観測可能な完了状態: 欠落検出テスト・正常テストの両方が緑、ログが Skin カテゴリで残る
  - _Requirements: 6.1, 6.2, 6.5, 6.6_
  - _Boundary: Skin/SkinValidator_
  - _Depends: 6.1, 6.2_

## 7. Core: 共通 UI コンポーネントライブラリ

- [ ] 7.1 (P) `VsbSlider`（数値スライダー）を UXML カスタムコントロール + USS + C# ロジックで実装する
  - TDD: `ValueChanged` / `Committed` イベントの発火、min/max/step の UxmlAttribute 反映、値域違反時の挙動を検証するテストを先に書く
  - `VsbControlBase` に `vsb-` プレフィクス登録と `DiagnosticsLogger` 注入を集約する
  - 内部でメインスレッドブロッキング処理を行わない
  - 観測可能な完了状態: PlayMode テストで UXML 直参照のスライダーが値変更を通知し、セレクタ `vsb-slider__handle` が適用される
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.7_
  - _Boundary: CommonUi/VsbSlider_

- [ ] 7.2 (P) `VsbColorPicker`（RGB/HSV 色選択）を実装する
  - TDD: RGB/HSV 切替を UxmlAttribute `mode` で受け、`ValueChanged(Color)` / `Committed(Color)` が発火することを検証する
  - 観測可能な完了状態: UXML 参照で `vsb-color-picker` がレンダリングされ、色変更イベントが緑テストで受信できる
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.7_
  - _Boundary: CommonUi/VsbColorPicker_

- [ ] 7.3 (P) `VsbNumberedList`（可変長整列リスト）を実装する
  - TDD: `AddItem` / `RemoveAt` / `Reorder` で `ItemAdded` / `ItemRemoved` / `ItemReordered` が発火し、自動採番が保たれることを検証する
  - 観測可能な完了状態: 3 要素追加→並び替え→削除のシナリオでイベント順序が期待通りになる緑テストがある
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.7_
  - _Boundary: CommonUi/VsbNumberedList_

- [ ] 7.4 (P) `VsbToggleGroup`（排他選択）を実装する
  - TDD: `Keys` UxmlAttribute（カンマ区切り）で項目群を定義し、選択時 `SelectionChanged(selectedKey)` が排他発火することを検証する
  - 観測可能な完了状態: 2 個以上の Key を持つグループで同時選択不可になる緑テストがある
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.7_
  - _Boundary: CommonUi/VsbToggleGroup_

- [ ] 7.5 `CommonUiRegistration` で 4 コントロールの UxmlFactory と既定 USS を一括登録する
  - `RegisterAll()` を提供し、`UiShellBootstrapper` の初期化時に 1 度だけ呼ぶ契約を用意する
  - 登録後、UXML から `<VsbSlider />` 等の参照が解決されることをテストで確認する
  - 観測可能な完了状態: UXML 参照テストが 4 コントロール全てで緑になる
  - _Requirements: 7.2, 7.5_
  - _Boundary: CommonUi/Registration_
  - _Depends: 7.1, 7.2, 7.3, 7.4_

## 8. Core: ルート UIDocument / タブレジストリ / タブバー

- [ ] 8.1 ルート UXML / USS と PanelSettings の構築を TDD で実装する
  - `TabBar.uxml` / `TabBar.uss` / `NotificationBar.uxml` / `EmptyTabShell.uxml` を Runtime.UxmlUss 下に作成する
  - `RootUiDocumentBuilder` が単一 PanelSettings（`targetDisplay = 0`）を生成・共有すること、タブバー領域とタブコンテンツ領域の階層が構築されることを検証する
  - `targetDisplay != 0` 設定を入力された場合に警告ログ + 0 に強制するテストを追加する
  - 観測可能な完了状態: PlayMode 起動でルート UIDocument が Display 1 のみに現れ、Display 2 へ描画される経路がないテストが緑
  - _Requirements: 1.1, 1.2, 1.3, 1.7, 6.8_
  - _Boundary: Panels/RootUiDocumentBuilder_

- [ ] 8.2 `TabPanelRegistry` のプリロード完了判定を TDD で実装する
  - 3 タブ分の OnEnable 到着を待って `IsPreloadComplete` が `true` になるテストを先に書く
  - `RegisterTab(tabId, metadata)` が `ITabLifecycleHandle` を返すこと、`Dispose` で購読が解除されることを検証する
  - `PreloadProgress` が `LoadedCount / TotalCount / FailedTabs` を正しく返す
  - 失敗タブがあっても他タブは完了扱いとなる（Requirement 3.5）テストを追加する
  - 観測可能な完了状態: 3 タブ Mount 完了で `IsPreloadComplete == true`、失敗 1 件注入でも `IsPreloadComplete == true` かつ `FailedTabs` に該当 ID が含まれる
  - _Requirements: 2.1, 3.1, 3.3, 3.4, 3.5, 3.6, 3.7, 5.7, 10.1, 10.2_
  - _Boundary: Panels/TabPanelRegistry_

- [ ] 8.3 `TabPanelRegistry.SwitchTo` を「`style.display` 切替のみ」で実装する
  - TDD: `SwitchTo(TabId)` 呼出し前後で VisualTreeAsset 参照が不変、`rootVisualElement.style.display` のみ変化することを検証する（Requirement 2.4, 3.6）
  - `PreloadIncomplete` / `TabDisabled` / `AlreadyActive` の `SwitchErrorCode` 返却条件をテストで固定する
  - `OnTabSwitched` イベント発火と `ITabLifecycleHandle.OnActivated` / `OnDeactivated` の順序をテストで固定する
  - 切替所要時間（`TabSwitchEvent.Duration`）をログ出力する
  - 観測可能な完了状態: 100 回連続切替の 95 パーセンタイルが 16.67ms 以内に収まる PlayMode テストが緑
  - _Requirements: 2.3, 2.4, 2.5, 2.6, 2.8, 2.9, 3.6, 11.2_
  - _Boundary: Panels/TabPanelRegistry_
  - _Depends: 8.2_

- [ ] 8.4 `TabBarController` で 3 ボタン UI・アクティブ表示・非活性制御を実装する
  - TDD: プリロード未完了時は `.vsb-tab-bar__button--disabled` が付与され操作不可、完了時に活性化し初期タブ（Character）がアクティブ化するテストを書く
  - アクティブタブのボタンに `.vsb-tab-bar__button--active` が付くことを検証する
  - ボタンクリックで `TabPanelRegistry.SwitchTo` が呼ばれることをフェイク経由で検証する
  - タブ切替時にメイン出力に干渉する同期 I/O・ブロッキング API を一切呼ばない（レビュー観点）
  - 観測可能な完了状態: プリロード完了前クリックは無視、完了後は Character タブが表示されるテストが緑
  - _Requirements: 1.3, 2.2, 2.6, 2.7, 2.9, 3.2, 3.3, 9.2, 11.2_
  - _Boundary: Panels/TabBarController_
  - _Depends: 8.3_

## 9. Core: 通知バー / メイン出力監視 / フェイルセーフ

- [ ] 9.1 (P) `NotificationBarController` を TDD で実装する
  - TDD: 接続断・再接続中・Display フォールバック・プリロード失敗の 4 種警告を縦積みで表示し、閉じるボタンで一時非表示化できることを検証する
  - 縦積みは最大 3 件、それ以上は診断パネルに流すことをテストで固定する
  - 観測可能な完了状態: `IConnectionStatus` をフェイクで変動させ、UI 上の警告要素数が期待通りになる PlayMode テストが緑
  - _Requirements: 6.6, 9.5, 9.6_
  - _Boundary: Diagnostics/NotificationBar_

- [ ] 9.2 (P) `MainOutputStatusWatcher` を TDD で実装する
  - TDD: `UiSubscriptionClient` 経由で `output/display/fallback` トピックの state を購読し、受信時に `NotificationBarController` へ警告発行指示を出すことを検証する
  - フェイク IPC を使って Display 1 フォールバック発生／解除のシナリオを往復させる
  - 観測可能な完了状態: フォールバック state 受信で警告が出現し、解除 state 受信で警告が消える PlayMode テストが緑
  - _Requirements: 9.6, 11.6_
  - _Boundary: FailsafeAndConnection/MainOutputStatusWatcher_

- [ ] 9.3 接続未確立時のフェイルセーフ挙動を結合テストで固定する
  - `FakeIpcClient` の接続を失敗状態で固定し、UI 起動・タブ切替・共通コンポーネント動作が継続することを検証する
  - `PublishState` 呼出しが `SendError.NotConnected` を即時返却し、UI 側に例外が波及しないこと（Requirement 9.4）を検証する
  - 後から接続が確立した場合に送信が通常成功に切り替わること（Requirement 9.3）を検証する
  - 観測可能な完了状態: 接続永続失敗・後接続成功の 2 シナリオが緑テストになる
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.7_
  - _Boundary: Commands/UiCommandClient, FailsafeAndConnection_
  - _Depends: 4.4, 9.2_

## 10. Integration: ブートストラップとライフサイクル統合

- [ ] 10.1 `UiShellConfig` と `UiShellBootstrapper` の Composition Root を実装する
  - 初期化順序: `PanelSettings → RootUIDocument → 3 TabUIDocument → TabPanelRegistry/TabBarController → SkinValidator → AddressablesAssetLoader → UiCommandClient/UiSubscriptionClient → MainOutputStatusWatcher → IPC 接続試行` をテストで固定する
  - 解放は逆順で Dispose パターンを徹底する
  - `BootstrapErrorCode`（`SkinProfileMissing`, `PanelSettingsAssignFailed`, `TabUxmlAttachFailed`, `AddressablesInitFailed`, `IpcAbstractionUnavailable`）の返却条件を全て網羅するテストを書く
  - IPC 接続試行が未完了のまま UI 起動が完了できること（Requirement 9.1）を検証する
  - 観測可能な完了状態: `StartShell(config)` が `BootstrapResult.Success == true` を返し、`StopShell` 後に全ハンドルが Dispose されているテストが緑
  - _Requirements: 1.1, 1.4, 3.1, 5.1, 8.1, 8.2, 9.1, 9.7_
  - _Boundary: Bootstrap/UiShellBootstrapper_
  - _Depends: 3.2, 3.3, 4.2, 4.4, 4.6, 5.2, 5.3, 6.1, 6.3, 7.5, 8.2, 8.3, 8.4, 9.1, 9.2_

- [ ] 10.2 `UiShellLifecycleDriver` で PlayMode / Standalone / Edit モード分岐を実装する
  - `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` で Standalone / PlayMode 開始時に `StartShell` を呼ぶ
  - Editor 限定で `EditorApplication.playModeStateChanged` をフックし、PlayMode 終了時に `StopShell` を呼ぶ（`#if UNITY_EDITOR`）
  - `Application.quitting` で Standalone 終了時の解放を保証する
  - Edit モードで一切初期化しないこと、ドメインリロードを跨いだ静的状態を持たないことをテストで固定する
  - 観測可能な完了状態: PlayMode Start/Stop を 5 回繰り返しても UIDocument 重複生成・リーク兆候なしの PlayMode テストが緑
  - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7_
  - _Boundary: Bootstrap/UiShellLifecycleDriver_
  - _Depends: 10.1_

- [ ] 10.3 Display 1 割当の抽象点（`IDisplayAssignmentStrategy`）をフック点として残す
  - 現行実装は `targetDisplay = 0` 固定だが、将来 `runtime-display-selector-integration`（spec #7）で差し替え可能にするため、Strategy インタフェースを内部に用意する
  - デフォルト実装は固定割当、差替え可能性をテストで確認する（差替え Strategy を渡した場合にのみ別 display に割当される）
  - 観測可能な完了状態: モック Strategy を渡したテストで `targetDisplay` が変化し、デフォルトでは常に 0 に固定される
  - _Requirements: 1.6_
  - _Boundary: Bootstrap/DisplayAssignmentHook_
  - _Depends: 10.1_

- [ ] 10.4 タブ ライフサイクルと購読解除のバックストップを結合する
  - `ITabLifecycleHandle.Dispose` および `UiShellBootstrapper.StopShell` 時に、`UiSubscriptionClient` 経由の購読・`AddressablesAssetLoader` の scope を一括解除するバックストップを実装する
  - タブ spec 相当のモックが `Dispose` を忘れた場合でもシェル停止時に全解除される結合テストを追加する
  - 観測可能な完了状態: モックが購読を 10 件残してもシェル停止後に残存 0 件となる緑テストがある
  - _Requirements: 2.8, 5.7_
  - _Boundary: Bootstrap/UiShellBootstrapper, Panels/TabPanelRegistry, Commands/UiSubscriptionClient, AssetLoading/AddressablesAssetLoader_
  - _Depends: 10.1_

## 11. Integration: スキン適用と検証の結合

- [ ] 11.1 `UiShellBootstrapper` が `UiToolkitShellSkinProfile` を読み込み、ルート/タブ UXML/USS を差し替える経路を結合する
  - TDD: 既定プロファイルで起動 → 別 SO 注入で起動 の 2 ケースで USS 変化と UXML 差し替えが反映されることを検証する
  - UXML 差し替え結果として必須クラスが不足する場合に `SkinValidator` が検出し、該当タブのみ非活性化して他タブ・シェル全体は継続する（Requirement 6.6）ことを結合テストで固定する
  - 追加 USS（利用者プロジェクト）が `CommonUiStyleSheets` 経由で積まれ、後ろほど優先される順序契約を検証する
  - 観測可能な完了状態: スキン差し替え PlayMode テストで既定と差替え後で USS プロパティが変化する緑テストがある
  - _Requirements: 6.3, 6.4, 6.5, 6.6, 6.7, 6.8_
  - _Boundary: Bootstrap/UiShellBootstrapper, Skin/SkinProfile, Skin/SkinValidator_
  - _Depends: 10.1_

- [ ] 11.2 `SkinProfileEditor`（Inspector カスタム）をガイド付き UX として追加する
  - Editor で `UiToolkitShellSkinProfile` の Inspector にセクション見出し・必須フィールド警告・既定値コピーボタンを提供する
  - 観測可能な完了状態: Editor で SO を開いた際に 3 タブセクションごとの見出しと警告バナーが表示される
  - _Requirements: 6.7, 6.4_
  - _Boundary: Editor/SkinProfileEditor_
  - _Depends: 6.1_

## 12. Validation: 単体・結合・E2E テスト網羅

- [ ] 12.1 (P) Unit: TabPanelRegistry のプリロード完了判定と表示切替契約をまとめた代表テストを追加する
  - 3 タブ完了→`IsPreloadComplete == true` の成功ケースと、1 タブ失敗時も `LoadedCount == 2` で進行する縮退ケースをそれぞれ追加する
  - `SwitchTo` 前後の VisualTreeAsset 参照不変性を明示検証する
  - 観測可能な完了状態: `TabPanelRegistryTests` / `TabSwitchTests` が全緑で CI に乗る
  - _Requirements: 2.3, 2.4, 3.1, 3.5, 3.6, 10.5_
  - _Boundary: Tests/Runtime_
  - _Depends: 8.2, 8.3_

- [ ] 12.2 (P) Unit: AddressablesAssetLoader の重複ロード抑止・Completion メインスレッド配信テストを追加する
  - 同一 key 連続 2 回の `LoadAsync` が 1 本のハンドルに集約されるケース、Cancel 時に `LoadErrorCode.Cancelled` で callback が呼ばれるケースを追加する
  - 観測可能な完了状態: `AsyncAssetLoaderTests` が全緑で CI に乗る
  - _Requirements: 4.3, 4.4, 4.7, 4.8, 10.5_
  - _Boundary: Tests/Runtime_
  - _Depends: 5.2_

- [ ] 12.3 (P) Unit: UiCommandClient の 3 系統呼び分けと SendError 伝搬テストを追加する
  - `PublishState` / `PublishEvent` / `RequestAsync` の経路分岐、`NotConnected` / `TopicInvalid` / `Timeout` の各エラーコード返却を検証する
  - 観測可能な完了状態: `UiCommandClientTests` が全緑で CI に乗る
  - _Requirements: 5.2, 5.3, 5.4, 5.5, 5.9, 9.4, 10.5_
  - _Boundary: Tests/Runtime_
  - _Depends: 4.4_

- [ ] 12.4 (P) Unit: SkinValidator の必須クラス欠落検出テストを追加する
  - 必須クラスを欠落させた UXML と、全クラスが揃った UXML の 2 パターンで Report 差を検証する
  - 観測可能な完了状態: `SkinValidatorTests` が全緑で CI に乗る
  - _Requirements: 6.5, 6.6, 10.5_
  - _Boundary: Tests/Runtime_
  - _Depends: 6.3_

- [ ] 12.5 Integration: IPC モック注入による送信↔受信 round-trip 結合テストを追加する
  - `FakeIpcClient` を差し込み、`PublishState` → `Subscribe` コールバック受信までのパスを検証する
  - `core-ipc-foundation` の自己ループ機構（spec #1 Requirement 8）を模したテスト手順書を tests ディレクトリに残す
  - 観測可能な完了状態: round-trip 結合テストが緑、ダミーコマンドの送受信が UI 単体で完結する
  - _Requirements: 10.3, 10.5, 10.6_
  - _Boundary: Tests/Runtime_
  - _Depends: 4.4, 4.6_

- [ ] 12.6 Integration: 起動→プリロード→初期タブ表示のエンドツーエンド結合テスト
  - `UiShellBootstrapper.StartShell` 後、診断スナップショットで `Preload.LoadedCount == 3` かつ `ActiveTab == Character` を確認する
  - IPC 未接続・Addressables 初期化成功の設定でも最終的に UI が操作可能になることを確認する
  - 観測可能な完了状態: 結合シナリオが緑、シェル単独で Wave 2 完了条件を満たすことを示すログ出力を伴う
  - _Requirements: 1.4, 3.1, 3.3, 10.1_
  - _Boundary: Tests/Runtime_
  - _Depends: 10.1_

- [ ] 12.7 E2E (PlayMode): `UiShellPlayModeSample` 最小シーンを仕上げて手動検証手順を付ける
  - 2.3 で置いたシーンに `UiShellBootstrapper` 駆動用コンポーネントと既定 SkinProfile を割当てる
  - 手動検証チェックリスト（起動→タブバー表示→クリック切替→通知バー表示）を tests ディレクトリの Markdown で残す
  - 観測可能な完了状態: Editor で PlayMode に入ると Display 1 に UI が出て 3 タブ切替が確認でき、手順書のチェック項目が全て埋まる
  - _Requirements: 10.4_
  - _Boundary: Tests/PlayMode_
  - _Depends: 10.2_

- [ ] 12.8 E2E (PlayMode): PlayMode 反復起動リーク試験と単独検証可能性の総合確認
  - PlayMode Start/Stop を 5 回繰り返しても `UIDocument` 重複生成・購読残存・Addressables ハンドル残存がないことを自動検証する
  - タブ spec が未実装な状態でも空枠 UXML (`EmptyTabShell.uxml`) で各タブが表示可能なことを確認する
  - 観測可能な完了状態: リーク試験・空枠起動ともに緑、Wave 2 の完了判定が Wave 3 実装と独立に下せることをログで示す
  - _Requirements: 8.3, 8.4, 8.5, 8.6, 8.7, 10.1, 10.2_
  - _Boundary: Tests/PlayMode_
  - _Depends: 10.2, 12.7_

- [ ] 12.9 Performance: プリロード・タブ切替・非同期ロード中フレームの性能目標を計測する
  - プリロード所要時間 < 1 秒、タブ切替 95 パーセンタイル < 16.67ms、100MB 相当ロード並行時のメイン出力側 `Time.unscaledDeltaTime` < 16.67ms をそれぞれ計測する
  - 計測結果を `DiagnosticsLogger` と結合テストの assertion で自動判定可能にする
  - 観測可能な完了状態: 3 指標すべての計測テストが緑、結果がログに数値として残る
  - _Requirements: 2.9, 3.1, 4.6_
  - _Boundary: Tests/Runtime, Tests/PlayMode_
  - _Depends: 12.6_

- [ ] 12.10 Coverage Audit: Requirement 全 ID の任意サンプリング検証と診断 API 実機確認
  - Requirement 1.x〜11.x から無作為に 5 件ピックし、該当タスクの成果物がコード / テストで観測可能な完了条件を満たしていることを目視確認する
  - `ShellDiagnosticsSnapshot.Capture()` が全フィールドを埋めた状態で返ることを実機 PlayMode で確認する
  - 観測可能な完了状態: 抜き取り検証表（Req ID → 成果物リンク → 確認結果）が tests ディレクトリの Markdown に残る
  - _Requirements: 3.7, 4.9, 11.9, 10.5_
  - _Boundary: Tests/Runtime_
  - _Depends: 3.3, 12.6, 12.8_

---

## Requirements Coverage Summary

| Req | Covered by Tasks |
|---|---|
| 1.1, 1.2, 1.3, 1.7 | 8.1, 10.1, 12.6 |
| 1.4 | 10.1, 12.6 |
| 1.5 | 1.1, 1.2 |
| 1.6 | 10.3 |
| 2.1 | 8.2 |
| 2.2 | 8.4 |
| 2.3, 2.4, 2.5, 2.8 | 8.3, 12.1 |
| 2.6, 2.7 | 8.4 |
| 2.9 | 8.3, 8.4, 12.9 |
| 3.1, 3.3 | 8.2, 10.1, 12.6 |
| 3.2 | 8.4 |
| 3.4, 3.5, 3.6 | 8.2, 8.3, 12.1 |
| 3.7 | 3.3, 12.10 |
| 4.1, 4.2, 4.3, 4.4, 4.6, 4.7, 4.8 | 5.1, 5.2, 5.3, 12.2 |
| 4.5 | 8.4 (tab bar remains operable), 9.3 |
| 4.9 | 3.3, 5.2, 12.10 |
| 5.1 | 4.4, 10.1 |
| 5.2, 5.3, 5.4, 5.5, 5.9 | 4.3, 4.4, 12.3 |
| 5.6, 5.7 | 4.5, 4.6, 10.4 |
| 5.8, 5.10 | 1.1, 1.2, 4.4 |
| 6.1, 6.2 | 6.2, 6.3 |
| 6.3, 6.4 | 6.1, 11.1, 11.2 |
| 6.5, 6.6 | 6.3, 11.1, 12.4 |
| 6.7, 6.8 | 6.1, 8.1, 11.1, 11.2 |
| 7.1, 7.2, 7.3, 7.4, 7.7 | 7.1, 7.2, 7.3, 7.4 |
| 7.5 | 1.2, 7.5 |
| 7.6 | 1.1, 1.2 (asmdef 境界が独自実装を妨げない構造) |
| 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7 | 10.2, 12.8 |
| 9.1, 9.2, 9.3, 9.4, 9.7 | 9.3, 10.1 |
| 9.5 | 4.2, 9.1 |
| 9.6 | 9.1, 9.2 |
| 10.1, 10.2 | 8.2, 10.1, 12.6, 12.8 |
| 10.3 | 2.1, 12.5 |
| 10.4 | 2.3, 12.7 |
| 10.5 | 12.1, 12.2, 12.3, 12.4, 12.10 |
| 10.6 | 2.1, 12.5 |
| 10.7 | 2.2 |
| 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8 | 3.2, 4.4, 4.6, 5.2, 5.3, 8.3, 8.4, 9.2 |
| 11.9 | 3.3, 12.10 |

すべての Requirement が少なくとも 1 件のタスクに対応している（Task Plan Review Gate 通過）。
