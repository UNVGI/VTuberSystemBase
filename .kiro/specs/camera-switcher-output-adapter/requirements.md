# Requirements Document

## Project Description (Input)
camera-switcher-output-adapter

`camera-switcher-tab`（spec #6）が UI 側から発する **OSC（UCAPI Flat Record）** と **IPC（WebSocket/JSON, core-ipc-foundation）** を、メイン出力シーンの実体（`output-renderer-shell` の `CamerasRoot` 配下に置かれる Unity `Camera` および各カメラに紐づく URP Local Volume）に反映する **メイン出力側アダプタ** を提供する。

責務（高レベル）:
- **OSC 受信ライン**: `uOSC.uOscServer` で `127.0.0.1` の所定ポートに着信する `/ucapi/camera/{cameraId}/flat` メッセージ（60 Hz 目安、UCAPI Flat Record blob）を受信し、`UCAPI4Unity` でデコードして該当 `cameraId` の `Camera` に Position / Rotation / Lens 等を適用する。
- **IPC 受信ライン**:
  - `cameras/active`（state, UI→出力）— 放送中カメラ ID。指定 ID の `Camera` のみ enable（他は disable / culling マスク低下）。
  - `camera/command`（event, UI→出力）— `add` / `delete` / `active-set`。`add` 時はメイン出力側で **cameraId を採番** し `camera/created` で UI に返す。
  - `camera/{id}/metadata/{key}`（state, UI→出力）— `displayName` / `type` / `defaultTransform` 等のメタデータを反映。
  - `camera/{id}/volume/override/{type}/{param}`（state, UI→出力）— Local Volume の Override パラメータを適用。
  - `camera/{id}/volume/override/{type}/enabled`（state, UI→出力）— Override の有効/無効。
  - `camera/{id}/volume/enabled`（state, UI↔出力）— Local Volume 全体の有効/無効。
  - `camera/{id}/volume/command`（event, UI→出力）— `override-add` / `override-remove`。
  - `camera/{id}/volume/overrides/metadata`（request, UI→出力）— 現在の URP VolumeProfile から Override スキーマ（型・レンジ・既定値）を Reflection で抽出して返答。
  - `camera/preset/*`（event/state, UI↔出力）— プリセット関連の通知（永続化は UI 側責務、本 spec はパススルー / 通知受け）。
- **IPC 送信ライン**:
  - `cameras/list`（state, 出力→UI）— 現在のカメラ一覧スナップショットを発行。
  - `cameras/active`（state, 出力→UI、確定値の echo）— 実際に enable された cameraId を権威として返す。
  - `camera/created`（event, 出力→UI）— `camera/command add` の `clientRequestId` に対する採番済み cameraId。
  - `camera/error`（event, 出力→UI）— 操作失敗時。
  - `camera/{id}/preview/handle`（state, 出力→UI）— プレビュー RenderTexture のハンドルキー。

`output-renderer-shell` の `IOutputCommandDispatcher` に IPC ハンドラを登録し、`IOutputSceneRoots.Cameras` 配下に Camera GameObject を生成・管理する。OSC は `core-ipc-foundation` の WebSocket/JSON とは別チャネルで、`uOSC.uOscServer` を本 spec が所有する独立 GameObject に attach する。

採用パッケージ:
- `com.hidano.ucapi4unity` — `UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` で Flat Record を Camera に適用。
- `com.hidano.uosc` v1.0.0+ — `uOscServer.onDataReceived(Message)` を受け取る。`Message.values[0]` は `byte[]`（blob）。

参照する Contracts:
- `com.hidano.vtuber-system-base.camera-switcher-tab` パッケージ内の `Runtime/Contracts/` asmdef（`VTuberSystemBase.CameraSwitcherTab.Contracts`、GUID は当該 asmdef を参照）。`CameraIpcTopics` / `CameraId` / `CameraType` / `OscAddressBuilder` / `Payloads/*` を本 spec の Runtime asmdef がそのまま参照する。

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応。
言語: 日本語（CLAUDE.md の規約に従う）。

対応する上流要件: `docs/requirements.md` §5.3.3 第 4 項（適用）、§5.3.5（カメラ切替時の Local Volume 自動連動）、§6.1（メイン出力フレームへの非干渉）、§6.2（メイン出力に UI/警告を描画しない）。
上位計画: `docs/integration-plan.md` Wave 3c（メイン出力側アダプタ実装）、§3.1 Camera 結節点表。

上流決定の継承:
- **D-1**: 単一 Unity アプリ + LocalHost。
- **D-3**: IPC 受信コールバックは Unity メインスレッド。OSC 受信も Unity メインスレッドに marshal してから Camera を更新。
- **D-4**: メイン出力側はサーバ。OSC は本 spec のスコープ内で `uOscServer`（受信サーバ）。
- **D-5 / D-7 / D-10**: IPC は state coalesce / event FIFO / request-response の規律を継承。
- **D-9**: PlayMode 限定常駐。Edit モードでは起動しない。
- **OR-1**: メイン出力に UI を描画しない。診断は Unity Console + UI 側（`camera/error` / `cameras/list`）に流す。
- **OR-2**: 同一 topic への state は last-write-wins（最新値のみ）。
- **CSW-1〜CSW-16**（camera-switcher-tab 側の決定）すべて整合させる。特に CSW-5（cameraId 採番権限）、CSW-6（OSC アドレス階層化）、CSW-9（フレーム同期 60 Hz）、CSW-12（active-set 連動 Volume）。

---

## Open Questions and Decisions (Dig)

