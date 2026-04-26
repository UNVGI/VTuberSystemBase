# Requirement Coverage Audit (task 12.10)

本書は `.kiro/specs/ui-toolkit-shell/tasks.md` の **task 12.10 (Coverage Audit)** に
対応する任意サンプリング検証記録である。Requirement 1.x〜11.x から無作為に 5 件を
選び、該当タスクの成果物（実装コードおよびテスト）が tasks.md で宣言した「観測可能
な完了状態」を満たしているかを目視で点検する。併せて
`ShellDiagnosticsSnapshot.Capture()` が PlayMode 実機ブートストラップ条件下でも
全フィールドを埋めて返ることを `UiShellPlayModeDiagnosticsSnapshotAuditTests` で
固定する（Requirement 3.7, 4.9, 11.9, 10.5）。

## サンプリング手続

- **対象母集団**: Requirement 1.1 〜 11.9（全 71 件のうち、tasks.md
  Requirements Coverage Summary で具体タスクに割り付けられている全 ID）。
- **抽出方法**: 各 Requirement カテゴリ（1, 3, 4, 6, 9）から 1 件ずつ、合計 5 件を
  事前に固定して抽出した（仕様書末尾の summary 表を縦に走査し、可観測効果が異なる
  項目を選ぶ）。サンプル ID は監査時点（2026-04-27）に固定し、後日の再走査でも
  同じ表が再現できるよう以下に列挙する。
- **判定基準**: 各 Requirement の文言に対して、(a) 当該 acceptance criterion を
  実装する Runtime コード、(b) その振る舞いを assertion で固定する単体／結合
  テストが両方とも存在し、CI で実行可能（`UiToolkitShell.Tests.asmdef`）であれば
  `合格` とする。

## サンプリング検証表

| Req ID | 観測対象 | 実装コード（成果物リンク） | 検証テスト（成果物リンク） | 確認結果 |
| --- | --- | --- | --- | --- |
| 1.7 | ルート UIDocument を Display 1 (`targetDisplay = 0`) に固定し、Display 2+ には一切描画しない | `Runtime/Panels/RootUiDocumentBuilder.cs` (CreateSharedPanelSettings, 0 強制 + 警告ログ); `Runtime/Bootstrap/IDisplayAssignmentStrategy.cs::FixedDisplayZeroStrategy` | `Tests/Runtime/RootUiDocumentBuilderTests.cs::CreateSharedPanelSettings_RequestNonZero_ForcesZero_AndWarnsOnLifecycle`; `Tests/Runtime/DisplayAssignmentStrategyTests.cs::FixedDisplayZeroStrategy_ResolveTargetDisplay_AlwaysReturnsZero`; 同 `StartShell_DefaultStrategy_PinsTargetDisplayToZero_EvenWhenRequestedNonZero` | 合格（task 8.1, 10.3 の観測条件「`targetDisplay = 0` 強制 + 非ゼロ要求時の警告ログ」を 3 件のテストで多重に固定） |
| 3.5 | プリロード対象が一部失敗しても他タブの起動を継続し、該当タブのみ失敗扱いとする | `Runtime/Panels/TabPanelRegistry.cs::MarkTabFailed`（失敗 ID 記録 + 完了カウントへ加算 + Skin カテゴリログ）；`GetPreloadProgress` が `FailedTabs` を返す | `Tests/Runtime/TabPanelRegistryTests.cs::MarkTabFailed_OneTab_RecordedAndCountedTowardLoaded`; 同 `TwoMountsPlusOneFailure_IsCompleteWithFailureRecorded` | 合格（task 8.2 観測条件「失敗 1 件注入でも `IsPreloadComplete == true` かつ `FailedTabs` に該当 ID」が両テストで明示検証） |
| 4.7 | 同一 key の重複ロード要求を 1 本のハンドルに集約し、両 callback に Completion を配信する | `Runtime/AssetLoading/AddressablesAssetLoader.cs`（`_entries` キャッシュで `(key, type)` 単位の共有、CompletionGate で多重通知制御） | `Tests/Runtime/AddressablesAssetLoaderTests.cs::LoadAsync_SameKeyTwice_DedupsToSingleSharedEntry_EmitsOneAssetUnloadedLog`; 同 `Cancel_OneOfDuplicateHandles_DoesNotAffectTheOther` | 合格（task 5.2 観測条件「同一 key 連続呼出が 1 ハンドルに集約され、片方 Cancel が他方に波及しない」を直接検証） |
| 6.5 | タブ UXML の必須クラス欠落を起動時に検出し、診断ログ (`LogCategory.Skin`) へ記録する | `Runtime/Skin/SkinValidator.cs::Validate`（ルート + 各タブ別必須クラス走査と Issue 蓄積）；`Runtime/Skin/SkinValidationRules.cs`（必須セレクタ定数群） | `Tests/Runtime/SkinValidatorTests.cs::Validate_RootMissingTabBar_AppendsIssueWithNullTabId`; 同 `Validate_TabMissingRequiredModifier_AppendsIssueWithThatTabId` | 合格（task 6.3 / 12.4 観測条件「欠落検出テストと正常テストの両方が緑、ログが Skin カテゴリで残る」を 2 件のテストで成立） |
| 9.4 | Command 送信 API が接続未確立時に例外を投げず `SendError.NotConnected` を即時返す | `Runtime/Commands/UiCommandClient.cs`（`!IsConnected` で `SendError.NotConnected` を生成）；`Runtime/Commands/SendResult.cs`（`SendErrorCode.NotConnected`） | `Tests/Runtime/UiCommandClientContractTests.cs::PublishState_WhenNotConnected_ReturnsNotConnected_NoException`; 同 `PublishEvent_WhenNotConnected_ReturnsNotConnected_NoException` | 合格（task 4.4 / 9.3 観測条件「接続未確立で `SendError.NotConnected` を返し UI に例外が波及しない」を 2 件のテストで明示） |

