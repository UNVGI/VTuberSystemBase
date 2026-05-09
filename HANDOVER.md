# セッション引き継ぎノート

## ◯ 今回やったこと

- リポジトリ全体を調査し、基盤 3 パッケージ（core-ipc-foundation / output-renderer-shell / ui-toolkit-shell）が tasks 完了済み、タブ 3 spec が未着手という現状を確定
- **重要発見**: 3 タブ spec はすべて UI 側のみが責務で、IPC を受信して Display 2 に反映する「メイン出力側アダプタ」spec が `.kiro/specs/` に存在しない（タブを完成させても Display 2 に何も映らない）
- `docs/integration-plan.md` を作成（Wave 3a〜3e + Wave 4 のロードマップ、結節点 IPC topic 一覧、検証ピラミッド L1〜L7、オープンイシュー含む）
- RuntimeDisplaySelector v0.1.1 が manifest 取り込み済み・API 完成済みであることを確認し、Wave 4（外部待ち）→ Wave 3e（並行可）に訂正
- **Wave 3a 実行**: 3 タブの Contracts asmdef を切り出し
  - `character-selection-tab` Contracts: 約 22 ファイル（自分で実装）
  - `stage-lighting-volume-tab` Contracts: 38 ファイル（Agent 並行）
  - `camera-switcher-tab` Contracts: 28 ファイル（Agent 並行）

## ◯ 決定事項

- **統合計画 4 Wave 構成**: 3a（Contracts 切り出し ✅完了）→ 3b（タブ UI 実装）→ 3c（メイン出力アダプタ実装）→ 3d（統合シーン）→ 3e（RDS 連携、3c と並行可）
- **メイン出力側アダプタは新規 3 spec 起票が必要**: `rac-main-output-adapter` / `stage-lighting-volume-output-adapter` / `camera-switcher-output-adapter`
- **Contracts asmdef 設定**: `overrideReferences:true` + `precompiledReferences` (System.Text.Json 等) を core-ipc 同梱方針に揃える。`references` に core-ipc Abstractions の GUID `286be82527bb75547a774598be8243ab` を入れて `init` プロパティの `IsExternalInit` を解決
- **Engine 参照**: character/camera は `noEngineReferences:true`（純 DTO のみ）。stage のみ `false`（`IPreviewHostService` で `RenderTexture` を扱う）
- **Topic Safe ヘルパ**: ASCII 英数 + `-_.` 以外を percent-encode する `Safe()` を 3 パッケージで統一実装（CharacterTopics.Safe / CameraIpcTopics.Safe / stage は固定 topic 中心で動的部分は呼出元責務）
- **`.meta` の方針**: パッケージルート / フォルダ / asmdef / package.json の `.meta` のみ事前生成 GUID を割当て、個別 `.cs.meta` は Unity 自動生成に任せる
- **GUID は都度ランダム生成**: PowerShell `[guid]::NewGuid().ToString('N')` でまとめて生成。連続パターン・派生 GUID は禁止（CLAUDE.md ルール）

## ◯ 捨てた選択肢と理由

- **「RDS 連携を Wave 4 で後回し」** → 現物 v0.1.1 確認したら Facade `RuntimeDisplaySelector.Current` から Spout 統合まで完成済み。Wave 3e に格上げ
- **「Contracts を別パッケージに切り出す」** → タブパッケージ内で Contracts asmdef を切り出す方が依存関係が単純。UI 側 Runtime asmdef も出力アダプタ側 asmdef も同パッケージ内 Contracts を参照すれば良い
- **「DTO を `init` 不使用の通常セッターにする」** → DTO immutability を保つため `init` 維持。`IsExternalInit` 不在問題は core-ipc Abstractions 参照で解決（同型が定義済み）
- **「個別 `.cs.meta` を全部書く」** → タブ 3 つで 80+ ファイルとなり量が膨大。Unity 自動生成で十分
- **「stage の Contracts も `noEngineReferences:true` にして `IPreviewHostService` を Runtime に置く」** → Locator パターンが UI 側と出力側アダプタの両方から参照される必要があるため、Contracts に置いて engine 参照ありで運用
- **「タブ spec の design.md 通りに Topic / DTO を Runtime asmdef に同居」** → character spec はそうだったが、UI 側と出力側アダプタの並行開発を可能にするため Contracts asmdef に切り出す方針に統一（design.md とディレクトリ構造が微妙にずれるが、設計意図の方を優先）

## ◯ ハマりどころ

- **`docs/requirements.md` の古い記述（RuntimeDisplaySelector「並行開発中、当面は後回し」）を踏襲して Wave 4 と書いてしまった**。ユーザー指摘で現物確認 → v0.1.1 完成済みと判明。ドキュメント記述だけで判断せず PackageCache の中身を必ず見るべき
- **stage-lighting-volume-tab の Agent が `find` コマンドを使用しセキュリティ警告**（ユーザーの deny rule 違反）。成果物自体は仕様通りで使えるが、Agent への指示で Bash 系コマンド制限を明示すべきだった
- **`init` プロパティの `IsExternalInit` 問題**: Unity 6 .NET Standard 2.1 では `IsExternalInit` が BCL に存在せず polyfill 必須。core-ipc-foundation は `Abstractions/IsExternalInit.cs` で自前定義済み。タブ Contracts は core-ipc Abstractions を `references` に入れることで transitively 解決