本セクションは本 spec 固有の設計上の決定事項を記録する。上流 spec の決定（D-1〜D-11、OR-1〜OR-2、UI-1〜UI-7、CSW-1〜CSW-16）は暗黙に継承される。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| CSO-1 | OSC 受信ロールと所有権 | **本 spec が `uOSC.uOscServer` を所有**し、メイン出力側 GameObject（`OutputSceneBootstrapper` 配下に独立 GameObject を生成して attach、または `CameraOscReceiverHost` MonoBehaviour として `CamerasRoot` の隣に配置）として常駐させる。`autoStart=false` で生成し、IPC 接続確立 + アダプタ初期化完了後に `StartServer()` を呼ぶ。 | uOSC は `MonoBehaviour` 前提のためライフサイクルを Unity に任せる方が単純。`output-renderer-shell` 側に押し付けず本 spec が完結して所有することで Wave 3e（RDS 連携）と独立に進化できる。 | 低（GameObject 生成は 1 つで済むため） |
| CSO-2 | OSC 受信ポート既定値 | **`127.0.0.1:9000`** を仮置きする（uOSC サンプルおよび OSC 標準的な値）。`docs/integration-plan.md` §7.2 オープンイシュー「OSC アドレス・ポート最終確定」が解決され次第差し替える。利用者側は設定ファイル / `CameraSwitcherOutputAdapterConfig` ScriptableObject から上書き可能。 | UCAPI 規格には固定ポートの指定がない。`core-ipc-foundation` の WebSocket は `61874` を使うため衝突しない範囲で選定。最終確定までは仮置きのまま動作する形で前進する。 | 中（最終確定時にメイン出力側 + UI 側両方の設定ファイル更新が必要、注記必須） |
| CSO-3 | OSC 受信時のメインスレッド契約 | **`uOscServer.onDataReceived` は Unity メインスレッドで発火する**（`uOscServer.Update()` 内で `parser_.Dequeue()` → Invoke する実装、PackageCache の `uOscServer.cs` で確認済み）ため、追加の SynchronizationContext 経由 marshal は不要。本 spec のハンドラはそのまま `Camera` を直接操作してよい（Requirement 5.3, CSW-8）。ただし将来 uOSC の挙動が変わるリスクに備えて、ハンドラ入口で `MainThreadGuard.AssertMainThread()` を呼んでアサートする。 | uOSC 1.0.0 の `uOscServer.cs` が `MonoBehaviour.Update` で `onDataReceived.Invoke()` を呼ぶ実装であることを確認済み。重ねて marshal すると 1 フレーム余計に遅延するため避ける。 | 低（uOSC の API 変更時のみ要再確認） |
| CSO-4 | OSC 受信フローのアロケーション抑制 | **Flat Record blob のデコードに使う作業バッファをプール化**する。`UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` は `byte[]` を受け取るため、`uOSC.Message.values[0]` が `byte[]` の参照そのものを使い回す形に保ち、追加の `byte[]` コピーを行わない。CRC 失敗時は廃棄してカウンタを進めるのみ。 | 60 Hz 連続受信で GC アロケーションが累積するとメイン出力フレームに干渉する。`uOSC.Message` は `struct` で `byte[] values` への参照のみ持つため、コピー不要で適用可能。 | 中（uOSC の内部実装に依存。バージョン更新時に再評価が必要） |
| CSO-5 | OSC 適用フレーム規律 | **OSC 受信は Update / LateUpdate を問わずメインスレッドで来た値を即座に Camera に適用** する（D-3 / CSW-8）。同一 cameraId に対し同一フレーム内で複数のメッセージが届いた場合は **最後に到着した値を採用**（last-write-wins、CSW-9 / 5.4 と整合）。受信キュー（`parser_`）は uOSC 内部で FIFO に保たれる。 | 受信側で coalesce を試みると Update タイミングごとに評価が必要となり実装が複雑化する。最後の値で上書きする限り中間値スキップは描画上不可視。 | 低 |
| CSO-6 | cameraId 採番ルール | **メイン出力側で採番**（CSW-5）。形式は `cam-{連番}` または `cam-{guid8}`。本 spec では衝突回避と可読性のため **`cam-{連番:04d}`**（4 桁ゼロ埋め連番、001 から開始、削除しても番号は再利用しない）を既定とする。利用者側で衝突しない範囲で上書き可能（`ICameraIdAllocator` 抽象として port 化）。`OscAddressBuilder` の許容文字（`[A-Za-z0-9_-]`）に収まる。 | UI 側は採番に関与しない（CSW-5）。連番再利用しないことで OSC 受信レースで旧 ID 向け blob を新カメラに誤適用するリスクを排除（5.6 と整合）。 | 中（長時間運用で番号が大きくなるが 4 桁では十分。再起動で連番リセットするため永続化は本 spec の責務外） |
| CSO-7 | Camera GameObject の生成方針 | **`CamerasRoot` 配下に直接 Camera GameObject を生成**する（`OutputSceneRootNames.Cameras = "CamerasRoot"`）。`output-renderer-shell` の `DefaultCamera` は `IOutputSceneRoots.DefaultCamera` で公開されているが、本 spec はこれを **「カメラ未追加時のフォールバック」** として扱い、`camera/command add` で 1 台目が追加されたら `DefaultCamera` を `enabled = false` に切り替え、本 spec が生成した Camera を `cameras/active` に追従して切り替える。 | `DefaultCamera` を破棄するとシェルの不変条件が壊れる。本 spec で生成する Camera は完全に独立したライフサイクルを持つため、追加・削除・active-set のすべてが `DefaultCamera` に影響しない。 | 低（`DefaultCamera` の `enabled` を切り替えるのみで shell の責務に踏み込まない） |
| CSO-8 | Camera コンポーネントの初期構成 | **新規生成 Camera は `usePhysicalProperties = true`** で初期化し、`focalLength` / `sensorSize` / `lensShift` 等の physical 系プロパティを UCAPI4Unity が直接書ける状態にする（`UcApiRecordParser.ToCamera` の挙動と整合）。デフォルト transform は **メイン出力側で固定値**（position=(0,1.5,-3)、rotation=identity、focalLength=50mm、sensorSize=(36,24) フルフレーム相当）として、`CameraSwitcherOutputAdapterConfig.DefaultCameraTransform` で上書き可能（CSW-10）。Culling Mask / `targetDisplay` は `IDisplayRoutingService` に従属（Wave 3e で RDS 経路に切替可能）。 | UCAPI Flat Record は `focalLength`・`sensorSize` を 1mm 単位の物理値として持つ（`UcApi4UnityCamera` のソース確認済み）ため physical properties がオフだと反映できない。 | 低 |
| CSO-9 | active-set と Camera 切替の構造 | **`cameras/active` state（UI→出力）を受信したら、対応 cameraId の Camera を `enabled = true`、他の本 spec 生成 Camera を `enabled = false` にする**。これによりレンダリング対象が 1 台に絞られる。`targetDisplay` は変更しない（`IDisplayRoutingService` の責務）。`cameras/active`（出力→UI、権威 echo）も併せて publish する。 | URP では複数有効 Camera は `Base / Overlay` 構造でないと衝突する。本 spec の最小機能（CSW-2）では単一カメラ運用に限定するため `enabled` 切替で済む（CSW-12）。PVW/PGM 拡張時は本決定を再評価。 | 中（PVW/PGM 拡張時に Camera Stacking を導入する余地を残す） |
| CSO-10 | Local Volume 連動の実装 | **active-set を受けたメイン出力側で、対応する Local Volume を `enabled = true`、他カメラの Local Volume を `enabled = false` にする**（CSW-12 の直接実装）。Local Volume は **Camera GameObject の子として 1 つの `Volume` コンポーネント + `BoxCollider`（trigger=true、Volume の `isGlobal=false` 用 collider）** または **`Volume.isGlobal = true` だが Volume の `weight` を切替** の二択。本 spec では **`Volume.isGlobal = true` + `enabled` 切替**（コライダー不要、URP の Camera Volume Layer 機構と独立に簡潔に実装）を採用する。Camera ごとの `cullingMask` / Volume Layer は本 spec で固定値（layer 30 を `CameraVolumes` に予約）。 | URP の Local Volume は本来「特定領域のみ効果を出す」ものだが、本フェーズの「カメラごとに固有のルック」要件は **isGlobal=true Volume の enabled 切替** で十分実現できる（コライダー設定が不要、URP の Camera Volume Layer Mask に依存しない）。 | 中（URP の Volume 機構の慣用的な使い方からは外れるが、最小機能としては十分。将来 isGlobal=false + Trigger Collider 方式に差し替え可能な抽象を `ILocalVolumeBinder` として port 化） |
| CSO-11 | Volume Override スキーマ Request の応答 | **`camera/{id}/volume/overrides/metadata` Request を受けたら、URP の `VolumeManager` が認識する全 `VolumeComponent` 派生型を Reflection で列挙し、各 `VolumeParameter<T>` の型・レンジ（`MinAttribute` / `MaxAttribute`）・既定値・表示名（フィールド名）を抽出して `VolumeMetadataResponse`（Contracts asmdef の DTO）として返す**。スキーマは静的なので初回 Request 時にキャッシュし、以降は同 instance を返す。 | UI 側（`camera-switcher-tab`）は本応答を元に動的 UI を生成する（CSW-11）。Reflection コストは初回のみ。Unity の URP `VolumeProfile` API は spec #2 / #5（stage-lighting）でも同じパターンを採るため、ヘルパクラスを `VolumeOverrideSchemaResolver` として独立実装する。 | 中（URP のバージョン更新で `VolumeParameter` の Reflection 構造が変わるリスク。adapter 内に閉じ込めて吸収する） |
| CSO-12 | プリセット関連 topic の扱い | **`camera/preset/command` / `camera/preset/list` / `camera/preset/active` は本 spec 内では永続化しない**（`camera-switcher-tab` 側の責務、CSW-13 / CSW-14）。本 spec は **`camera/preset/command activate` event を受信したら、後続の `camera/command` `delete/add` / `camera/{id}/metadata/*` / `camera/{id}/volume/*` / `camera/command active-set` の通常経路で UI 側から再構築命令が届く前提** であり、プリセット切替自体に対する特殊ハンドラは持たない（CS-10 / SL-8 と同方針）。`camera/preset/*` 関連 topic は **観測ログ目的でのみ購読**し、診断ログに記録する。 | UI 側プリセット切替時は通常 state/event 経路で再構築されるため、メイン出力側で「プリセット」概念を理解する必要がない。本 spec の責務範囲を最小化できる（CSW-13 と整合）。 | 低 |
| CSO-13 | プレビュー RenderTexture の提供 | **`camera/preview/command attach` event を受けたら、対応する cameraId 用に **小サイズ RenderTexture**（既定 192×108、設定で上書き可）を生成し、Camera コンポーネントの `targetTexture` ではなく **本 spec が管理する別の "プレビュー専用 Camera"** にコピーして RenderTexture へ書き出す**。さらに `IPreviewHandleResolver`（`camera-switcher-tab` Contracts 側 port）が解決可能な textureKey を生成し、`camera/{id}/preview/handle` state で UI 側に返す。**注**: メイン放送カメラの `targetTexture` を直接書き換えると `targetDisplay` レンダリングが破壊されるため、別 Camera を本 spec 内に立てる。**ただし本フェーズの最小機能では Camera を独立に生やす実装コストが高いため、初期実装ではプレースホルダ実装（解像度ゼロ / handle 不在）として `camera/error` を返し、Wave 3 後段の拡張で本格実装する**。 | docs/integration-plan.md でプレビュー RenderTexture の責務分担はまだ未確定（§7.2 オープンイシュー含意）。最小機能（CSW-2）として、まず OSC 受信 + Camera 適用 + Local Volume + active-set + cameras/list の本流を完成させる。プレビュー機能は段階的に組み込む。 | 高（プレビューが正式実装されるまで UI 側のマルチプレビュー UX が空になる。`camera-switcher-tab` 側ではプレースホルダ表示で凌ぐ仕様（CSW-16）と整合させる必要あり） |
| CSO-14 | 起動順序とフェイルセーフ | **本 spec の起動順序は `OutputSceneBootstrapper` 完了 → 本 spec の `CameraSwitcherOutputAdapter` 初期化 → IPC ハンドラ登録（`IOutputCommandDispatcher`）→ `uOscServer.StartServer()` → `cameras/list` の初期 publish**。OSC 起動失敗（ポート占有等）は致命扱いとせず、IPC 経路は継続。OSC 受信ハンドラ自体は登録済みなので、ポート空きが回復した次回 PlayMode で動作する（D-9 と整合）。`camera/error` event で UI 側に「OSC 受信不可」を通知。 | OSC 断と IPC 断は独立（CSW-15）。UI 操作経路（カメラ CRUD・Volume 編集）は OSC 受信失敗でも継続できる。 | 中（OSC 起動失敗時に再試行ロジックを入れるか、PlayMode 開始のみで起動するかの選択。本 spec では後者で簡潔に） |
| CSO-15 | UCAPI cameraNo と本 spec cameraId のマッピング | **UCAPI Flat Record の `CameraNo` フィールド（`UcApiRecord.CameraNo`、byte 値、1 から始まる）は本 spec の `cameraId`（string、`cam-{連番:04d}`）とは別管理**。OSC アドレス側 `/ucapi/camera/{cameraId}/flat` の `cameraId` 文字列で多重化を区別する（CSW-6）ため、`CameraNo` は使わず Flat Record の `CameraNo` 値はデコード後にチェックしないか、診断ログに記録のみ。 | UCAPI 仕様には `CameraNo` があるが、本フェーズではアドレス階層化（CSW-6）で多重化するため `CameraNo` を再利用するとアドレスと内容が二重指定になり混乱する。本 spec ではアドレス側を真とする。 | 低（UCAPI 側仕様変更時のみ要再確認） |
| CSO-16 | スレッド契約と LateUpdate 順序 | **本 spec の Camera 適用は uOSC の `Update` で発火するため、OSC で受け取った transform は `LateUpdate` までに完全に確定**する。`output-renderer-shell` の他の更新ロジックや `camera-switcher-tab` のプレビュー描画とのフレーム順序衝突は **PlayerLoop の Update 段階で決着** する。本 spec は LateUpdate / FixedUpdate にロジックを置かず、すべて OSC 受信ドリブン + IPC 受信ドリブンで動作する。 | OSC 受信は uOSC の `Update` で来るため、それ以降のフレーム描画段階（LateUpdate → Render）には最新値が反映される。明示的な PlayerLoop 挿入は不要。 | 低 |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase における **camera-switcher-output-adapter（カメラ・Local Volume メイン出力側アダプタ）** を定義する。`camera-switcher-tab`（spec #6）の UI から発信される **OSC（UCAPI Flat Record）** と **IPC（WebSocket/JSON, core-ipc-foundation）** を、メイン出力シーン（`output-renderer-shell` の `CamerasRoot` / `VolumeRoot`）に存在する実体（`UnityEngine.Camera` および URP `Volume`）へ反映する責務を持つ。本 spec はメイン出力側のシーン操作・OSC 受信・IPC ハンドラ登録・cameraId 採番・Local Volume の連動切替・Volume Override スキーマの Reflection 抽出までを範囲とする。