## 診断 API 実機検証

`ShellDiagnosticsSnapshot` の全 6 フィールド (`Preload`, `AssetLoad`,
`ConnectionStatus`, `ActiveSubscriptionCount`, `ActiveTab`, `CapturedAt`) が
PlayMode 実機ブートストラップ後に「埋まった状態」で返ることを以下の固定テストで
担保する（Requirement 3.7, 4.9, 11.9; design.md §Diagnostics §ShellDiagnosticsSnapshot）。

- `Tests/PlayMode/UiShellPlayModeDiagnosticsSnapshotAuditTests.cs::Capture_OnLiveBootstrappedShell_PopulatesAllFields`
  - `UiShellLifecycleDriver.StartShell()` で本物の `UiShellBootstrapper`
    から `TabPanelRegistry` / `AddressablesAssetLoader` / `ConnectionStatus`
    / `UiSubscriptionClient` を取得し、`ShellDiagnosticsSnapshotProvider` に
    束ねた上で `Capture()` を呼ぶ。
  - 6 フィールドすべてに対して assertion を行う:
    - `Preload`: `LoadedCount` / `TotalCount` / `FailedTabs` が初期化済み
      （3 タブ Mount → `LoadedCount == 3`, `FailedTabs` は空）。
    - `AssetLoad`: `PendingByScope` が非 null、`PendingCount` /
      `CompletedCount` / `FailedCount` が非負値。
    - `ConnectionStatus`: `ConnectionStatusCode` の宣言済み値 (Initializing
      など) のいずれか。
    - `ActiveSubscriptionCount`: 検証用の購読 2 件を実際に登録し、Capture
      時点で `>= 2` を直接観測。
    - `ActiveTab`: `TabId.Character`（`InitialTab` で固定）。
    - `CapturedAt`: テスト前後の壁時計レンジに収まる（`DefaultClock` の
      `DateTimeOffset.UtcNow` 経路が動作している証拠）。

## 監査結論

- 抽出 5 件すべて、対応する Runtime コードと assertion 付きテストの両方が存在し、
  「観測可能な完了状態」（tasks.md 各タスク末尾）を満たしている。
- `ShellDiagnosticsSnapshot.Capture()` の全フィールド埋めも実機 PlayMode
  シナリオで確認済み。
- 追加で見つかった問題: なし。
- フォローアップ: なし（`tasks.md` 12.10 の観測条件を満たすため、本表を完成版
  として確定する）。
