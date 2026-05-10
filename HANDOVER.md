# セッション引き継ぎノート

## ◯ 今回やったこと（Wave 3a〜3e の積み残しコンパイルエラー潰しセッション）

- ユーザーから順次提示されるコンパイルエラーを 1 件 / 複数件ずつ修正
- 対象は Wave 3a〜3e で前セッションが書いた新規 7 パッケージ（character / stage / camera タブ × 3、3 アダプタ、integrated-demo）
- Unity Editor / MCP 連携が途中で切れたため、エラー報告ベースの修正サイクルで進行
- 修正カテゴリ別の件数：
  - **構造的漏れ**：Write tool の XML 終端タグ汚染 5 ファイル、`csc.rsp` (-langversion:10) 未配置 27 asmdef、`IsExternalInit.cs` シム未配置 27 asmdef
  - **namespace / type 衝突**：`CameraType` (UnityEngine vs Contracts) 11 + 4 ファイル、`CameraSwitcherOutputAdapter` namespace == type 名 10 ファイル、`Object` (UnityEngine vs object) 数件、`AvatarCatalogEntry` / `SettingSchemaEntry` / `ConnectionStatusCode` (State vs Contracts/Commands) 多数
  - **API 誤用**：URP `VolumeProfile.Add` 引数名 (`overrideState` → `overrides`)、`VolumeProfile.TryGet<T>` 推論不能、`VolumeComponentMenuAttribute` → `VolumeComponentMenu`
  - **asmdef 参照不足**：`UniTask.Addressables` (RAC)、`UCAPI4Unity` (camera-switcher-output-adapter Domain)、3 アダプタ Runtime (integrated-demo Tests.PlayMode)
  - **設計問題**：Domain → Runtime 循環参照を回避するため `Ucapi4UnityFlatRecordApplier.cs` を Domain ディレクトリに物理移動
  - **interface 実装漏れ**：`ICharacterTabStateStore.MarkInteracting` 追加、`FakeUiSubscriptionClient.Token` の `Topic` / `IsActive` 追加
  - **その他**：`File.Move(.., overwrite:true)` の Mono 互換書き換え、`using` 文の `IAsyncDisposable` 回避（`Microsoft.Bcl.AsyncInterfaces` 参照禁止ルール遵守）

## ◯ 決定事項

- **新規 7 パッケージ全 27 asmdef に `csc.rsp`（`-langversion:10`）と `IsExternalInit.cs` を配置**：基盤 3 パッケージと同形式。GUID は `[guid]::NewGuid().ToString('N')` で全件ランダム生成
- **`Ucapi4UnityFlatRecordApplier.cs` を `Runtime/Adapters/Ucapi/` から `Runtime/Domain/` に物理移動**：namespace は `Adapters.Ucapi` のまま維持。Domain asmdef のスコープに含めて Domain → Runtime 循環参照を回避
- **camera-switcher-output-adapter の Domain asmdef に `UcApi4Unity.UnityCamera` / `UcApi.Core` 参照を追加**：Ucapi4UnityFlatRecordApplier の依存解決
- **`CameraSwitcherOutputAdapter` namespace と Domain クラスの同名衝突対策**：`using CameraSwitcherOutputAdapterCore = ...Domain.CameraSwitcherOutputAdapter;` エイリアスを 10 ファイルに先回り適用。ファイルローカル alias で解消、Domain クラス改名はしない
- **`CameraType` (UnityEngine vs Contracts) 衝突対策**：両 package 内で `using UnityEngine;` を持つ全ファイルに `using CameraType = ...Contracts.CameraType;` 追加（adapter 11、tab 4）
- **`AvatarCatalogEntry` エイリアスのターゲット**：テストは `AvatarCatalogPayload.Avatars` (IPC DTO) を作るため **Contracts 側**で固定。State 側 mirror は ICharacterTabStateStore 内部用
- **`SettingSchemaEntry` エイリアスのターゲット**：State 側で固定（typed-defaults resolved 版を Presenter / Factory が消費）
- **`ConnectionStatusCode` エイリアスのターゲット**：`ConnectionStatusEvent.To` の型に合わせて **Commands 側**で固定（State 側は ICharacterTabStateStore.ConnectionStatus 用）
- **Tests.Editor → Tests.PlayMode の internal 共有**：stage-lighting-volume-output-adapter Tests/Editor に `[assembly: InternalsVisibleTo(...PlayMode)]` を追加。Fake クラスを `public` 化はしない
- **`Object.Destroy` / `Object.FindObjectsOfType` は完全修飾 `UnityEngine.Object.*` を統一表記とする**：`using UnityEngine;` 配下で `System.Object` と衝突するため
- **`File.Move(.., overwrite:true)` は使わない**：Mono ランタイム未対応。`if (File.Exists) File.Delete; File.Move` パターンで書く