具体的には：

1. **OSC 受信ライン**: `uOSC.uOscServer` を本 spec が所有する独立 GameObject に attach し、`/ucapi/camera/{cameraId}/flat` のメッセージ（60 Hz 目安、UCAPI Flat Record blob）をメインスレッドで受信し、`UCAPI4Unity.UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` で対応 cameraId の `Camera` に適用する（Requirement 1, 2）。
2. **IPC ハンドラ登録（`IOutputCommandDispatcher` 経由）**:
   - `camera/command`（event）を受け、`add` で cameraId を採番して `Camera` GameObject を生成、`delete` で破棄、`active-set` で `enabled` を切替（Requirement 3, 4）。
   - `camera/{id}/metadata/{key}`（state）を受け、`displayName` / `type` / `defaultTransform` 等のメタデータを Camera プロパティに反映（Requirement 5）。
   - `camera/{id}/volume/override/{type}/{param}`（state）と `enabled`（state）を受け、対応 Camera の Local Volume プロファイルに `VolumeParameter<T>` 値を Reflection で適用（Requirement 6）。
   - `camera/{id}/volume/command`（event, override-add/remove）で Volume Override の追加・削除を行う（Requirement 6）。
   - `camera/{id}/volume/overrides/metadata`（request）に対し、URP `VolumeComponent` 派生型を Reflection で列挙したスキーマを返答する（Requirement 7）。
   - `camera/preset/*`（event/state）はパススルー観測のみ行う（プリセット永続化は UI 側責務、CSO-12）。
3. **IPC 送信ライン**: `cameras/list`（state）/ `cameras/active`（state, 権威 echo）/ `camera/created`（event）/ `camera/error`（event）/ `camera/{id}/preview/handle`（state）を発行する（Requirement 8）。
4. **Camera ライフサイクル管理**: `output-renderer-shell.IOutputSceneRoots.Cameras`（`CamerasRoot`）配下に Camera GameObject を生成・破棄し、`DefaultCamera` を 1 台目追加時にフォールバックから外す（Requirement 4）。
5. **active-set と Local Volume の連動**: UI 側の `camera/command active-set` を受けたメイン出力側で、対応 cameraId の Camera を `enabled = true`、他の本 spec 管理 Camera を `enabled = false` にし、対応 Local Volume を同様に切替える（Requirement 9, CSW-12）。
6. **メインスレッド契約厳守**: OSC 受信コールバック / IPC 受信コールバックともに Unity メインスレッドで `UnityEngine.Camera` / `Volume` を操作する。`MainThreadGuard` でアサート（Requirement 10）。
7. **PlayMode 限定の常駐**: `OutputSceneBootstrapper` 完了後に起動、PlayMode 終了 / Application Quit / Domain Reload で `uOscServer.StopServer()` + Camera 破棄 + IPC ハンドラ Dispose を確実に行う（Requirement 11）。
8. **配信適合性**: 本 spec は **メイン出力サーフェス（Display 2+）に UI / 警告 / デバッグオーバーレイを一切描画しない**。診断は Unity Console + IPC `camera/error` event で UI 側へ流す（Requirement 12, OR-1 / 5.6）。
9. **本 spec 単独での検証可能性**: UI 側不在でも、自前のテストハーネスから OSC blob 注入と IPC envelope 注入で全機能を検証できる構造を提供する（Requirement 13）。

本 spec は **メイン出力側のアダプタ実装** に責務を限定する。UI 側のタブ機能（プレビュー UI、SceneViewStyleCameraController、UCAPI シリアライズ、OSC 送信、Local Volume 編集 UI、プリセット CRUD、永続化）は `camera-switcher-tab` の責務である。OSC トランスポート実装そのもの（`uOSC` パッケージ）、UCAPI Flat Record デコード実装（`UCAPI4Unity` パッケージ）、IPC トランスポート（`core-ipc-foundation`）、メイン出力シーン骨格（`output-renderer-shell`）、ディスプレイ振り分け（`IDisplayRoutingService` の RDS 連携、Wave 3e）は範囲外である。

## Boundary Context

