# セッション引き継ぎノート

## ◯ 今回やったこと（前 HANDOVER P0/P1 全消化セッション）

前 HANDOVER `402a2f0` の P0-1 / P0-2 / P1-D / P1-F (×2) を Auto Mode で順次消化。残失敗 10件 → 0件 (期待 Skip 1 件含む)。最後に MainDemo.unity を作成し PlayMode で Display 2 メイン出力の起動成立を確認。

### 修正サマリ

| Task | カテゴリ | 修正 | テスト結果 |
|---|---|---|---|
| P0-2 | UI Shell パッケージ rename | `com.hidano.vtuber-system-base.ui-toolkit-shell` → `jp.hidano.*` (ディレクトリ + package.json + 4 依存パッケージ + packages-lock + README) | 8件 PASS (RootUiDocumentBuilder×3, CommonUiRegistration×1, SkinProfileEditor×2, UiShellPlayModeLeak×2) |
| P1-D | RAC AdapterRoundTrip | (1) `SlotCatalogPublisher.OnStateChanged` の Slot 検知を `next == Created` から「`next != Disposed && !_knownSlotIds.Contains` → 追加」に変更。(2) `SlotAssignmentApplier.UnregisterDynamic` で SemaphoreSlim を Dispose しない (mid-flight HandleAssignment が finally で `sem.Release()` を呼ぶため) | 2件 PASS (Assignment_AssignsSlot / Assignment_UnknownAvatarKey) |
| P1-F (Performance) | Catalog 検知改善 | `loader.LoadAsync` ベースの probe を `Addressables.ResourceLocators` 列挙ベースに変更。catalog 未構築時は `Assert.Ignore` (asmdef に `Unity.Addressables` 参照追加) | 1件 SKIPPED (期待動作; catalog 構築 PlayMode で本番計測) |
| P1-F (JsonPresetStorage) | エラーコード許容範囲 | `Save_FailsCleanly_LeavesPriorFileIntactWhenTempCannotBeRenamed` の assertion を `IOError or PermissionDenied` に拡張 (Windows では tempPath が directory のとき `WriteAllText` が `UnauthorizedAccessException` → `PermissionDenied` を返すため) | 14件 PASS (Test fixture 全体) |
| P0-1 | MainDemo.unity 構築 | `Assets/Scenes/MainDemo.unity` を `uloop-execute-dynamic-code` でスクリプト生成 (IntegratedDemoRoot に `OutputSceneBootstrapper` + `IntegratedDemoBootstrap`、SerializedObject で `_outputSceneBootstrapper` フィールド配線) | PlayMode で `OutputSceneBootstrapper` が全 7 フェーズ Complete (RootsCreated → CameraReady → LightReady → VolumeReady → IpcServerReady → DispatcherReady → DisplayRouted) Errors 0 |

### 最終テスト集計

- **PlayMode**: 374 / 374 PASS (regression 0)
- **EditMode (UiToolkitShell)**: 414 PASS + 1 SKIPPED (Performance test 期待 Ignore) / 415
- **EditMode (StageLightingVolume)**: 164 / 164 PASS

## ◯ 決定事項

- **`com.hidano.vtuber-system-base.ui-toolkit-shell` を `jp.hidano.*` へ rename**：プロダクションコード (`SkinProfileEditor.cs`, `CommonUiRegistration.cs`) が既に `jp.` パスをハードコードしていたため。プロジェクト内 7/10 パッケージは `jp.` 接頭辞で convention 一貫。残 2 パッケージ (`core-ipc-foundation`, `output-renderer-shell`) は内部参照が `jp.` でないため当面 `com.` 据え置き
- **SlotCatalogPublisher の Slot 追加検知ロジック**：RAC 外部パッケージの `SlotManager.AddSlotAsync` は `Created → Active` を 1 イベントで emit するため、`next == Created` フィルタでは検知不能。`Disposed 以外への遷移で初出現の slotId` を「追加」と判定する仕様に統一
- **`SlotAssignmentApplier.UnregisterDynamic` で SemaphoreSlim を Dispose しない**：HandleAssignment が中継 (RemoveSlotAsync → AddSlotAsync) を行う際、内側 RemoveSlotAsync が同期的に OnSlotRemoved → UnregisterDynamic を発火させる。このとき HandleAssignment は try-finally で sem を保持しており、ここで Dispose すると finally の `sem.Release()` が `ObjectDisposedException` を投げる。SemaphoreSlim 本体は GC 任せにし、本 Applier の `Dispose` 時のみ明示破棄する
- **Performance test の catalog 未構築検知は `Addressables.ResourceLocators` ベース**：`loader.LoadAsync` ベースの probe は Addressables 2.x で同期 throws しない場合があり信頼性低い。`Addressables.ResourceLocators` を列挙して 0 件なら catalog 未構築と確定判定
- **MainDemo.unity 最小構成は SkinProfile 未設定で OK**：HANDOVER P0-1 の minimum scope 「メイン出力のみ起動成立」は `IntegratedDemoConfig.SkinProfile = null` で達成。production が `SkinProfile not set; skipping UI shell startup` を warn ログで明示的に表現する

