# Handover (2026-04-26)

## 今回やったこと

- `/kiro:spec-run-waves` を Wave 1 (`core-ipc-foundation`) ALL_OK で完了、Wave 2 を sequential で再開
- ui-toolkit-shell の tasks.md を git 実態に同期 (1.1〜10.3、11.1, 11.2, 12.1, 12.2, 12.3 を `[x]` 化)
- output-renderer-shell の tasks.md を同期 (1.2, 1.3 を `[x]` 化)
- EditMode テストハング修正 (cabcb8e): UiCommandClient timeout test の sync-over-async デッドロック
- PlayMode batch ハング修正 (2a1cd6c): `RuntimeBootstrap.OnBeforeSceneLoad` の auto-bootstrap が test runner で test runtime と競合 → test 専用 disabler で抑止
- PerformanceLoadTests のメモリ測定バグ修正 (9cc91f6): end snapshot 前の GC 抜けで gen-0 ガベージが delta に混入していた
- ui-toolkit-shell 12.4 を 1 タスク完走 (9fd62c0、コスト計測用プローブ)

## 決定事項

- **Wave 2 は sequential 実行**。同一 Unity プロジェクトで PlayMode batch を 2 つ並列起動すると `Temp/UnityLockfile` が競合する
- **tasks.md は手動 sync 容認**。spec-run のチェックボックス自動更新は信用できない (commit はあるが `[ ]` のまま放置されるパターン頻発)
- **Wave 3 着手は 4/30 8pm 週次リセット後**。残予算 15% で全 Wave 3 (~100 件) は不可
- **per-task quota 実測値: 約 1% / week** (12.4 ベース)。残 15% で安全フィットは 12 件まで
- **spec-run-waves の Wave 2 計画は parallel と書かれているが、実運用では sequential に降格**して良い (command コメントの "downgrade if contention" 条項に基づく)

## 捨てた選択肢と理由

- **timeout test を `[Ignore]` でスキップ** → 根本原因 (sync-over-async) を直さないと future test でも再発するため、`async Task` 化を選択
- **`CoreIpcRuntime.ResetForTesting()` だけで auto-bootstrap 残骸を掃除** → static 参照を null にするだけで実体は Dispose されず、port 61874 と背景スレッドが生き残るため、`DisableAutoBootstrap()` flag で発火そのものを止める方式に変更
- **Wave 2 を spec 計画通り parallel で再試行** → 過去 2 セッションで Unity lockfile 競合が再現済み、決定的失敗パターン
- **max-turns 60 のまま再走** → 12.4 が 1 度 60 turn で力尽きた実績あり、120 に増やして成功
- **テスト専用ガードを `#if UNITY_INCLUDE_TESTS` で production 本体に置く** → asmdef 単位で隔離する方が clean、`Tests/Runtime/TestSupport/AutoBootstrapDisabler.cs` (`UNITY_INCLUDE_TESTS` 制約付き asmdef 内) に新規追加

## ハマりどころ

- task 12.3 inner runner が **5 時間ループ** (Unity batch test → results.xml 出ず → 再試行を繰り返し)。`task-12.3-editmode.log` が延々 commit されるだけだった
- 「Unity プロセス 30 時間ハング」と誤計算 → 実際は 1.5 時間 (日付跨ぎを誤って桁上げ)
- ユーザーの**別件 Unity Editor (a8mb-unity-tools)** と混同しかけた → kill 対象は `-projectPath D:/Personal/Repositries/VTuberSystemBase` のバッチプロセスに限定すべき
- `claude -p` サブプロセスのコストは `/cost` に**含まれない** (別 API セッション扱い)。per-task コストは週次 % delta から逆算するしかない
- 月次/週次/5h ウィンドウの**3 種リミット併存**。週次が最も厳しい
- 「4+ 並列セッション」が usage の 60% 超を占めていた。spec-run-waves の parallel mode は quota も倍速で食う

## 学び