- **In scope**:
  - **OSC 受信パイプライン**: `uOSC.uOscServer` の所有、`/ucapi/camera/{cameraId}/flat` 受信、UCAPI Flat Record デコード、対応 cameraId の `Camera` への適用（CSO-1, CSO-3, CSO-4, CSO-5, CSO-15）
  - **IPC ハンドラ登録**: `IOutputCommandDispatcher.RegisterStateHandler` / `RegisterEventHandler` / `RegisterRequestHandler` を介した `camera/command` / `camera/{id}/metadata/{key}` / `camera/{id}/volume/*` / `camera/{id}/volume/overrides/metadata` / `camera/preview/command` / `camera/preset/*` の購読
  - **cameraId 採番**: 連番 `cam-{NNNN}` 形式（CSO-6）、`ICameraIdAllocator` 抽象として port 化
  - **Camera GameObject ライフサイクル**: `CamerasRoot` 配下への生成・破棄、`DefaultCamera` のフォールバック扱い、`usePhysicalProperties = true` 初期化（CSO-7, CSO-8）
  - **active-set 切替**: `Camera.enabled` の切替（CSO-9）と `cameras/active` 権威 echo
  - **Local Volume 連動**: `Volume.enabled` 切替（CSO-10）、`VolumeProfile.AddComponent<T>()` / `Remove<T>()` / Override パラメータの Reflection 適用
  - **Volume Override スキーマ Reflection**: `VolumeMetadataRequest` 応答（CSO-11）
  - **IPC 送信**: `cameras/list` / `cameras/active`（権威 echo）/ `camera/created` / `camera/error` / `camera/{id}/preview/handle`
  - **メインスレッド契約**: `MainThreadGuard.AssertMainThread()` を OSC / IPC ハンドラ入口で実行
  - **ライフサイクル**: PlayMode 開始/停止に同期した起動・停止、ハンドル/購読の確実な解放（D-9）
  - **OSC 起動フェイルセーフ**: ポート占有等で起動失敗してもメイン出力描画を継続、UI 側に診断 event を送出（CSO-14, CSW-15）
  - **配信適合性**: メイン出力サーフェスに UI / 警告 / デバッグログを描画しない（OR-1）
  - **スタンドアロンビルドと Editor PlayMode の両対応**
  - **本 spec 単独検証**: UI 側 / OSC 送信側不在でテスト可能なハーネス
- **Out of scope**:
  - **UI 側タブ機能**: `camera-switcher-tab` の責務（プレビュー UI / SceneViewStyleCameraController / UCAPI シリアライズ / OSC 送信 / Local Volume 編集 UI / プリセット CRUD と永続化 / `camera/preset/*` 永続化）
  - **uOSC パッケージ本体・UCAPI4Unity パッケージ本体・UCAPI C++ DLL の実装** （採用パッケージをそのまま使用）
  - **`core-ipc-foundation` のトランスポート / シリアライゼーション / メインスレッド配信実装**（spec #1 の責務）
  - **`output-renderer-shell` のシーン骨格 / `OutputSceneBootstrapper` / `IOutputSceneRoots` 実装本体 / `DefaultCamera` 生成本体**（spec #2 の責務）
  - **`IDisplayRoutingService` の実装**（`output-renderer-shell` 側で `BuiltInDisplayRoutingService` または Wave 3e の RDS 経路）
  - **他タブ系 spec とその出力アダプタ** （`character-selection-tab` / `rac-main-output-adapter` / `stage-lighting-volume-tab` / `stage-lighting-volume-output-adapter`）
  - **トランジション・ディゾルブ・PVW/PGM・外部ハードウェアスイッチャー連携・タイムライン録画リプレイ**（docs/requirements.md §5.3.4、本フェーズの非目標）
  - **OSC のセキュリティ / 認証 / 暗号化**（localhost 限定運用、上流方針継承）
  - **タブ共通 UI 状態の永続化**（UI-7、本 spec はメイン出力側のため UI 側の責務でもない）
  - **プレビュー RenderTexture の本格実装**（CSO-13、本フェーズではプレースホルダ）
- **Adjacent expectations**:
  - `core-ipc-foundation` が `IOutputCommandDispatcher`（実装は spec #2）経由で IPC 受信コールバックを Unity メインスレッドに配信すること（D-3）
  - `output-renderer-shell` が `IOutputSceneRoots.Cameras` / `Volumes` を提供し、`DefaultCamera` が初期生成されていること（OR Requirement 1.2）
  - `output-renderer-shell` が `IOutputCommandDispatcher` インスタンスを Service Locator または DI 経由で本 spec に提供すること
  - `camera-switcher-tab` の `Runtime/Contracts/` asmdef（`VTuberSystemBase.CameraSwitcherTab.Contracts`）が `CameraIpcTopics` / `CameraId` / `CameraType` / `OscAddressBuilder` / `Payloads/*` を公開していること（Wave 3a 完了済み、本 spec はそのまま参照）
  - `com.hidano.ucapi4unity` が `UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` を提供し、Unity 6.3 で UCAPI C++ DLL がロード可能であること
  - `com.hidano.uosc` v1.0.0+ が `uOscServer` MonoBehaviour と `Message`（`address`, `byte[] values[]`）を提供し、`onDataReceived` をメインスレッド配信すること（PackageCache の `uOscServer.cs` で確認済み）
  - 利用者プロジェクトが `OutputSceneBootstrapper` のシーン上で本 spec の `CameraSwitcherOutputAdapter` MonoBehaviour（または同等の Composition Root）を配置していること
  - スタンドアロンと Editor PlayMode の両方で本 spec が同一挙動を取ること（D-9）

---

### Requirement 1: OSC 受信パイプラインの所有とライフサイクル

**Objective:** 配信運用者として、UI 側から `127.0.0.1:9000`（既定値、設定で上書き可）に送信される `/ucapi/camera/{cameraId}/flat` メッセージを、メイン出力アプリの起動中に確実に受信できる状態を得たい。そうすれば UI 側のカメラ操作がメイン出力カメラに低遅延で反映される。

**Note:** OSC 受信側ロールはメイン出力側（CSW-7、本 spec）。`uOSC.uOscServer` の所有は本 spec が持ち、`autoStart=false` で生成して IPC ハンドラ登録完了後に `StartServer()` を呼ぶ（CSO-1, CSO-14）。OSC 起動失敗（ポート占有）はメイン出力描画を阻害しない（CSO-14, CSW-15）。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall **`uOSC.uOscServer` を所有する独立 GameObject**（既定名 `CameraOscReceiverHost`）を本 spec の Composition Root が生成し、`autoStart=false` で attach する（see CSO-1）。
2. The Camera Switcher Output Adapter shall OSC 受信ポート・ホストを設定ファイル / `ScriptableObject`（`CameraSwitcherOutputAdapterConfig`）から読み込み、未指定時は `127.0.0.1:9000` を既定値とする（see CSO-2、`docs/integration-plan.md` §7.2 オープンイシュー、最終確定までは仮置き）。
3. When 本 spec が初期化されたとき、the Camera Switcher Output Adapter shall **IPC ハンドラ登録完了後** に `uOscServer.StartServer()` を呼び、`onDataReceived` を本 spec の OSC 受信ハンドラに購読する（see CSO-14）。
4. When `uOscServer.StartServer()` がポート占有等で失敗したとき、the Camera Switcher Output Adapter shall 失敗事由を Unity Console と IPC `camera/error` event でログし、IPC ハンドラ系（カメラ CRUD / Volume / metadata 応答）の動作は継続する（see CSO-14, CSW-15, Requirement 12）。
5. When PlayMode 終了 / Application Quit / Domain Reload が発生したとき、the Camera Switcher Output Adapter shall `uOscServer.StopServer()` を呼び、UDP ソケット・ワーカースレッドを完全に解放する（see D-9, Requirement 11）。
6. While PlayMode の開始と停止が繰り返される間, the Camera Switcher Output Adapter shall OSC ポート占有残存・スレッドリーク・GameObject 残存を発生させずに毎回クリーンに再初期化する（Req 13.4 と整合）。
7. The Camera Switcher Output Adapter shall **Edit モードでは OSC サーバを起動しない**（D-9 継承）。
8. The Camera Switcher Output Adapter shall OSC 受信のスレッドモデルを `uOSC.uOscServer.Update()` 経由のメインスレッド配信に従属させ、追加の SynchronizationContext 経由 marshal は実装しない（see CSO-3、`uOscServer.cs` の `parser_.Dequeue()` → `onDataReceived.Invoke()` 構造に依存）。
9. When OSC アドレスが本 spec の期待プレフィクス（既定 `/ucapi/camera`）と一致しないメッセージを受信したとき、the Camera Switcher Output Adapter shall 当該メッセージを破棄して診断カウンタを進めるのみとし、例外送出・描画停止を発生させない。

---

### Requirement 2: UCAPI Flat Record のデコードと Camera への適用

**Objective:** 配信運用者として、OSC で届いた UCAPI Flat Record が、対応する cameraId の Unity Camera に position / rotation / focalLength / sensorSize / clip 等を含めて即座に反映され、配信映像が UI 側のカメラ操作に追従する状態を得たい。

**Note:** UCAPI4Unity の `UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` を直接利用する（PackageCache 確認済み）。Flat Record の CRC16-CCITT 検証は UCAPI4Unity / C++ DLL 側で行われ、デコード失敗時は例外を返す。本 spec は例外をキャッチして当該フレームを破棄する。

#### Acceptance Criteria