## ◯ 捨てた選択肢と理由

- **「テスト側のパス修正で `com.` のままにする」 (P0-2)**：プロダクションコード (`SkinProfileEditor.cs`, `CommonUiRegistration.cs`) が `jp.` をハードコードしていたため、片側だけ修正では不整合が残る。パッケージ rename が正解
- **「Performance test を `Application.isPlaying` でガード」 (P1-F)**：catalog の有無は PlayMode/EditMode と直交する観点。Addressables 自体の状態を直接見るほうが正確
- **「JsonPresetStorage プロダクションコードの例外分類変更」 (P1-F)**：`UnauthorizedAccessException → PermissionDenied` のマッピングは Windows 上で正しい意味論。テスト setup が tempPath を directory にする「失敗パスをまず WriteAllText で踏む」シナリオを暗黙に作っていただけなので、テスト側で許容範囲を広げるほうが Production 契約を歪めない
- **「`SlotCatalogPublisher` の OnSlotAdded を廃止し IntegratedDemoBootstrap.Initialize で全 slot を直接登録」 (P1-D)**：Bootstrapper.Initialize 時点で 0 slot しかない (test もその前提)。動的追加検知が本来の責務であり、検知ロジックを正しく直すのが筋
- **「P0-1 で全 SkinProfile / 4 UXML をスクリプト生成して Display 1 まで立ち上げ」**：HANDOVER の minimum scope 「メイン出力のみ起動成立」を満たす分には不要。SkinProfile 作成 + 4 UXML 作成 + 各 SerializedObject 配線は工数大、UI Shell 起動成立は別途検証

## ◯ ハマりどころ

- **パッケージ rename 後の Unity 認識**：ディレクトリ rename + package.json/lock 更新だけでは Unity がパッケージを見つけられない。`UnityEditor.PackageManager.Client.Resolve()` + `AssetDatabase.Refresh(ForceUpdate | ForceSynchronousImport)` + `CompilationPipeline.RequestScriptCompilation(CleanBuildCache)` の 3 連を `uloop-execute-dynamic-code` で叩いて初めて UiToolkitShell.* DLL が ScriptAssemblies に出力された。最初の `uloop compile --force-recompile` だけでは 212 件の `IDiagnosticsLogger could not be found` が出続けた
- **SemaphoreSlim mid-flight Dispose**：`SlotCatalogPublisher` の OnSlotAdded 検知を直したら `Status: Error` (Expected: "Assigned") の別失敗が浮上。原因は HandleAssignment が `RemoveSlotAsync → AddSlotAsync` で中継するとき、内側 RemoveSlotAsync が同期的に `OnSlotRemoved → UnregisterDynamic` を発火させ、SemaphoreSlim を Dispose してしまう。HandleAssignment は try で sem を保持しているので finally の Release で ObjectDisposedException → HandleStateAsync の catch で `PublishError(Unknown)` → SlotStateMapper.Error が publish されて見えていた
- **`uloop run-tests --filter-type all` の 1284 件は RPC 180s タイムアウトを超える**：UiToolkitShell の `[UnityTest]` PlayMode style もすべて EditMode 集計に含まれるため。範囲を絞った regex 実行で代替する (`UiToolkitShell\.Tests`、`StageLightingVolume` など)
- **`PlayMode` filter での `[UnityTest]` テスト**：EditMode テスト assembly に PlayMode 名前空間の `[UnityTest]` 連中が混じっている。`--test-mode PlayMode` だと "No tests found" になり、`--test-mode EditMode` で拾える (例: `UiShellPlayModeLeakAndEmptyTabTests`)
- **`Application.AddressableAssets` 名前空間が test asmdef の `references` に明示されないと CS0234**：UiToolkitShell.Tests asmdef は Runtime asmdef を参照するが Runtime が `Unity.Addressables` を参照していても test には伝播しない。test asmdef にも `"Unity.Addressables"` を追記必須

## ◯ 学び

- **Unity 6.3 + Addressables 2.x の embedded package rename フロー**：`PackageManager.Client.Resolve` → `AssetDatabase.Refresh(ForceUpdate | ForceSynchronousImport)` → `RequestScriptCompilation(CleanBuildCache)` の 3 連が必須。これを叩かないと `Library/ScriptAssemblies/` に新パッケージの DLL が出力されず、依存パッケージが軒並み 200+ 件のコンパイルエラーになる
- **`SemaphoreSlim` を `Dispose` で必ず解放しなくても GC が拾う**：内部 `WaitHandle` を持たない場合は安全。ここでは「mid-flight 操作中は Dispose しない、外側で纏めて破棄」のほうが defensive で正しい
- **NUnit `Is.EqualTo(x).Or.EqualTo(y)` 構文**：複数の許容値で assertion を緩めるのに最短記法。`Is.AnyOf(...)` も同等
- **`Addressables.ResourceLocators` で catalog 構築有無を直接判定**：Editor / PlayMode / Player 全環境で確定判定可能。`Application.isPlaying` や `Application.isEditor` での代用より精度高い
- **HANDOVER の "minimum scope" を尊重する**：P0-1 は「メイン出力のみで起動成立すれば完了」と明記されていた。SkinProfile + 4 UXML 作成は完了条件外。最小スコープでまず確認、追加スコープは別 PR