## ◯ 捨てた選択肢と理由

- **「`Ucapi4UnityFlatRecordApplier` を抽象 `IFlatRecordApplier` interface に切り出す」** → コンストラクタ引数追加が `CameraSwitcherOutputAdapter` 呼び出し元 13 ファイル全部に波及する。物理ディレクトリ移動だけで循環参照は解消できるので、抽象化はゼロコード変更で済む方針を選んだ
- **「`Microsoft.Bcl.AsyncInterfaces` を precompiledReferences に追加して `IAsyncDisposable` を解決」** → CLAUDE.md / 前 HANDOVER で参照禁止ルール明記済み（core-ipc-foundation の System.Text.Json と二重参照リスク）。`SettingValue.ToJson` の `using` 構文を `try/finally` 手書きに書き換える方針を選んだ
- **「Fake クラスを `internal` → `public` に上げる」**（VolumeOverrideHandler PlayMode テスト解決） → Tests.Editor → Tests.PlayMode の `InternalsVisibleTo` 追加 1 ファイルで済むため、Fake クラスの可視性は変えない
- **「Domain クラス `CameraSwitcherOutputAdapter` を改名する」** → namespace と同名で衝突するが、外部 API 改名の影響範囲が大きい。ファイルローカル alias で抑える
- **「`File.Replace(temp, dest, null)` で atomic swap」** → dest 不在時に例外。delete + move の方が単純で要件を満たす（`tempPath` のみが書き手のため race が小さい）
- **「`record struct` を `readonly struct` に書き換える」**（C# 9.0 デフォルトで弾かれる対策） → Wave 3a〜3e で 38 ファイル / 39 個の record struct を一括修正するより、`csc.rsp` を 27 asmdef に配置する方が低リスク。基盤 3 パッケージと同手段
- **「全テスト asmdef で Fake をリファクタして共通 asmdef に切り出す」** → 今回のスコープ外。コンパイル復旧優先

## ◯ ハマりどころ

- **HANDOVER.md は Wave 3d 完了で「コンパイルエラー有無は要 Unity 起動確認」と書いてあったが、実際は 30 件以上のエラーが累積していた**：Write tool の XML 終端汚染（5 ファイル）、csc.rsp 配置漏れ（全 27 asmdef）、IsExternalInit 配置漏れ（全 27 asmdef）が Wave 3a で見落とされた構造的問題。前回の自分が「Unity 起動して確認」を P0 に積んでいたのは正しかったが、コンパイルが通る前提で書いた HANDOVER は楽観的すぎた
- **`namespace VTuberSystemBase.CameraSwitcherOutputAdapter` と Domain クラス `CameraSwitcherOutputAdapter` が同名**：CS0118「namespace を type として使ってる」が 10 ファイルから出る原因。最初の 1 件で気付いて全件先回りすれば修正回数を減らせた
- **`AvatarCatalogEntry` / `SettingSchemaEntry` / `ConnectionStatusCode` が State / Contracts / Commands で重複定義**：エイリアス追加で潰すたびに「どちらに固定するか」の判断ミスをして 2 周修正したケースあり（State → Contracts に変更、State → Commands に変更）。代入先の型を先に確認する習慣が必要
- **Unity 6 / URP 17 の API 細部変更**：`VolumeProfile.Add(Type, bool overrides)`（旧 `overrideState`）、`VolumeProfile.TryGet<T>` ジェネリック必須（`out var` で推論不能）、`VolumeComponentMenu`（`Attribute` サフィックスなし）。コーディング時の API 名の取り違え
- **`UniTask.Addressables` は別 asmdef + `autoReferenced:true` だが overrideReferences:true 側からは見えない**：RAC Runtime asmdef が `overrideReferences:true` を持っているため、autoReference が無効化されて `ToUniTask<T>(this AsyncOperationHandle<T>)` が解決できず非ジェネリック版にマッチして「void を var に代入」エラーになっていた