1. When OSC アドレス `/ucapi/camera/{cameraId}/flat` のメッセージを受信したとき、the Camera Switcher Output Adapter shall アドレスから `cameraId` 部分を抽出し、本 spec が管理する Camera Registry から該当 cameraId の `UnityEngine.Camera` 参照を解決する。
2. When `cameraId` がメイン出力側で未知（`camera/command add` 完了前 / 既に `delete` 済み）であった場合, the Camera Switcher Output Adapter shall 当該メッセージを破棄して診断カウンタを進めるのみとし、UI 側に通知せずメイン出力描画を継続する（see Requirement 5.6, CSW-9）。
3. When `cameraId` の Camera が解決できたとき、the Camera Switcher Output Adapter shall `Message.values[0]` を `byte[]` として取り出し、`UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` に渡す（追加コピーを行わない、see CSO-4）。
4. If `UcApi4UnityCamera.ApplyToCamera` が例外（CRC 失敗 / DLL 不在 / 解析失敗）を投げた場合, the Camera Switcher Output Adapter shall 例外を捕捉して診断ログに記録し、当該メッセージを破棄、以降の受信を継続する（see Requirement 5.5）。
5. The Camera Switcher Output Adapter shall 同一 cameraId に対し同一フレーム内で複数のメッセージが届いた場合、**最後に到着した値を採用**（last-write-wins、see CSO-5、CSW-9 / 5.4 と整合）する。中間値スキップは描画上不可視。
6. The Camera Switcher Output Adapter shall UCAPI Flat Record の `CameraNo` フィールドはアドレス側 `cameraId` の真値と独立に扱い、`CameraNo` での再分岐は行わない（see CSO-15）。診断ログには記録のみ。
7. The Camera Switcher Output Adapter shall OSC 受信から Camera 適用までの 1 メッセージあたりの処理を **メインスレッドで完結** させ、`MainThreadGuard.AssertMainThread()` をハンドラ入口で評価する（see CSO-3, Requirement 10）。
8. The Camera Switcher Output Adapter shall OSC 受信処理が **メイン出力（Display 2+）の描画フレームを中断・遅延させない** ことを構造的に保証する（GC アロケーション抑制、see CSO-4, docs/requirements.md §6.1）。
9. The Camera Switcher Output Adapter shall 1 秒以上にわたって連続で受信する 60 Hz × 1 cameraId × 1000 件以上のメッセージに対し、損失なし（uOSC 内部の FIFO は保たれる前提）に Camera を更新できる（see Requirement 13.1）。

---

### Requirement 3: `camera/command` event の処理（add / delete / active-set）

**Objective:** 配信運用者として、UI 側のカメラ追加・削除・アクティブ切替操作がメイン出力シーンに反映され、`CamerasRoot` 配下の Camera GameObject 構成と enable 状態が UI 側意図と一致する状態を得たい。

**Note:** `camera/command` は event（FIFO）で UI→出力（CSW-4）。本 spec は `IOutputCommandDispatcher.RegisterEventHandler<CameraCommandPayload>(CameraIpcTopics.CameraCommand, ...)` でハンドラを登録する。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall `IOutputCommandDispatcher.RegisterEventHandler<CameraCommandPayload>(CameraIpcTopics.CameraCommand, OnCameraCommand)` を初期化時に登録する。
2. When `CameraCommandPayload.Op == "add"` を受信したとき、the Camera Switcher Output Adapter shall **メイン出力側で cameraId を採番**（CSO-6、`ICameraIdAllocator` 経由 `cam-{NNNN}` 形式）し、`CamerasRoot` 配下に新規 GameObject を生成、`Camera` コンポーネントを attach、`usePhysicalProperties = true` で初期化、デフォルト transform を適用する（see CSO-7, CSO-8, CSW-10）。
3. When `add` 処理が完了したとき、the Camera Switcher Output Adapter shall `IpcCommand.PublishEvent(CameraIpcTopics.CameraCreated, CameraCreatedEventPayload { ClientRequestId, CameraId, Metadata })` で UI 側に採番済み cameraId と初期メタデータを返す（see CSW-5）。
4. When `add` 処理がリソース不足 / 不正 type 等で失敗した場合, the Camera Switcher Output Adapter shall `IpcCommand.PublishEvent(CameraIpcTopics.CameraError, CameraErrorEventPayload { ClientRequestId, Op="add", Reason, Detail })` で UI 側に通知し、該当 cameraId は採番しない（see Requirement 12.3）。
5. When `CameraCommandPayload.Op == "delete"` と対象 `CameraId` を受信したとき、the Camera Switcher Output Adapter shall 該当 Camera GameObject を破棄し、Camera Registry から削除、`cameras/list` state を再 publish する。`cameras/active` が当該 cameraId を指していた場合は `active = null` に切り替え state を発行する。
6. When `CameraCommandPayload.Op == "active-set"` と対象 `CameraId` を受信したとき、the Camera Switcher Output Adapter shall **対応 cameraId の `Camera.enabled = true`、他の本 spec 管理 Camera を `Camera.enabled = false`** にする（see CSO-9, CSW-12）。`DefaultCamera`（output-renderer-shell 提供）は `enabled = false` にする（1 台目以降は本 spec の Camera が描画する）。
7. When `active-set` の対象 cameraId が未知だった場合, the Camera Switcher Output Adapter shall `camera/error` event で通知し、現在のアクティブ cameraId を変更しない。
8. When `active-set` 処理が成功したとき、the Camera Switcher Output Adapter shall `IpcCommand.PublishState(CameraIpcTopics.CamerasActive, new CamerasActiveStatePayload { ActiveCameraId, UpdatedAtUnixMs })` を権威 echo として発行する。
9. The Camera Switcher Output Adapter shall `add` 後 1 台目の Camera が生成された時点で **`DefaultCamera` を `enabled = false`** に切り替え、`delete` で全カメラが消滅した場合は `DefaultCamera` を `enabled = true` に復帰させる（see CSO-7、フォールバック挙動）。
10. The Camera Switcher Output Adapter shall `add` / `delete` / `active-set` イベントを FIFO 順で処理し、複数の event 受信中も Camera Registry の整合性を維持する（D-7 継承）。
11. The Camera Switcher Output Adapter shall `add` イベントの ClientRequestId を保持して `camera/created` の echo に同 ClientRequestId を載せる。

---

### Requirement 4: Camera Registry と `cameras/list` state 発行

**Objective:** UI 側として、メイン出力側に存在するカメラ一覧を `cameras/list` state で取得し、UI のカメラリストを最新に保ちたい。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall **Camera Registry**（cameraId → CameraEntry）を内部に持ち、`add` / `delete` のたびに更新する。
2. The Camera Switcher Output Adapter shall Camera Registry の変更ごとに `IpcCommand.PublishState(CameraIpcTopics.CamerasList, new CamerasListPayload { Cameras, UpdatedAtUnixMs })` を発行する。
3. The Camera Switcher Output Adapter shall `CamerasListPayload.Cameras` を Camera Registry の **採番順**（cameraId 連番、CSO-6）で固定し、UI 側の表示順を安定化する（CSW Requirement 6.9）。
4. The Camera Switcher Output Adapter shall 各 `CameraListEntry` に `CameraId` / `DisplayName` / `Type`（`CameraTypeNames.Perspective` / `Orthographic`）/ `DefaultTransform`（position / rotation / focalLength）を含める。
5. When 本 spec が初期化された直後、the Camera Switcher Output Adapter shall `cameras/list` を一度発行する（カメラ 0 台時は空配列、UI 側の起動時 sync 用）。
6. When `cameras/active` の権威値が変化したとき、the Camera Switcher Output Adapter shall `CamerasActive` state を発行する（see Requirement 3.8）。

---

### Requirement 5: `camera/{id}/metadata/{key}` state の処理

**Objective:** UI 側として、カメラの表示名・タイプ・初期 transform 等のメタデータを変更したらメイン出力側のカメラに反映され、`cameras/list` にも追従して再 publish される状態を得たい。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall `IOutputCommandDispatcher.RegisterStateHandler` で `CameraIpcTopics.CameraMetadataPrefix(cameraId)`（または topic prefix サブスクライブ機能、shell 側の対応に依存）でメタデータ系 state を購読する。トピックの動的な `{cameraId}` / `{key}` 部分の解決は本 spec の責務とする。
2. When `camera/{cameraId}/metadata/displayName` を受信したとき、the Camera Switcher Output Adapter shall 該当 GameObject の `name` を `Camera-{cameraId}-{displayName}` に更新し、Camera Registry のエントリを更新、`cameras/list` を再 publish する。
3. When `camera/{cameraId}/metadata/type` を受信したとき、the Camera Switcher Output Adapter shall `CameraTypeNames.Parse(value)` で enum 解析し、`CameraType.Perspective` → `Camera.orthographic = false`、`CameraType.Orthographic` → `Camera.orthographic = true` を適用する。`Unknown` は破棄してログ。
4. When `camera/{cameraId}/metadata/defaultTransform` を受信したとき、the Camera Switcher Output Adapter shall `CameraDefaultTransform` を Camera の `transform.position` / `transform.rotation` / `Camera.focalLength` に適用する（OSC ストリームが上書きする可能性があることを前提とした「初期値」扱い）。
5. The Camera Switcher Output Adapter shall metadata state を冪等（last-write-wins）に処理する（OR-2 継承）。
6. When 未知 cameraId の metadata が届いた場合, the Camera Switcher Output Adapter shall 当該メッセージを破棄して診断ログに記録するのみとする（Req 5.6 と整合）。