- Unity EditMode TestRunner で `sync void test + Assert.DoesNotThrowAsync(async () => await ...)` は、内側 `await` が main-thread SyncContext を捕捉 → NUnit が `.Wait()` で main thread をブロック → 継続が走らず古典的 sync-over-async デッドロック。`async Task` シグネチャ + `ConfigureAwait(false)` で回避
- `[RuntimeInitializeOnLoadMethod]` は **PlayMode テストでも fire する**。test runtime と prod auto-bootstrap が同居して resource を奪い合うので、test asmdef 側で `SubsystemRegistration` フェーズ (BeforeSceneLoad より早い) に disable seam を呼ぶのが正攻法
- `GC.GetTotalMemory(true)` と `(false)` は別物。retention bound を測りたいなら**両端で `(true)`** にして collector を強制する必要あり
- `PlayerLoopInstaller` は内部で静的 `s_flushAction` 1 個しか保持しない。複数 install は警告ログ + 置換になるだけで、二重に動かない
- `/usage` は機械ローカル approximation。他リポ/他デバイス分は含まれない (今回の per-task 計算もここを補正した)
- spec-run のフロー: outer (`/kiro:spec-run`) が inner (`claude -p "Execute only this single task..."`) を `--max-turns 60` で逐次起動。inner が hang すると outer も外側で待つだけで自動 kill しない
- `git reset --soft N` で commit 履歴のノイズを潰しつつ working tree の内容を保持できる (今回 9 件のログ-only commit を 1 件にまとめた)

## 次にやること

### P1 (今週中、~7% 消費見込)

- ui-toolkit-shell 残 6 件 (12.5〜12.10) を **sequential 実行**
  - inner-runner プロンプトを `claude -p` に直接投げる方式 (`--max-turns 120`) が安定
  - 各タスク完了後に `/usage` 確認しつつ進める

### P2 (4/30 8pm 週次リセット後)

- output-renderer-shell 残 19 件
- Wave 3: camera-switcher-tab / character-selection-tab / stage-lighting-volume-tab (~100 件)
  - Wave 3 は **同 Unity プロジェクト内で 3 spec が独立に PlayMode batch を回せるか不明**。安全側に倒すなら sequential 推奨

### P3 (低優先)

- pre-existing flaky test `EditorPlayModeBridgePlayModeTests.RepeatedSimulatedPlayModeCycles_KeepPortBindable` の調査 (Wave 1 完走時に同時 Editor 起動による flakiness が観測されていた)
- spec-run skill 自体に「N 分間 commit 進捗無し → 強制終了」の watchdog を組み込む (12.3 のような 5 時間ループ予防)

## 関連ファイル

### 今回触ったプロダクションコード

- `Packages/com.vtuber-system-base.core-ipc-foundation/Runtime/Core/Lifecycle/RuntimeBootstrap.cs` (`DisableAutoBootstrap()` 追加, `OnBeforeSceneLoad` に flag check 追加)

### 今回触ったテストコード

- `Packages/com.vtuber-system-base.core-ipc-foundation/Tests/Runtime/TestSupport/AutoBootstrapDisabler.cs` (新規, `UNITY_INCLUDE_TESTS` 制約 asmdef 内)
- `Packages/com.vtuber-system-base.core-ipc-foundation/Tests/Runtime/CoalesceSemanticsTests.cs` (defensive `[SetUp]` 追加)
- `Packages/com.vtuber-system-base.core-ipc-foundation/Tests/Runtime/PerformanceLoadTests.cs` (end snapshot に GC.Collect 追加)
- `Packages/jp.hidano.vtuber-system-base.ui-toolkit-shell/Tests/Runtime/UiCommandClientTests.cs` (timeout test を `async Task` に変換)

### tasks.md (sync 済)

- `.kiro/specs/core-ipc-foundation/tasks.md` (前セッションで全 [x])
- `.kiro/specs/ui-toolkit-shell/tasks.md` (12.5〜12.10 のみ `[ ]`)
- `.kiro/specs/output-renderer-shell/tasks.md` (1.1, 2.x〜8.x が `[ ]`)

### 関連コミット (今セッション)

- `cabcb8e` Fix sync-over-async deadlock in UiCommandClient timeout test
- `daf1aaa` Add [SetUp] to CoalesceSemanticsTests (defensive)
- `2a1cd6c` Suppress RuntimeBootstrap auto-bootstrap during the Unity Test Runner
- `9cc91f6` Force GC before end-of-test memory snapshot
- `37b764a` Sync tasks.md to git reality (Wave 2 partial completion)
- `091b947` task 12.4 中間 (Fixtures + テストファイル更新)
- `9fd62c0` 12.4 (P) Unit: SkinValidator の必須クラス欠落検出テストを追加する
