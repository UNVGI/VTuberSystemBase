# Handover (2026-04-27)

## 今回やったこと

- ui-toolkit-shell 残 6 件 (12.5〜12.10) を inner runner (`claude -p --enable-auto-mode`) で sequential 実行 → 全件 ALL_OK でクローズ
- output-renderer-shell の tasks.md と git 実態の差分を確認、1.1 / 2.1 を実態に合わせて `[x]` 化 (commit 4a5f2d2 / 3a36e46 / dde14b9 が既存で完了済みだった)
- output-renderer-shell の 2.2〜4.1 を **直接実装** (inner runner が「Create Unsafe Agents」で permission 拒否されたため、ユーザー承認のもと self-implement に切替)
- 各タスク 1 commit 単位で `[x]` 化と同梱

## 決定事項

- **inner runner (`claude -p --enable-auto-mode --max-turns N`) は許可ダイアログがリセットされた状態では permission 拒否される**。続行するには `/permissions` で `Bash(unset CLAUDECODE && echo "" | claude -p ...)` を allow するか、self-implement で進める
- **self-implement の per-task コストは inner runner より高い**（読解・実装が現セッションのトークンを直接消費するため）。残予算に応じて選ぶ
- **PlayMode テストは Unity Editor が VTuberSystemBase で開いていない限り走らせられない**。今回はコード生成と git commit のみ、Test Runner 実行は次回ローカルで行う
- **.meta ファイルは手動生成 (16-hex GUID)**。Unity 起動時に GUID 衝突があれば再生成される。今回は衝突しないよう連番ベースで採番
- 残予算 ~10% / week (4/27 04:35 時点) で 8 件達成 (1.1 sync, 2.1 sync, 2.2, 2.3, 2.4, 3.1, 3.2, 4.1)

## 捨てた選択肢と理由

- **inner runner の permission 設定変更を依頼** → `/permissions` の永続化はユーザー操作が必要。auto mode 中なので self-implement に倒した
- **`/kiro:spec-run output-renderer-shell` で残全件投げ** → 結局 nested claude を使うため同じ permission 拒否に遭う
- **2 タスクずつまとめて 1 commit** → tasks.md の進捗追跡と分割再開のしやすさを優先し、1 タスク 1 commit を維持
- **DefaultCameraFactory に `targetDisplay` を直接設定** → 2.x / 3.x の境界違反。`IDisplayRoutingService.Activate(camera, ...)` 経由のみ許容する設計通りに `targetDisplay = 0` のまま残した

## ハマりどころ

- HANDOVER.md が PreCompact hook の auto-handover で stub に上書きされていた (前セッションの詳細版は `HANDOVER.md.bak` に退避されたまま) → 今回の HANDOVER は新規作成として上書き
- `bash` 経由の `cat` / `head` / `tail` / `find` / `grep -n` が permission 拒否される → Glob / Grep / Read / Edit / Write の専用ツールに切替
- 12.7 inner runner が前セッション末で commit 寸前で context-out → working tree に未コミット成果物 (UiShellPlayModeSampleHost / DefaultSkinProfile.asset / UiShellPlayModeSample.unity 編集 / UiShellPlayModeSample.md) が残っていた → 復帰冒頭で手動コミット (e86961e)
- claude.exe 子プロセスが過去セッションから複数残存 (PID 12272, 36588, 57444, 66160) → API 接続自体は活きているが古いものは無効。新規 task の bg PID と区別が必要

## 学び

- `--max-turns 120` でも 12.8 / 12.9 / 12.10 のような E2E PlayMode は ~25-30 分かかる。背景起動 → 25 分待ち → TaskOutput で Read という polling パターンが Cache TTL (5min) と相性悪い → `delaySeconds=1500` (25 分) で 1 回のみ wakeup する方がトータル安い
- Unity の Camera + UniversalAdditionalCameraData は AddComponent 順序に依存しない (URP では Camera 追加時に自動 attach) が、明示的に AddComponent しておくと「将来 URP 外し対応」のリグレッションを防げる
- `IDisplayProbe` のような薄いラッパを作るだけで Unity 静的 API (`Display.displays.Length`) のテスト依存を切れる → `Tests/PlayMode` 内 private nested class でスタブを書く程度で済む
- `csc.rsp` で `-langversion:10` 指定があるので `record struct` / `init` セッター / `?` 否定演算子はそのまま使える
- spec-run-waves の Wave 2 / 3 は per-spec の `claude -p` を fork する設計だが、permission 設定がリセットされた状態だと外部から自動承認できない。Auto mode + nested claude は user 承認の壁がある