---

### Requirement 6: Local Volume の Override 適用と enabled 切替

**Objective:** 配信オペレーターとして、UI で各カメラの Local Volume Override（Bloom / Tonemapping / DoF 等）を編集したら、メイン出力カメラの URP Volume プロファイルに即座に反映され、active-set されたカメラの Volume のみが効果を持つ状態を得たい。

**Note:** Volume は **`isGlobal = true` の `Volume` コンポーネントを各 Camera GameObject の子として 1 つ持ち、`enabled` の切替で active-set 連動する**（CSO-10）。Override の追加削除は `VolumeProfile.AddComponent<T>()` / `Remove<T>()`、param 適用は Reflection で `VolumeParameter<T>.SetValue` 相当。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall `add` で Camera を生成する際、子 GameObject `LocalVolume-{cameraId}` を作成し、`Volume` コンポーネント（`isGlobal = true`、`weight = 1`、`priority = cameraId 連番` で衝突回避）を attach、空の `VolumeProfile` を割り当てる。`Volume.enabled` は初期 `false`（active-set されるまで効果なし）。
2. When `camera/{id}/volume/command` event の `Op == "override-add"` を受信したとき、the Camera Switcher Output Adapter shall `OverrideType` 名（例: `"Bloom"`）を `VolumeManager.instance.baseComponentTypeArray` から解決し、対応する `VolumeProfile.AddComponent<T>()` を呼ぶ。失敗時は `camera/error` event を発行。
3. When `camera/{id}/volume/command` event の `Op == "override-remove"` を受信したとき、the Camera Switcher Output Adapter shall `VolumeProfile.Remove<T>()` を呼ぶ。
4. When `camera/{id}/volume/override/{type}/enabled` state を受信したとき、the Camera Switcher Output Adapter shall 該当 Override コンポーネントの `active` プロパティに値を反映する。
5. When `camera/{id}/volume/override/{type}/{param}` state を受信したとき、the Camera Switcher Output Adapter shall **Reflection で対象 `VolumeParameter<T>`.value に値を設定**する（`VolumeParamSchema.TypeTag` に応じた変換: `float` → `float`、`int` → `int`、`bool` → `bool`、`color` → `Color`、`enum` → `int` cast）。`overrideState = true`（URP の Override マーカー）を併せて設定する。
6. When `camera/{id}/volume/enabled` state を受信したとき、the Camera Switcher Output Adapter shall 該当 Local Volume の `Volume.enabled` を当該 bool 値に上書きする（手動制御モード相当、CSW-9.4 の暫定既定）。
7. When `cameras/active` の権威値が cameraId X に切り替わったとき、the Camera Switcher Output Adapter shall **cameraId X の Local Volume.enabled = true、他の Local Volume.enabled = false** に切り替える（CSO-10、CSW-12 の自動連動）。`camera/{id}/volume/enabled` state も併せて全カメラ分 publish する（UI 側に状態を見せるため）。
8. When `delete` が発生したとき、the Camera Switcher Output Adapter shall 該当 Camera と一緒に Local Volume GameObject も破棄する。
9. When 未知 `OverrideType` または `param` が届いた場合, the Camera Switcher Output Adapter shall 当該メッセージを破棄して診断ログに記録する。
10. When Reflection による `VolumeParameter<T>` の値設定で例外が発生した場合, the Camera Switcher Output Adapter shall 例外を捕捉して `camera/error` event で通知し、他カメラ・他 Override の処理を継続する。
11. The Camera Switcher Output Adapter shall Override パラメータの値域外（例: `Min` / `Max` 違反）を URP 側のクランプに委ね、本 spec ではクランプを試みない（送信側 UI の責務、see CSW Requirement 8.8）。

---

### Requirement 7: Local Volume Override メタデータ Request の応答

**Objective:** UI 側として、`camera/{id}/volume/overrides/metadata` Request を送ると、URP が認識する `VolumeComponent` 派生型（Bloom / Tonemapping / ColorAdjustments 等）と各 `VolumeParameter` の型・レンジ・既定値・表示名を構造化された Response として受け取り、UI を動的に生成できる状態を得たい。

**Note:** URP の `VolumeManager.instance.baseComponentTypeArray` から全 `VolumeComponent` 派生型を取得し、各型の public フィールド（`VolumeParameter<T>` 派生）を Reflection で抽出してスキーマを構築する（CSO-11）。スキーマは初回 Request 時にキャッシュし、以降は同 instance を返す。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall `IOutputCommandDispatcher.RegisterRequestHandler<VolumeMetadataRequest, VolumeMetadataResponse>(CameraIpcTopics.VolumeOverridesMetadata(cameraId), ...)` を、各 cameraId 単位で登録する（または topic prefix サブスクライブが shell 側で許される場合はワイルドカード登録）。
2. When `VolumeMetadataRequest` を受信したとき、the Camera Switcher Output Adapter shall `VolumeOverrideSchemaResolver.GetSchema()` を呼び、URP の `VolumeManager.instance.baseComponentTypeArray` から `VolumeComponent` 派生型を列挙する。
3. The Camera Switcher Output Adapter shall 各 `VolumeComponent` 型について、`Type.Name` を `VolumeOverrideSchema.Type` に、displayName を Reflection で `VolumeComponentMenuAttribute` から、または `Type.Name` をフォールバックとして抽出する。
4. The Camera Switcher Output Adapter shall 各型の `public` フィールドのうち `VolumeParameter` 派生型を列挙し、`VolumeParamSchema { Name, TypeTag, Min, Max, Default, DisplayName, Unit, EnumValues }` を構築する：
   - `TypeTag`: `FloatParameter` / `MinFloatParameter` / `ClampedFloatParameter` → `"float"`、`IntParameter` 系 → `"int"`、`BoolParameter` → `"bool"`、`ColorParameter` → `"color"`、`{T}EnumParameter`（Enum 派生）→ `"enum"`。
   - `Min` / `Max`: `MinAttribute` / `MaxAttribute` / `ClampedFloatParameter.min/max` から抽出。
   - `Default`: フィールドの既定値を `JsonElement` に変換。
   - `EnumValues`: enum 型なら `Enum.GetNames()` の文字列配列。
5. The Camera Switcher Output Adapter shall スキーマを **初回 Request 時にキャッシュ**し、以降は同一 `VolumeMetadataResponse` を返す（URP のロード後に型集合は変わらない前提）。
6. When Reflection の途中で未知の `VolumeParameter` 派生型に遭遇した場合, the Camera Switcher Output Adapter shall 当該パラメータをスキップして診断ログに記録し、他のスキーマ生成は継続する。
7. The Camera Switcher Output Adapter shall Request タイムアウト（D-8、5 秒既定）以内に Response を返す。
8. The Camera Switcher Output Adapter shall Response 構築中に例外が発生した場合、空の `VolumeMetadataResponse { Overrides = [] }` を返し、診断ログに記録する（UI 側は空スキーマを縮退表示する、CSW Requirement 8.11 と整合）。

---

### Requirement 8: IPC 送信ライン（cameras/list, cameras/active, camera/created, camera/error, preview/handle）

**Objective:** UI 側として、メイン出力側で発生したカメラ状態変化（追加完了、削除、active 切替、エラー）を IPC で受信し、UI を最新状態に保ちたい。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall `cameras/list` state を Camera Registry の変更ごと（add 完了 / delete / metadata 変更）に再 publish する（see Requirement 4.2）。
2. The Camera Switcher Output Adapter shall `cameras/active` state を active-set 完了ごとに権威 echo として publish する（see Requirement 3.8）。
3. The Camera Switcher Output Adapter shall `camera/created` event を `add` 完了ごとに publish する（see Requirement 3.3）。
4. The Camera Switcher Output Adapter shall `camera/error` event を、操作失敗（`add` 採番失敗、`delete` の未知 cameraId、`active-set` の未知 cameraId、Volume Override 適用失敗、OSC 起動失敗、UCAPI デコード失敗集計）時に publish する。`Reason` は分類可能な短い識別子（例: `ResourceExhausted` / `InvalidType` / `UnknownCameraId` / `OscStartupFailed` / `UcapiDecodeFailed`）。
5. The Camera Switcher Output Adapter shall すべての state 系発行が冪等（同一値の連続発行で UI 側が破綻しない）であることを構造的に保証する（OR-2 継承）。
6. The Camera Switcher Output Adapter shall すべての send が `IUiCommandClient` を直接呼ぶのではなく、shell 側 `core-ipc-foundation` の出力サーバ API（具体名は実装時に確認）を経由する（D-4 / D-5 継承）。
7. The Camera Switcher Output Adapter shall `camera/{id}/preview/handle` state を、`camera/preview/command attach` を受けた cameraId 分について publish する（see Requirement 9, CSO-13）。本フェーズではプレースホルダ実装で `textureKey` を空文字で返してよい。