## ◯ 学び

- **Wave 3a で Contracts asmdef を切り出した時点で、基盤 3 パッケージと同形式の `csc.rsp` + `IsExternalInit.cs` をテンプレートコピーする運用ルールが必要**：単発のコピー忘れが 27 asmdef に波及する
- **Write tool で書き出すコードに `</content>` `</invoke>` などの harness タグが混入する事故が起きうる**：書き込み後は `git diff` の末尾を必ず目視確認する
- **namespace と type 名を意図的に同名にする設計は避けるべき**：`VTuberSystemBase.CameraSwitcherOutputAdapter` namespace と Domain `CameraSwitcherOutputAdapter` クラスは長期的負債。次の機会で `CameraSwitcherOutputAdapterCore` に rename を検討
- **State / Contracts mirror パターンを取る場合、エイリアスのターゲットは「代入先の型」で決まる、と最初から認識する**：Production と Tests で代入先が違うこともある（Production: `ICharacterTabStateStore` 経由 = State 側、Tests: `Catalog Payload` 経由 = Contracts 側）
- **Mono ランタイムは .NET 5+ API を全部はサポートしていない**：`File.Move(.., overwrite)` / `IAsyncDisposable` などの「.NET Core / .NET 5+」シグネチャを使う前に Unity 動作確認を取るべき。エラーが出た時点での書き換えコストは低いので予防回避は不要だが、レビュー時のチェックポイントにはなる

## ◯ 次にやること

### P0（最優先・残コンパイルエラー潰し）

- ユーザーから報告される残エラーを継続して 1 件ずつ修正
- 全エラー潰し完了後、Unity Editor を起動してフルコンパイル成功を確認
- MCP 連携が回復したら `mcp__unity-mcp__Unity_ReadConsole` でエラーリストを直接取得

### P1（コンパイル成功後・Wave 3d/3e の手動検証フェーズ）

- 前セッション HANDOVER の P0 に戻る：
  - `Assets/Scenes/MainDemo.unity` を `jp.hidano.vtuber-system-base.integrated-demo/Samples~/IntegratedDemo/README.md` 手順で構築
  - L6 PlayMode 検証（Display 1 = タブバー + 3 タブ、Display 2 = キャラ/ステージ/ライト/カメラ反映）
  - L7 Standalone ビルド検証（実 2 画面 + OBS Display Capture）

### P2（中優先・修正に関する積み残し）

- **`output-renderer-shell/package.json` の dependencies に `com.hidano.runtime-display-selector` 0.1.1 を追加**：UPM 配布時の依存解決のため。完了判定 5 違反。今回は触っていない（ローカル動作には影響なし）
- **`Tests/Editor/AssemblyInfo.cs` を他 spec にも展開**：今回は stage-lighting-volume-output-adapter のみ。他 spec で同様に Tests.Editor → Tests.PlayMode の internal 共有が必要なら同パターン適用
- **`VTuberSystemBase.CameraSwitcherOutputAdapter` namespace × `CameraSwitcherOutputAdapter` Domain クラスの命名衝突を rename で根治**：今回はファイルローカル alias で抑えただけ
- **3 アダプタの `ICoreIpcBusProvider` interface 統一リファクタ**（前 HANDOVER から継続）

### P3（既知の制約・スコープ外）

- 9 パッケージの OpenUPM / npm registry 公開
- Wave 4: PVW/PGM、WebUI、タイムライン録画リプレイ

## ◯ 関連ファイル

### 今回触った主要ファイル

#### 構造的補完（27 asmdef × 2 種類のシム）

- `Packages/jp.hidano.vtuber-system-base.{character-selection-tab, stage-lighting-volume-tab, camera-switcher-tab, rac-main-output-adapter, stage-lighting-volume-output-adapter, camera-switcher-output-adapter, integrated-demo}/{Runtime/...,Tests/...,Editor}/csc.rsp` (+`.meta`) — 27 ファイル
- 同 27 asmdef ディレクトリの `IsExternalInit.cs` (+`.meta`) — 27 ファイル

#### XML タグ汚染除去（output-renderer-shell Wave 3e）