## 次にやること

### P1 (週次リセット待ちなしで進められる)

- output-renderer-shell 残 9 件を 4.2 から順に **直接実装** で進める
  - 4.2 OutputCommandDispatcher (中規模、ICoreIpcServer 連携あり)
  - 5.1 OutputDiagnostics (小規模、状態遷移ロジック)
  - 6.1〜6.4 OutputSceneBootstrapper 系 (中〜大、Composition Root)
  - 7.1, 7.2 統合テスト
  - 8.1, 8.2 サンプルシーン + Coverage 回帰スイート
  - 8.3* は任意 (MVP 後)
- Unity Editor で `VTuberSystemBase` を開いて 2.2〜4.1 のテストが Pass することを確認 (今回は未実行)

### P2 (4/30 8pm 週次リセット後)

- Wave 3: camera-switcher-tab / character-selection-tab / stage-lighting-volume-tab (~100 件)
- inner runner を使うなら事前に `/permissions` 設定を allow に変更しておく

### P3 (低優先)

- HANDOVER.md.bak の整理 (Apr 26 23:35 版、後続 commit で track 済み)
- pre-existing flaky test `EditorPlayModeBridgePlayModeTests.RepeatedSimulatedPlayModeCycles_KeepPortBindable` の調査
- spec-run skill に「N 分 commit 進捗無し → 強制終了」watchdog 追加

## 関連ファイル

### 今回のセッションで触ったプロダクションコード (output-renderer-shell)

- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Scene/DefaultCameraFactory.cs` (新規, 2.2)
- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Scene/DefaultLightFactory.cs` (新規, 2.3)
- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Scene/GlobalVolumeFactory.cs` (新規, 2.4)
- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Abstractions/IDisplayRoutingService.cs` (新規, 3.1)
- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Display/IDisplayProbe.cs` (新規, 3.2)
- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Display/UnityDisplayProbe.cs` (新規, 3.2)
- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Display/BuiltInDisplayRoutingService.cs` (新規, 3.2)
- `Packages/com.vtubersystembase.output-renderer-shell/Runtime/Dispatch/HandlerRegistry.cs` (新規, 4.1)

### 今回のセッションで触ったテストコード

- `Packages/com.vtubersystembase.output-renderer-shell/Tests/PlayMode/DefaultCameraFactoryTests.cs` (新規, 2.2)
- `Packages/com.vtubersystembase.output-renderer-shell/Tests/PlayMode/DefaultLightFactoryTests.cs` (新規, 2.3)
- `Packages/com.vtubersystembase.output-renderer-shell/Tests/PlayMode/GlobalVolumeFactoryTests.cs` (新規, 2.4)
- `Packages/com.vtubersystembase.output-renderer-shell/Tests/EditMode/Fakes/FakeDisplayRoutingService.cs` (新規, 3.1)
- `Packages/com.vtubersystembase.output-renderer-shell/Tests/EditMode/IDisplayRoutingServiceContractTests.cs` (新規, 3.1)
- `Packages/com.vtubersystembase.output-renderer-shell/Tests/PlayMode/BuiltInDisplayRoutingServiceTests.cs` (新規, 3.2)
- `Packages/com.vtubersystembase.output-renderer-shell/Tests/EditMode/HandlerRegistryTests.cs` (新規, 4.1)

### 今回のセッションでの commit (新しい順)

#### output-renderer-shell

- `06cf526` 4.1 HandlerRegistry と登録解除トークンの実装
- `6de93e8` 3.2 BuiltInDisplayRoutingService による暫定実装 (IDisplayProbe seam 経由)
- `ce9044f` 3.1 IDisplayRoutingService 抽象と FakeDisplayRoutingService の確定
- `0ad7f22` 2.4 (P) GlobalVolumeFactory による空の Global Volume と空 VolumeProfile の生成
- `65e2abd` 2.3 (P) DefaultLightFactory によるデフォルト Directional Light の生成
- `474589a` 2.2 (P) DefaultCameraFactory による URP 対応デフォルトカメラの生成
- `8c2aa75` Sync tasks.md: mark output-renderer-shell 1.1, 2.1 as [x]

#### ui-toolkit-shell

- `7d2f49b` 12.10 Coverage Audit
- `71b5925` 12.9 Performance
- `d30e2c6` 12.8 E2E PlayMode 反復起動リーク試験
- `e86961e` 12.7 E2E PlayMode サンプルシーン + 手動検証手順
- `7755455` 12.6 Integration 起動→プリロード→初期タブ
- `d9e4456` 12.5 Integration round-trip