---

### Requirement 9: プレビュー attach/detach の処理（プレースホルダ実装）

**Objective:** UI 側として、`camera/preview/command attach` を送ると、メイン出力側がカメラごとの RenderTexture ハンドルを `camera/{id}/preview/handle` で返してくれて、UI 側のプレビューパネルに RenderTexture を貼り付けられる状態を得たい。

**Note:** 本フェーズはプレースホルダ実装で十分（CSO-13）。本 spec の最小機能（CSW-2）として OSC 受信 / Camera 適用 / Local Volume / active-set / cameras/list を完成させ、プレビューは将来拡張の枠を残す。`camera-switcher-tab` 側もプレースホルダ表示を許容（CSW Requirement 2.7 / 12.7）。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall `IOutputCommandDispatcher.RegisterEventHandler<PreviewCommandPayload>(CameraIpcTopics.PreviewCommand, ...)` を初期化時に登録する。
2. When `PreviewCommandPayload.Op == "attach"` を受信したとき、the Camera Switcher Output Adapter shall **本フェーズではプレースホルダ応答**として、各 cameraId について `camera/{cameraId}/preview/handle` state を `PreviewHandleStatePayload { TextureKey = "", Size = [0, 0], Fps = 0 }` で publish する（実装拡張時に正規の RenderTexture ハンドルを返す）。
3. When `PreviewCommandPayload.Op == "detach"` を受信したとき、the Camera Switcher Output Adapter shall 当該 cameraId の `preview/handle` state をクリア（空 payload を publish）する。
4. The Camera Switcher Output Adapter shall プレビュー機能のプレースホルダ動作中、UI 側からのカメラ CRUD・Volume 編集・active-set 等の主要機能を一切阻害しない。
5. Where 本フェーズ以降にプレビュー本格実装を追加する場合, the Camera Switcher Output Adapter shall 各 cameraId 用の補助 Camera を `CamerasRoot` 配下に立て、RenderTexture へ書き出して `IPreviewHandleResolver` 解決可能な textureKey で publish する構造に拡張可能であること（契約レベルで後方互換維持）。

---

### Requirement 10: メインスレッド契約と並行性

**Objective:** 開発者として、本 spec のすべての `UnityEngine.Camera` / `UnityEngine.Rendering.Volume` 操作が Unity メインスレッドで実行され、ワーカースレッドからの誤呼び出しによるアサート違反やレースが発生しない状態を得たい。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall OSC 受信ハンドラ入口で `MainThreadGuard.AssertMainThread()` を呼び、`uOSC.uOscServer` のメインスレッド配信契約（CSO-3）に依存する。アサート違反時は例外を Unity Console に出して当該フレームを破棄。
2. The Camera Switcher Output Adapter shall IPC 受信ハンドラ入口で `MainThreadGuard.AssertMainThread()` を呼び、`core-ipc-foundation` の D-3 メインスレッド配信契約に依存する。
3. The Camera Switcher Output Adapter shall 内部状態（Camera Registry / Volume State）を Unity メインスレッド専有とし、ロックフリー / 単一スレッド前提で実装する。
4. The Camera Switcher Output Adapter shall 非同期処理（`Task` / `async`）を使う場合、戻り先を Unity メインスレッドの `SynchronizationContext` に明示的に戻す。
5. The Camera Switcher Output Adapter shall シリアライズ / デシリアライズ / Reflection による Volume スキーマ抽出も含めてメインスレッドで実行し、ワーカーへ逃がさない（軽量処理 + 初回キャッシュで十分という前提、CSO-11）。

---

### Requirement 11: PlayMode ライフサイクルとリソース解放

**Objective:** 開発者・運用者として、PlayMode 開始 → 停止 → 再開を繰り返しても、本 spec の OSC ポート / GameObject / IPC ハンドラ登録 / Camera Registry / Volume プロファイルがクリーンに解放され、リークが発生しない状態を得たい。

**Note:** D-9 継承により、Editor では PlayMode 区間のみ常駐。Domain Reload に跨る状態維持は試みない。

#### Acceptance Criteria

1. When PlayMode が開始したとき、the Camera Switcher Output Adapter shall Composition Root を `OutputSceneBootstrapper` 完了後に初期化し、IPC ハンドラ登録 → OSC 起動 → 初期 `cameras/list` publish の順で起動する（see CSO-14）。
2. When PlayMode が停止したとき、the Camera Switcher Output Adapter shall **逆順で**: 初期 `cameras/list` の最終状態を維持しないまま、(1) `uOscServer.StopServer()`、(2) IPC ハンドラ Dispose（`OutputCommandHandlerRegistration.Dispose`）、(3) Camera GameObject 破棄、(4) `CameraOscReceiverHost` GameObject 破棄、を実行する。
3. The Camera Switcher Output Adapter shall Application Quit / OnApplicationQuit / OnDestroy の各経路で同等の解放処理を行う。
4. While PlayMode が 5 回以上繰返される間, the Camera Switcher Output Adapter shall ハンドル数 / GC 可到達参照 / OSC ソケット占有 / Camera 残存数 がベースラインから増加しない（リーク許容ゼロ）。
5. The Camera Switcher Output Adapter shall **Edit モードでは起動しない**（D-9）。Editor の Edit モードに `MonoBehaviour` インスタンスを作らない、または `[ExecuteAlways]` を使わない構造を維持する。
6. The Camera Switcher Output Adapter shall Domain Reload 中の状態維持を試みず、PlayMode 開始のたびに新しいインスタンスから初期化する。
7. The Camera Switcher Output Adapter shall スタンドアロンビルド時も Editor PlayMode 時も同一の起動・停止フローで動作する。

---

### Requirement 12: 配信適合性とフェイルセーフ

**Objective:** 配信運用者として、本 spec の挙動が配信に載るメイン出力（Display 2+）を一切汚染せず、内部エラーや OSC 断 / IPC 断などの障害が発生してもメイン出力描画が継続される状態を得たい。

**Note:** OR-1 / 5.6（メイン出力サーフェスへの UI 描画禁止）を継承。`output-renderer-shell` の `MainOutputNoOverlayTests` の対象範囲に本 spec の追加実装も含める。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall **メイン出力サーフェス（Display 2+）に `OnGUI` / `IMGUI` / `UIDocument` / `Debug.Log` のオーバーレイ等を一切描画しない**（OR-1 / 5.6）。診断ログは Unity Console（`Debug.Log` レベル制御済み）と IPC `camera/error` event のみ。
2. If OSC 受信中に例外が発生した場合, the Camera Switcher Output Adapter shall 例外を捕捉して当該フレームを破棄し、診断ログに記録、以降の受信を継続する（Requirement 2.4 と整合）。
3. If IPC ハンドラ実行中に例外が発生した場合, the Camera Switcher Output Adapter shall `IOutputCommandDispatcher` の例外捕捉契約（Req 3.6 / 5.5 / 9.5、shell 側で try/catch）に従属する。本 spec ではハンドラ内で本来回復可能なエラー（未知 cameraId 等）を `camera/error` event で UI 側に通知する。
4. If `uOscServer` の起動が失敗した場合, the Camera Switcher Output Adapter shall IPC 経路を継続し、UI 側の Camera CRUD / Volume 編集は引き続き反映される（OSC 受信のみ機能停止）。`camera/error` event で `OscStartupFailed` を通知。
5. If `core-ipc-foundation` の WebSocket が切断された場合, the Camera Switcher Output Adapter shall `core-ipc-foundation` の接続管理層に従属し、本 spec はその間 OSC 受信のみで Camera を更新し続ける（UDP 特性で UI 操作は届かないが、現状の active-set 状態は維持）。
6. The Camera Switcher Output Adapter shall 本 spec が原因でメイン出力フレームが 1 フレームでもフリーズすることがない構造を維持する（GC アロケーション抑制 + 同期ロード回避、see CSO-4, docs/requirements.md §6.1）。
7. The Camera Switcher Output Adapter shall すべての診断情報を構造化ログ（`Debug.Log` + 必要に応じて `IDiagnosticsLogger` 相当の shell API）として記録し、ログレベル切替に従属する（Req 9.7 と整合）。