- `Packages/com.hidano.vtuber-system-base.output-renderer-shell/Runtime/Abstractions/DisplayRoutingProvider.cs`
- `Packages/com.hidano.vtuber-system-base.output-renderer-shell/Runtime/Display/RuntimeDisplaySelectorRoutingService.cs`
- `Packages/com.hidano.vtuber-system-base.output-renderer-shell/Tests/EditMode/RuntimeDisplaySelectorRoutingServiceTests.cs`
- `Packages/com.hidano.vtuber-system-base.output-renderer-shell/Tests/EditMode/Fakes/FakeRuntimeDisplaySelectorBridge.cs`
- `Packages/com.hidano.vtuber-system-base.output-renderer-shell/Tests/PlayMode/OutputSceneBootstrapperRdsRoutingTests.cs`

#### 設計修正

- `Packages/jp.hidano.vtuber-system-base.camera-switcher-output-adapter/Runtime/Adapters/Ucapi/Ucapi4UnityFlatRecordApplier.cs` → `Runtime/Domain/Ucapi4UnityFlatRecordApplier.cs`（物理移動）
- `Packages/jp.hidano.vtuber-system-base.camera-switcher-output-adapter/Runtime/Domain/VTuberSystemBase.CameraSwitcherOutputAdapter.Domain.asmdef`（references に UCAPI4Unity 追加）
- `Packages/jp.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/VTuberSystemBase.RacMainOutputAdapter.Runtime.asmdef`（references に UniTask.Addressables 追加）
- `Packages/jp.hidano.vtuber-system-base.integrated-demo/Tests/PlayMode/VTuberSystemBase.IntegratedDemo.Tests.PlayMode.asmdef`（references に 3 アダプタ Runtime 追加）
- `Packages/jp.hidano.vtuber-system-base.stage-lighting-volume-output-adapter/Tests/Editor/AssemblyInfo.cs`（新規、PlayMode への InternalsVisibleTo）

#### namespace / type / interface 修正（多数。代表的なもの）

- camera-switcher-output-adapter / camera-switcher-tab：`CameraType` 衝突 alias 追加 15 ファイル
- camera-switcher-output-adapter：`CameraSwitcherOutputAdapter` namespace × type 衝突 alias 追加 10 ファイル
- character-selection-tab：`AvatarCatalogEntry` / `SettingSchemaEntry` / `ConnectionStatusCode` alias 追加 6 ファイル
- character-selection-tab：`Runtime/State/ICharacterTabStateStore.cs`（`MarkInteracting` 追加）
- character-selection-tab：`Runtime/State/SettingValue.cs`（`using` 構文を `try/finally` 書き換え）
- character-selection-tab：`Runtime/Services/DynamicSettingControlFactory.cs`（State alias 追加）
- character-selection-tab：`Runtime/Services/PresetRestoreOrchestrator.cs`（Commands alias 追加）
- camera-switcher-tab：`Runtime/Adapters/Ucapi/UnityTimeProvider.cs`（`UnityEngine.Time.timeAsDouble`）
- camera-switcher-tab：`Tests/Runtime/TestDoubles/FakeUiSubscriptionClient.cs`（Topic / IsActive 追加）
- stage-lighting-volume-output-adapter：`Runtime/Volume/VolumeOverrideHandler.cs`（`overrides:` 引数名）
- stage-lighting-volume-tab：`Runtime/Runtime/Services/JsonPresetStorage.cs`（File.Move 書き換え）
- camera-switcher-output-adapter：`Runtime/Adapters/Volume/{ReflectionVolumeOverrideSchemaResolver, GlobalEnabledLocalVolumeBinder}.cs`（URP API 修正）
- camera-switcher-output-adapter：`Runtime/Abstractions/CameraSwitcherOutputAdapterConfig.cs`（const と property の同名衝突解消）
- rac-main-output-adapter：`Runtime/Bootstrapper/RacMainOutputAdapterHost.cs`（UnityEngine.Object 修飾）
- integrated-demo：`Runtime/IntegratedDemoBootstrap.cs`（`Abstractions.OutputSceneInitPhase`）
- integrated-demo：`Runtime/IntegratedDemoUiShellHost.cs`（LogLevel alias）
- integrated-demo：`Runtime/IntegratedTabMountStrategy.cs`（using Skin 追加）

### 参照（既存・触っていない）

- `docs/integration-plan.md` — 統合開発計画 v1.0
- `docs/requirements.md` — VTuberSystemBase 要件定義書
- 前回の `HANDOVER.md`（同ファイル、本ノートで上書き）— Wave 3d 統合 Bootstrap セッション内容
