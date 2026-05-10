# Overnight 引き継ぎノート（rac-movin-mocap-factory + MOVIN 実機動作）

> 作成: 2026-05-11 / 担当: Claude Code (Opus 4.7) overnight 自律実行
> 用途: 朝起きたユーザーが、実装結果を引き取って残作業を進めるためのチェックリスト

## 1. 自動で完了したはずの作業

`/kiro:spec-run rac-movin-mocap-factory` 相当のバッチを別エージェントで実行しました。完了タスクは `.kiro/specs/rac-movin-mocap-factory/tasks.md` のチェックボックスで確認できます（`- [x]` が完了、`- [ ]` が未完）。

実装で触ったはずのファイル一覧：

- 新規パッケージ: `VTuberSystemBase/Packages/com.hidano.vtuber-system-base.rac-movin-mocap-factory/`
  - `package.json` + `.meta`
  - `Runtime/` (asmdef, AssemblyInfo, IsExternalInit, MovinMoCapSourceConfigFactory.cs, MovinMoCapSourceConfigFactoryProvider.cs)
  - `Tests/EditMode/` (asmdef, MovinMoCapSourceConfigFactoryTests.cs, MovinMoCapSourceConfigFactoryProviderTests.cs)
- 既存編集:
  - `Packages/com.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/ExtensionPoints/IMoCapSourceConfigFactoryProvider.cs`（新規）
  - `Packages/com.hidano.vtuber-system-base.rac-main-output-adapter/Runtime/Bootstrapper/RacMainOutputAdapterHost.cs`（DI seam 追加）
  - `Packages/com.hidano.vtuber-system-base.integrated-demo/package.json`（dependency 追加）
  - `Packages/com.hidano.vtuber-system-base.integrated-demo/Runtime/IntegratedDemoBootstrap.cs`（自動配線 + reflection 注入）
- インフラ系編集:
  - `VTuberSystemBase/Packages/manifest.json`（uloop bridge `io.github.hatayama.uloopmcp@2.1.1` を追加）

`git log --oneline -20` でタスクごとのコミットメッセージ（タスク ID をタイトルにしたもの）が並んでいるはず。

## 2. 朝イチでやってほしい確認

### 2-1. Unity コンパイル

Unity Editor (VTuberSystemBase) を前面に出して、`Console` のエラーを確認。

```powershell
uloop compile --project-path "D:/Unvgi/Repositries/VTuberSystemBase/VTuberSystemBase"
```

エラーが出ていたら、最も多いのは:
- `IMoCapSourceConfigFactoryProvider` の参照不足 → `rac-movin-mocap-factory.asmdef` の `references` を確認
- `MovinMoCapSourceFactory.MovinSourceTypeId` の参照失敗 → `realtimeavatarcontroller.movin` パッケージへの依存を確認

### 2-2. EditMode テスト

```powershell
uloop run-tests --project-path "D:/Unvgi/Repositries/VTuberSystemBase/VTuberSystemBase" --test-mode EditMode --filter "MovinMoCap"
```

期待結果: `MovinMoCapSourceConfigFactoryTests`（6 件）+ `MovinMoCapSourceConfigFactoryProviderTests`（4 件）が PASS。

回帰確認:
```powershell
uloop run-tests --project-path "D:/Unvgi/Repositries/VTuberSystemBase/VTuberSystemBase" --test-mode EditMode
```

`rac-main-output-adapter` の既存テスト群が一切 fail しないこと。

## 3. MainDemo シーンに MOVIN Provider を取り付ける

`IntegratedDemoBootstrap` 側で AddComponent するロジックを追加したので、シーンを Play すれば自動で MOVIN Provider が `IntegratedDemoRoot` に着くはず。Inspector 値を変えたい場合のみ Edit モードで明示的に AddComponent しておく。

```
1. MainDemo シーンを開く
2. IntegratedDemoRoot を選択
3. Inspector → Add Component → "VTuberSystemBase/RAC MOVIN MoCap Factory Provider"
4. port = 11235（既定）/ rootBoneName / boneClass を必要に応じて編集
```

### Edit モードからの確認（uloop 経由）

```powershell
uloop execute-dynamic-code --project-path "..." --code "Selection.activeObject = GameObject.Find(\"IntegratedDemoRoot\");"
```