---

### Requirement 13: 本 spec 単独での検証可能性

**Objective:** 開発者として、本 spec を `camera-switcher-tab`（UI 側）の実装と独立に検証したい。そうすれば Wave 3 のメイン出力アダプタ群を並行開発する際に、UI 側不在でも OSC 受信 + IPC 契約を独立にテストできる。

**Note:** 本要件は `core-ipc-foundation` Requirement 8（自己ループ）と `output-renderer-shell` の単独検証構造を活用する。OSC 送信側のテスト用クライアント（同プロセス内 `uOscClient`）を立てて 138 byte blob を送信し、本 spec の Camera 適用を確認する。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall **UI 側（`camera-switcher-tab`）が存在しない状態でも**、自前のテストハーネスから IPC envelope を直接注入することで `camera/command` / `camera/{id}/metadata/{key}` / `camera/{id}/volume/*` / `camera/{id}/volume/overrides/metadata` の全ハンドラを実行できる構造とする。
2. The Camera Switcher Output Adapter shall OSC 受信のテストを `uOSC.uOscClient`（同プロセス）で UCAPI Flat Record を 1000 件 / 60Hz で送信し、本 spec の Camera が適用されていることを検証可能な構造とする（see Requirement 2.9）。
3. The Camera Switcher Output Adapter shall `IOutputCommandDispatcher` を Fake / In-Memory 実装で差し替え可能とし、shell 不在でも単体テストが書ける構造を提供する。
4. The Camera Switcher Output Adapter shall `ICameraIdAllocator` / `ILocalVolumeBinder` / OSC 受信の port 抽象を提供し、テスト時に Fake で差し替え可能とする。
5. The Camera Switcher Output Adapter shall 提供するテストケース：OSC blob 受信から Camera 適用までのラウンドトリップ（1000 件 / 60Hz）、`camera/command add → camera/created` のラウンドトリップ、`active-set → cameras/active 権威 echo + Camera.enabled` 切替、`volume/overrides/metadata` Request → Reflection スキーマ Response、`active-set → Local Volume.enabled` 連動切替、PlayMode 繰返しでのリソース解放、OSC 起動失敗時の IPC 経路継続。
6. The Camera Switcher Output Adapter shall `Samples~/MockedOscSenderSample/`（または同等）に手動検証手順を含める：(1) PlayMode 起動 → (2) 同梱の `uOscClient` で `127.0.0.1:9000` に Flat Record を送信 → (3) `CameraOscReceiverHost` の `onDataReceived` が発火 → (4) `CamerasRoot` 配下の Camera transform が更新されることを Inspector で確認。
7. The Camera Switcher Output Adapter shall Editor PlayMode と スタンドアロンビルドの両方でテストを通過する構造を維持する（D-9）。

---

### Requirement 14: 観測性・診断可能性

**Objective:** 開発者として、本 spec の動作中に発生する OSC 受信件数 / IPC 受信件数 / 適用失敗件数 / cameraId 採番状況 / Volume 適用失敗 を即座に切り分けて把握したい。

#### Acceptance Criteria

1. The Camera Switcher Output Adapter shall 初期化（Composition Root 起動 / IPC ハンドラ登録完了 / OSC サーバ起動完了 / 初期 `cameras/list` publish）の各段階の開始・完了・失敗をログ出力する。
2. The Camera Switcher Output Adapter shall OSC 受信件数（cameraId 別）/ 受信失敗件数 / UCAPI デコード失敗件数 を診断 API（`CameraSwitcherOutputAdapterDiagnostics.GetSnapshot()`）から取得可能にする。
3. The Camera Switcher Output Adapter shall IPC ハンドラ実行件数（topic 別）/ 失敗件数 / `camera/error` 発行件数 を診断 API から取得可能にする。
4. The Camera Switcher Output Adapter shall Camera Registry の現状（cameraId 一覧 / アクティブ cameraId / Default Camera fallback 状態）を診断 API から取得可能にする。
5. The Camera Switcher Output Adapter shall すべての診断ログを Unity Console + （shell 側 `IDiagnosticsLogger` が利用可能なら）shell に流し、メイン出力サーフェスへ一切描画しない（Requirement 12.1 と整合）。
6. The Camera Switcher Output Adapter shall ログレベルを外部から切替可能にする（shell の `IDiagnosticsLogger.MinimumLevel` に従属）。

---

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案、要件レベル厳選、上流 spec の決定を積極継承）
- **本 spec 固有の決定**: 16 件（CSO-1〜CSO-16）
- **継承**:
  - `core-ipc-foundation` の D-1 / D-3 / D-4 / D-5 / D-7 / D-9 / D-10 / D-11
  - `output-renderer-shell` の OR-1（メイン出力に UI 描画禁止）/ OR-2（state last-write-wins）/ Req 1.1〜1.8 / Req 3.x / Req 4.x / Req 5.6
  - `camera-switcher-tab` の CSW-1〜CSW-16 すべて（特に CSW-5 採番権限、CSW-6 OSC アドレス階層、CSW-9 60Hz 同期、CSW-12 active-set 連動 Volume、CSW-15 OSC/IPC 独立フェイルセーフ）
- **主要な発見（本 spec 固有）**:
  - `uOSC.uOscServer` の `onDataReceived` が **MonoBehaviour.Update から発火**するため Unity メインスレッドで来ることを PackageCache 確認で固定（CSO-3）。追加の SynchronizationContext marshal を実装しない方針が技術的に成立する。
  - `UCAPI4Unity.UcApi4UnityCamera.ApplyToCamera(byte[], Camera)` が OSC blob → Unity Camera 適用のワンショット API として既に存在することを確認（CSO-4）。本 spec はこの API を直接利用するだけで Camera への反映が完結する。
  - URP の Local Volume を **`isGlobal = true` + `enabled` 切替** で active-set 連動を実現する（CSO-10）。Trigger Collider / Camera Volume Layer Mask を使わずシンプルな実装が可能。
  - `output-renderer-shell.IOutputSceneRoots.DefaultCamera` は **本 spec が 1 台目の Camera を追加した時点で `enabled=false` に切り替えるフォールバック扱い**（CSO-7）にすることで、shell の不変条件（DefaultCamera は破棄しない）を侵さず active-set 切替を実現できる。
  - cameraId 採番は **`cam-{NNNN}` 連番、削除しても再利用しない**（CSO-6）。`OscAddressBuilder` の許容文字に収まり、起動レースで未知 cameraId 受信を破棄する規律と整合する（5.6）。
  - プレビュー機能は **本フェーズではプレースホルダ実装**（CSO-13）。最小機能（CSW-2）から外し、Wave 3 後段で本格化する。`camera-switcher-tab` 側もプレースホルダ表示で凌ぐ仕様と整合。
- **残留リスク（設計フェーズで継続検討 / 実装時に確認）**:
  - R-CSO-1: OSC ポート最終確定（`docs/integration-plan.md` §7.2）。`127.0.0.1:9000` は仮置き。確定時に `camera-switcher-tab` 側の送信ポート設定と一括更新が必要。
  - R-CSO-2: URP の `VolumeManager.instance.baseComponentTypeArray` API シグネチャ（Unity 6.3 / URP 17 系）の最終確認。Reflection 抽出ロジックの adapter 内分離で吸収するが、API 変更時の影響範囲を design.md 側で記録する。
  - R-CSO-3: `IOutputCommandDispatcher` の topic prefix サブスクライブ機能（`camera/+/metadata/+` 相当）の有無。現状の design では cameraId ごとに dynamic 登録が必要かどうかは shell 側 design.md / 実装で確認。最悪は cameraId が増減するたびに動的登録 / 解除する。
  - R-CSO-4: `ICameraIdAllocator` の連番枯渇対応（運用上 9999 で溢れるかは運用想定外だが、`cam-{guid8}` フォールバック等の戦略）。
  - R-CSO-5: 既存の `core-ipc-foundation` 出力サーバ API（PublishState / PublishEvent）の具体的な呼び出し形（`IUiCommandClient` 系の名称が UI 側、出力側の対応 API は確認が必要）。実装時に具体名を確定し、design.md で参照する。
  - R-CSO-6: プレビュー RenderTexture 本格実装の段階移行計画（CSO-13）。Wave 3 後段または別 spec への切り出し。
  - R-CSO-7: `output-renderer-shell` の `MainOutputNoOverlayTests` の対象範囲拡張（本 spec が追加する新規 GameObject / Camera / Volume が誤って Display 2+ にオーバーレイを描画しないことの構造的検証）。