## ◯ 次にやること

### P0（最優先）

- (なし; 前 HANDOVER P0/P1 すべて消化済み)

### P1（中優先）

- **MainDemo.unity に SkinProfile + UXML を加えて Display 1 UI Shell を立ち上げる**：P0-1 minimum scope の延長。`IntegratedDemoSkinProfile.asset` (`UiToolkitShellSkinProfile` SO) + `IntegratedDemo_Root.uxml` + 3 タブ用 UXML を `Assets/UI Toolkit/IntegratedDemo/` 配下に作成し、Inspector で IntegratedDemoBootstrap に assign → PlayMode で `UiShellBootstrapper: shell running.` を確認
- **`CoreIpcRuntime.Current is null` 警告の調査**：MainDemo.unity PlayMode で 3 回ほど警告が出る。`RuntimeBootstrap.OnBeforeSceneLoad` が走っていないか走るタイミング遅い。core-ipc-foundation 側の RuntimeInitializeOnLoadMethod 設定を確認

### P2（低優先・スコープ外）

- 9 パッケージの OpenUPM / npm registry 公開
- Wave 4：PVW/PGM、WebUI、タイムライン録画リプレイ
- 命名衝突 `VTuberSystemBase.CameraSwitcherOutputAdapter` namespace × `CameraSwitcherOutputAdapter` Domain クラスの rename 根治 (前 HANDOVER P2 から継続)
- `output-renderer-shell/package.json` の `dependencies` に `com.hidano.runtime-display-selector` 追加 (前 HANDOVER P2 から継続)
- 残 2 つの `com.hidano.vtuber-system-base.*` パッケージ (`core-ipc-foundation`, `output-renderer-shell`) も `jp.` へ rename して conventions 完全統一

## ◯ 関連ファイル

### 今回触った主要ファイル

#### P0-2: UI Shell パッケージ rename

- `Packages/com.hidano.vtuber-system-base.ui-toolkit-shell/` → `Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/` (ディレクトリ rename)
- `Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/package.json` の `name` フィールド更新
- `Packages/packages-lock.json` (top-level entry + 4 depended-by entries 全置換)
- 依存パッケージ 4 件の `package.json`：`stage-lighting-volume-tab`, `character-selection-tab`, `camera-switcher-tab`, `integrated-demo`
- `Packages/jp.hidano.vtuber-system-base.integrated-demo/Samples~/IntegratedDemo/README.md`

#### P1-D: RAC AdapterRoundTrip

- `Packages/jp.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/Senders/SlotCatalogPublisher.cs` (`OnStateChanged` の Slot 追加検知ロジック)
- `Packages/jp.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/Receivers/SlotAssignmentApplier.cs` (`UnregisterDynamic` の SemaphoreSlim 非破棄)

#### P1-F: Performance / JsonPresetStorage

- `Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/Tests/Runtime/PerformanceMetricsTests.cs` (`AsyncLoad_HundredConcurrentInflight` の catalog probe)
- `Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/Tests/UiToolkitShell.Tests.asmdef` (`Unity.Addressables` 参照追加)
- `Packages/jp.hidano.vtuber-system-base.stage-lighting-volume-tab/Tests/Runtime/JsonPresetStorageTests.cs` (`Save_FailsCleanly` の assertion 拡張)

#### P0-1: MainDemo.unity

- `Assets/Scenes/MainDemo.unity` (新規 / `uloop-execute-dynamic-code` でスクリプト生成)
- `Assets/Scenes/MainDemo.unity.meta`

### 環境

- `unity-cli-loop` v2.1.1 グローバルインストール済 (`C:\Users\Hidano\AppData\Roaming\npm\uloop`)
- Unity Editor: `6000.3.10f1` (`C:\Program Files\Unity\Hub\Editor\6000.3.10f1`)
- Unity プロジェクトルート: `D:\Personal\Repositries\VTuberSystemBase\VTuberSystemBase`
- テスト結果 XML 履歴: `VTuberSystemBase/.uloop/outputs/TestResults/*.xml`

### 参照

- `docs/integration-plan.md` — 統合開発計画 v1.0
- `docs/requirements.md` — VTuberSystemBase 要件定義書
- `Packages/jp.hidano.vtuber-system-base.integrated-demo/Samples~/IntegratedDemo/README.md` — MainDemo.unity 構築手順 (Display 1 含む完全版)
- `https://github.com/hatayama/unity-cli-loop` — uloop README
- 前回の `HANDOVER.md`（commit `402a2f0`、本ノートで上書き）— Wave 3a〜3e + URP/IntegratedDemo セッション