## ◯ 学び

- **タブ spec の design.md は Out of Boundary が極めて明確**。「メイン出力側アダプタは別 spec」と書いてあるので、別 spec が存在するかを `.kiro/specs/` で必ず確認する
- **Contracts asmdef の切り出しは Wave 3 全体のスループットを上げる**。UI 実装と出力アダプタ実装が並行可能になる
- **design.md の Contracts セクションは具体的な C# コード例を提供するため、Agent への指示として優秀**。「L634〜901 のとおりに DTO を作る」と指示するだけで成果物が揃う
- **Wave 3a は 3 タブ並行で 1 セッション内に完了可能**。character を自分でリファレンス実装 → stage / camera を Agent に並行で投げる戦略が有効

## ◯ 次にやること

### P0（最優先・並行 4 トラックで「今晩で一気に」進められる）

- **トラック B**: メイン出力アダプタ 3 spec の起票
  - `/kiro:spec-init "rac-main-output-adapter: character-selection-tab の IPC を受信し RealtimeAvatarController を駆動するメイン出力側アダプタ"`
  - `/kiro:spec-init "stage-lighting-volume-output-adapter: stage-lighting-volume-tab の IPC を受信し Light/Volume/Stage を実シーン反映するメイン出力側アダプタ"`
  - `/kiro:spec-init "camera-switcher-output-adapter: OSC 受信→UCAPI デコード→Camera 適用するメイン出力側アダプタ"`
  - 各 spec とも Contracts asmdef（既存）を参照して受信ハンドラを `IOutputCommandDispatcher.RegisterStateHandler` に登録する設計
- **トラック C**: タブ UI 実装の `/kiro:spec-run` バッチ実行（モック注入で完結する）
  - `/kiro:spec-run character-selection-tab`
  - `/kiro:spec-run stage-lighting-volume-tab`
  - `/kiro:spec-run camera-switcher-tab`
- **トラック D（独立）**: `output-renderer-shell` に `RuntimeDisplaySelectorRoutingService` を追加し `BuiltInDisplayRoutingService` から差し替え（Wave 3e）。Klak Spout 経由の OBS 送出経路を整備

### P1（中優先）

- Unity を起動して 3 タブ Contracts パッケージのコンパイル確認（Library 削除推奨）
- `docs/integration-plan.md` の §7.2 オープンイシューを Wave 3b 着手前に潰す（特に OSC アドレス・ポート最終確定、Addressables Group 構成）

### P2（低優先・スコープ外）

- Wave 3d: `Assets/Scenes/MainDemo.unity` の統合シーン構築（Wave 3b/3c 完了後）
- Wave 4: PVW/PGM、WebUI、タイムライン録画リプレイ（次フェーズ）

## ◯ 関連ファイル

### 新規作成（Wave 3a）

- `docs/integration-plan.md` — 統合開発計画
- `VTuberSystemBase/Packages/jp.hidano.vtuber-system-base.character-selection-tab/` 配下 ~22 ファイル
  - `package.json`、`Runtime/Contracts/VTuberSystemBase.CharacterSelectionTab.Contracts.asmdef`
  - `Runtime/Contracts/Topics/CharacterTopics.cs`
  - `Runtime/Contracts/Payloads/{Slot,Avatar}*.cs` + `SettingType.cs`
- `VTuberSystemBase/Packages/jp.hidano.vtuber-system-base.stage-lighting-volume-tab/` 配下 38 ファイル
  - `Runtime/Contracts/{Topics,Dtos,Preview,Presets}/`
- `VTuberSystemBase/Packages/jp.hidano.vtuber-system-base.camera-switcher-tab/` 配下 28 ファイル
  - `Runtime/Contracts/{CameraId,CameraType,OscAddressBuilder,CameraIpcTopics}.cs`
  - `Runtime/Contracts/Payloads/`

### 参照（既存）

- `docs/requirements.md` — VTuberSystemBase 要件定義書（RDS の記述は古い、注意）
- `docs/spec-breakdown.md` — kiro spec 切り分け計画（v1.0、初版）
- `.kiro/specs/{character-selection-tab,stage-lighting-volume-tab,camera-switcher-tab}/design.md` — Contracts 切り出しの根拠
- `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.core-ipc-foundation/Runtime/Abstractions/` — Contracts asmdef のテンプレート（参照 GUID: `286be82527bb75547a774598be8243ab`）
- `VTuberSystemBase/Library/PackageCache/com.hidano.runtime-display-selector@406b0084630f/` — Wave 3e で使う RDS v0.1.1（Facade、Spout、Persistence、Win32 全完備）