## 4. **本番フル E2E に必須の残タスク**（自動化できなかったもの）

`MainDemo.unity` を実際に MOVIN モーションで動かすには、以下が**まだ未整備**です。これらは設計上の判断 / アセット用意が必要なのでオーバーナイトでは触っていません。

### 4-1. UI Toolkit Skin Profile + 4 種 UXML

`Samples~/IntegratedDemo/README.md` §2 参照。
- `Assets/UI/IntegratedDemoSkinProfile.asset`
- `Assets/UI/IntegratedDemo_Root.uxml`
- `Assets/UI/IntegratedDemo_CharacterTab.uxml`
- `Assets/UI/IntegratedDemo_StageTab.uxml`
- `Assets/UI/IntegratedDemo_CameraTab.uxml`

未設定だと `IntegratedDemoBootstrap` は SkinProfile null を検知して `[IntegratedDemoBootstrap] SkinProfile not set ... skipping UI shell startup` を出力し、UI なしで main-output アダプタのみ起動します（MOVIN は流れます、UI からの操作だけが無効）。

### 4-2. Addressables Group

`Samples~/IntegratedDemo/README.md` §4 参照。最小:
- Group `Avatars`: `avatars/sample-avatar` (Humanoid 設定済み VRM Prefab)
- Group `Stages`: `stages/sample-stage` (Cube + Plane)
- Address `vtuber-system-base/character/default-avatar-thumbnail`: 64x64 Texture2D

これが無いと `IAvatarKeyResolver` がアバターを解決できないため、Slot に MOVIN Source は接続されても適用先アバターが空になる。

### 4-3. Humanoid アバター

VRM など Animator + Humanoid Avatar 構成済み Prefab を 1 体用意し、Addressables Group `Avatars` に登録 (`avatars/sample-avatar`)。

### 4-4. MOVIN Studio 側設定

- 送信先 IP: VTuberSystemBase が動いている PC の IPv4
- 送信ポート: **11235**（Provider の port を変えたなら同じ値）
- プロトコル: OSC (uOSC 互換, UDP)
- 送信開始

## 5. デバッグ Tips

PlayMode 起動後の Console で以下が出れば成功:
1. `[CoreIpc.RuntimeBootstrap] CoreIpcRuntime initialization completed.`
2. `[IntegratedDemoBootstrap] Awake wiring complete (PlayMode integration scaffold ready).`
3. `[RacMainOutputAdapterHost] MoCap factory provider resolved: MovinMoCapSourceConfigFactoryProvider` ← 本パッケージで追加されたログ
4. `[RacMainOutputAdapterHost] Initialize complete` 相当
5. UI シェルが立ち上がっていれば `UiShellBootstrapper: shell running.`

MOVIN の OSC が届いていないとき:
- `[MovinMoCapSource]` 系の警告/エラーがあれば bind に失敗している（ポート競合・ファイアウォールなど）
- `netstat -ano | findstr :11235` で受信ソケットの存在を確認
- Windows Defender Firewall が Unity.exe の UDP 11235 受信を許可しているか確認

## 6. 既知の懸念

- **uloop bridge は manifest 追記直後**: 朝起きたとき Unity の package import が完了していなければ、`uloop list/compile/run-tests` が無限に待たされる可能性がある。`uloop fix` でロックを掃除して再試行してください。
- **codex 実行時に rate limit 検出**: 自動フォールバックで `claude -p` に切り替わったタスクがあれば、`tasks.md` のコミットログにそれが記録されているはず（"engine: claude-fallback" 相当の補注を入れる仕様）。
- **オートモード classifier 弾き**: オーバーナイト中、claude 親セッション側で `uloop list` 等のシェル呼び出しがオートモード classifier に弾かれた事象があった。subagent 内では問題なく codex 実行できているはず。朝のコンパイル確認は手動で進めてください。

## 7. 次の spec 候補（時間あれば）

- `integrated-demo-skin-profile-asset`: SkinProfile + 4 UXML を ScriptableObject + Editor menu で生成する spec
- `addressables-bootstrap-pack`: 最小 Avatar/Stage/Thumbnail を生成する Editor ツール

これらは MOVIN 接続そのものではなく「統合シェルの UI 操作」を実機で叩くための前準備。深掘りすればまた一晩仕事です。
