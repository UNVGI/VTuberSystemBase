# Research & Design Decisions — camera-switcher-tab

## Summary

- **Feature**: `camera-switcher-tab`
- **Discovery Scope**: Complex Integration（Wave 2 タブ spec、上流 4 spec + 採用 2 パッケージ + 新規 OSC チャネルの統合）
- **Key Findings**:
  - **二系統チャネル分離**（CSW-1）は Hexagonal の adapter 層で `IUiCommandClient`（WebSocket/JSON）と `IUcapiOscEmitter`（OSC/UDP）を完全に分離することで、タブロジックから両プロトコル差異を隠蔽できる。
  - **UCAPI（Flat Record 128 byte + 10 byte header + CRC16-CCITT + MessagePack 対応）** は C++ DLL + UCAPI4Unity UPM で提供され、Unity Camera のパラメータを一括シリアライズする POD 構造を持つ。CRC16 検証・Flat Record のエンコードは UCAPI4Unity の公開 API に委譲し、本 spec は薄いアダプタ層（`IUcapiFlatRecordSerializer`）を介して差し替え可能性を確保する。
  - **OSC ライブラリは hecomi/uOSC** を第一候補に採用する（MIT ライセンス、byte[] blob サポート、送信はバックグラウンドスレッド、コールバックはメインスレッド、動的ポート切替）。Flat Record を 1 引数の blob として `/ucapi/camera/{cameraId}/flat` アドレスへ載せる。

## Research Log

### Topic 1: UniversalCamerawork (UCAPI) の公開仕様と Unity 統合形態

- **Context**: CSW-1 / Requirement 3 で「UCAPI Flat Record（128 byte）+ Header（10 byte）+ CRC16-CCITT + MessagePack 対応」をカメラ状態共通フォーマットとして採用することが要件で固定されているため、UCAPI4Unity UPM の公開 API と Unity Camera 値との単位差異を棚卸しする必要がある。
- **Sources Consulted**:
  - [UniversalCamerawork（GitHub）](https://github.com/Hidano-Dev/UniversalCamerawork) — C 互換 Flat Record API の概要、10 byte header + 128 byte record + CRC16-CCITT、MessagePack 対応、タイムコード 14 種（23.976〜240 fps、drop-frame 含む）、Windows x64 MSVC v143 ビルドが現行サポート、別パッケージ UCAPI4Unity が Cinemachine 統合を提供。
- **Findings**:
  - Flat Record は POD（C 互換）であり C# からは `[StructLayout(Sequential)]` 相当で扱えるサイズ（128 byte）。C++ DLL が CRC16 計算を担う想定で、本 spec は UCAPI4Unity の公開 API を介した変換以外を自前で書かない。
  - 記録される camera パラメータは position、rotation（matrix）、focal length、aperture、sensor dimensions、clipping planes、timecode を含む。Unity Camera の `transform.position / rotation`、`Camera.focalLength`（Physical Camera 有効時）、`sensorSize`、`nearClipPlane` / `farClipPlane` に 1:1 で写像可能。
  - Rotation の matrix 表現 vs Unity の Quaternion は UCAPI4Unity 側で変換 API を提供する想定（リポジトリの記述から推定）。本 spec 側は Quaternion → UCAPI rotation matrix 変換を UCAPI4Unity API に委譲する（自前では書かない）。
- **Implications**:
  - `IUcapiFlatRecordSerializer` adapter を定義し、UCAPI4Unity の公開 API を薄く包む。UCAPI のバージョン更新は adapter 実装差分のみで吸収（Requirement 3.7, 3.8）。
  - Unity Camera → UCAPI 変換の具体マッピング（単位・座標系・Physical Camera 有効化）は実装フェーズで UCAPI4Unity の API リファレンスに照合しながら確定する（R-CSW-4 の残留項目）。
  - CRC16 計算は UCAPI DLL 側で実施される前提のため、本 spec は CRC 検証ロジックを持たない（受信側の責務、Requirement 5.5）。
  - DLL 配布は Native Plugin フォルダ同梱を既定とし、将来的な Addressables 化は非目標（UCAPI C++ バイナリは Addressables の Managed Asset ではないため R-CSW-12 のまま残留）。

### Topic 2: Unity 向け OSC ライブラリの選定

- **Context**: CSW-7 / Requirement 4 で UI 側（本タブ）を OSC 送信クライアント、メイン出力側を OSC 受信サーバとする。毎フレーム（目安 60 Hz）で Flat Record（138 byte）+ OSC アドレスパターン + 1 引数（blob）を送る用途に適した Unity 向け OSC ライブラリを選定する必要がある（R-CSW-5 の残留項目）。
- **Sources Consulted**:
  - [hecomi/uOSC（GitHub）](https://github.com/hecomi/uOSC) — MIT、int/float/string/bool/byte[] サポート、Bundle 対応、送信はバックグラウンドスレッド + キュー、コールバックはメインスレッド、動的ポート/アドレス切替、UPM 対応、v2.2.0（2023-01）。
  - [stella3d/OscCore（GitHub）](https://github.com/stella3d/OscCore) — パフォーマンス重視（ゼロアロケーション）だが blob 型サポートは検証要（メッセージ API 中心）。
  - [Iam1337/extOSC（GitHub）](https://github.com/Iam1337/extOSC) — 多プラットフォーム対応、MIT、機能豊富だが送信頻度・スレッドモデルの差異確認要。
- **Findings**:
  - **uOSC** は byte[] 引数を 1 つの OSC argument として自然に載せられる（UCAPI Flat Record blob 138 byte をそのまま 1 引数で渡せる）。スレッドモデルは CSW-8（I/O はワーカー、コールバックは Unity メインスレッド）と整合。動的アドレス切替は CSW-5（cameraId 生成完了後に送信開始）にも適する。MIT ライセンスなので同梱・派生が自由。
  - **OscCore** は高速だが内部は各引数を別タグで格納する OSC の型文字列 API に寄っており、138 byte blob を 1 引数で送る用途では uOSC の方が素直。
  - **extOSC** は UI ドリブンな API が中心で、高頻度プログラマティック送信には uOSC の方が軽量。
- **Implications**:
  - **uOSC を第一候補** として採用する。参照は Git URL (`https://github.com/hecomi/uOSC.git#upm`) 経由の UPM 取り込みを想定（本 spec の asmdef から参照）。
  - `IUcapiOscEmitter` adapter を定義し、uOSC の具体型（`uOscClient`）への依存を adapter 内部に閉じ込める。将来 OscCore / extOSC への差し替えは adapter 実装差分のみで可能（Requirement 15.7 のテスト容易性にも寄与）。
  - OSC の具体パス `/ucapi/camera/{cameraId}/flat` は UCAPI の慣例が確認できない段階の仮置きとし、設計確定値として本 spec の Data Contracts で定義する（R-CSW-2 は本フェーズで暫定値として固定）。

### Topic 3: Unity Camera を 1 枚の RenderTexture に投影するプレビュー手法

- **Context**: CSW-3 / CSW-16 / Requirement 2 で「SceneViewStyleCameraController による編集対象カメラのプレビューを Display 1 側（タブ UI 内）の RenderTexture に描画」「マルチプレビュー（全カメラサムネイル）+ 大アクティブカメラプレビューの二層構成」を求められる。メイン出力カメラ（Display 2+）と干渉しない描画境界を確認する必要がある。
- **Sources Consulted**:
  - 採用パッケージ `SceneViewStyleCameraController`（GitHub: Hidano-Dev/SceneViewStyleCameraController）
  - `output-renderer-shell` の Boundary Commitments（`CamerasRoot` / `LightsRoot` / `VolumeRoot` 提供、カリングマスク契約）
  - `ui-toolkit-shell` の `IAsyncAssetLoader`、`PanelSettings targetDisplay=0`（Display 2+ 描画禁止の構造保証）
- **Findings**:
  - メイン出力側（`output-renderer-shell` の `CamerasRoot` 配下）の Camera GameObject に対し、**UI 側タブは別途プレビュー専用 Camera を Display 1 側のレイヤーに生成**して `targetTexture` に RenderTexture を割り当てる。プレビューカメラはメイン出力カメラと **別のカメラ**（Requirement 2.6 の SL-1 踏襲）。
  - マルチプレビューは N 台のカメラそれぞれにサムネイル用 RenderTexture（小解像度、例 192×108）を割り当て、更新頻度は設計で絞る（非アクティブカメラは例えば 15 fps）。大きなアクティブプレビューは高解像度（640×360 程度）で 60 Hz。
  - メイン出力描画との干渉回避は (a) `Camera.targetTexture` を設定することで `targetDisplay` の自動描画が無効になる Unity 仕様、(b) メイン出力カメラのカリングマスクにプレビュー RenderTexture に映したい UI 専用レイヤーを含めない契約（output-renderer-shell）で二重に担保。
  - プレビュー Camera 自体は **メイン出力シーン側の GameObject として生成**する（メイン出力カメラと同シーンを共有）。これは SL-1 で引きカメラが採用した方式と整合し、ステージ・Light・キャラクター等を共通シーンの実オブジェクトとしてプレビューに映せる。
- **Implications**:
  - プレビュー Camera の生成・破棄は UI 側から `camera/command` の `preview-attach` / `preview-detach` のような専用 event か、または `CamerasRoot` 配下のプレビュー用子ノードに対する `PublishEvent` で指示する。本 spec では **SL-1 と同様に「プレビュー Camera はメイン出力側が生成するが、映す対象カメラ（cameraId）は UI 側から指定」** のモデルを採用する。編集対象 cameraId の transform は OSC で送るだけで、プレビューカメラは単純に「指定された cameraId のカメラが映す光景と同じ視点から描画する」＝**メイン出力側で対象カメラを clone/mirror してプレビューに表示**する仕組みとする。
  - マルチプレビューは UI 側からの「プレビュー購読要求」イベント（`camera/preview/subscribe` に cameraId 集合）で開始する。受信実装はメイン出力側の責務だが、本 spec の IPC 契約として UI 側が要求する API を定義する。
  - プレビュー描画頻度・解像度・最大カメラ数（R-CSW-16-1, R-CSW-9-2 残留）は **設定ファイル駆動**（`PreviewConfig`: thumbnailResolution, thumbnailFps, activeFps, maxPreviewCount）とし、本 spec は既定値のみ提示する。

### Topic 4: URP Local Volume のメタデータ駆動編集

- **Context**: CSW-11 / Requirement 8 で「Local Volume の Override リスト + 各 Override の enabled/param を SL-7 と同じメタデータ駆動の動的 UI で生成」するため、URP の `VolumeComponent` 公開 API（Reflection で取得する項目名・型・レンジ・既定値）を確認する必要がある。
- **Sources Consulted**:
  - `stage-lighting-volume-tab` の先行設計（SL-7 が Global Volume で同じ戦略を採用）
  - Unity URP `VolumeComponent` / `VolumeParameter<T>` ドキュメント（knowledge cutoff 内の知識）
- **Findings**:
  - URP の `VolumeComponent` サブクラス（Bloom, Tonemapping, ColorAdjustments, DepthOfField, FilmGrain 等）は `VolumeParameter<T>` 型のフィールドを持ち、Reflection で列挙可能。各 `VolumeParameter` は `overrideState`（= enabled）、`value`、`min/max` 属性（`ClampedFloatParameter` 等）を公開。
  - メタデータ取得は **メイン出力側が Reflection で構築した JSON スキーマ** を `volume/overrides/metadata` Request で UI 側へ返す設計とし、UI 側は URP 型に直接依存しない（SL-7 と同方針、CSW-11）。
  - Local Volume は Global Volume と Unity 実装上は同じ `Volume` コンポーネントで `isGlobal=false` + Collider + Priority の違いのみ。本 spec では **Camera GameObject 配下に Local Volume + BoxCollider を配置** する既定を想定するが、詳細はメイン出力側の責務とし、本 spec は IPC 契約（metadata 取得、Override 追加削除、enabled/param 送信）のみを定義する。
- **Implications**:
  - `IUiCommandClient.RequestAsync<VolumeMetadataRequest, VolumeMetadataResponse>("camera/{id}/volume/overrides/metadata", ...)` を用意し、返却値に Override 種別一覧と各 Override の param スキーマ（name, typeTag, min, max, default, displayName）を含める。UI 側は `ui-toolkit-shell` の `VsbSlider` / `VsbColorPicker` / `VsbToggleGroup` を動的に割り当てる。
  - Local Volume 自体の有効/無効（`volume.enabled`）とカメラ active-set の連動はメイン出力側の自動処理（CSW-12）。UI 側は `camera/{id}/volume/enabled` state を購読するのみで、active-set に対して Local Volume enabled を追送しない。
  - 「オペレーター操作中 state 受信時の衝突解消」（Requirement 8.10）は CS-5 / SL-7 の R-6 と同じ「操作中はコントロールを server-echo 抑止（SL-7 の扱いに倣う）」方式を推奨値として設計に記載する。

### Topic 5: OSC UDP と WebSocket の二チャネル共存でのライフサイクル整合

- **Context**: CSW-15 / Requirement 10, 12, 13 で「OSC 断と WebSocket 断を独立事象として扱う」「PlayMode 開始〜停止でクリーン」「Editor/Standalone で API 挙動同一」を求める。`core-ipc-foundation` の `CoreIpcRuntime` ライフサイクル（PlayMode 限定、D-9）と整合する必要がある。
- **Sources Consulted**:
  - `core-ipc-foundation` design.md §Flow 4 PlayMode ライフサイクル、§ConnectionStateMachine
  - `ui-toolkit-shell` design.md §UiShellLifecycleDriver、§Flow 1 起動シーケンス
  - hecomi/uOSC のライフサイクル（`uOscClient` は MonoBehaviour、有効/無効で送信開始/停止、動的 port 切替可）
- **Findings**:
  - `CoreIpcRuntime` は `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` で起動、`Application.quitting` または `EditorApplication.playModeStateChanged(ExitingPlayMode)` で停止。`ui-toolkit-shell` の `UiShellBootstrapper` も同じ仕組みに乗る。
  - 本タブは **`ui-toolkit-shell` が起動し `ITabLifecycleHandle` を提供した後** に初期化される（タブレベルの依存順序）。OSC 送信クライアントはさらに **`core-ipc-foundation` の IPC 接続が確立し、メイン出力側が cameraId 受け入れ可能になった後** に起動するのが安全（Requirement 10.1）。
  - UDP は TCP と違い「接続」概念がないため、送信先ポート不在でも送信はエラーを返さない（ICMP Port Unreachable が非同期で返るだけ）。CSW-15 の「OSC 断検出は原理的に弱い」前提は正しい。実用上は (a) 初期化時の `Socket.Bind` 失敗、(b) ICMP を受けたときの UdpClient 例外（Windows では `SocketException.ConnectionReset`）を検出する程度に留める。
  - hecomi/uOSC は内部で `UdpClient.SendAsync` を使い、例外はキューで隔離される実装のため、本 spec が直接 UdpClient を触る必要はない。Adapter で uOSC のエラーコールバックを観測して診断 API に露出すれば足りる。
- **Implications**:
  - 本 spec の Lifecycle は「UI シェル起動 → `RegisterTab(TabId.CameraSwitcher)` で `ITabLifecycleHandle` 取得 → IPC 接続成立を `IConnectionStatus.OnStatusChanged` で待機 → `IUcapiOscEmitter.Start(host, port)` → 編集対象 cameraId 確定後に送信開始」という明示的な Gated 初期化を採用する。
  - PlayMode 停止時の解放は `ITabLifecycleHandle.OnDisposed` をフックとし、`IUcapiOscEmitter.Stop()` → RenderTexture.Release() → 購読解除 → プリセット未フラッシュ書き出しの順で行う。
  - スタンドアロンと Editor PlayMode の差異は `ui-toolkit-shell` の `UiShellLifecycleDriver` が吸収しているため、本 spec レベルでは分岐不要（Requirement 13）。

### Topic 6: カメラ transform の送信レート設計（CSW-9 の「メイン出力描画フレーム同期」）

- **Context**: CSW-9 で「OSC 送信はメイン出力描画フレーム同期（60 fps 時は実質 60 Hz）」「同一フレーム内の複数変化は 1 メッセージに集約」「受信側は最新値優先」と決定済み。Unity のどのフェーズで送信するか（Update / LateUpdate / MonoBehaviour 独自コルーチン）を確定する必要がある（R-CSW-9-2）。
- **Sources Consulted**:
  - Unity MonoBehaviour ライフサイクル（Update → LateUpdate → onPreCull → onRender → OnPostRender の順、LateUpdate はカメラ行列確定後）
  - `core-ipc-foundation` の `PlayerLoop.PreUpdate` 経由ディスパッチ（参考実装パターン）
- **Findings**:
  - Unity Camera の `transform` は Update 段階で Scene View 操作（SceneViewStyleCameraController）が反映し、LateUpdate では既にカメラの「このフレームの最終 transform」が確定している。OSC 送信は **LateUpdate 末尾**（または `Camera.onPreCull` 相当）が最も確実。
  - 編集対象が複数ある場合（マルチプレビュー用途で複数カメラを同時送信する設計は本フェーズ非目標）、切替前の旧カメラへの送信は「編集対象切替と同じフレーム」で停止（Requirement 4.12）。
  - プレビュー描画と OSC 送信のフレームレートは、メイン出力描画フレーム（Display 2+）に合わせる。UI プレビュー側の RenderTexture 描画は Display 1 の PanelSettings のフレームに従うが、対象 Camera 自体は LateUpdate で 1 回 transform が確定するだけなので、OSC 送信と RenderTexture 描画のフレーム齟齬は発生しない。
- **Implications**:
  - `IUcapiOscEmitter` は `FrameTick()` メソッドを公開し、タブ側の `LateUpdate` 相当（内部で `PlayerLoopSystem` 挿入または `CameraPreviewBehaviour.LateUpdate()`）から 1 フレーム 1 回呼ぶ。最終 transform のみがキャプチャされ OSC 送信される設計とする。
  - 60 fps 未満で描画が遅延した場合、OSC 送信頻度も連動して下がる（CSW-9 の受容済みトレードオフ）。
  - 受信側（メイン出力）には「同 cameraId の最新値優先適用」を契約として要求（Requirement 5.4、CSW-9 と同思想の OSC 側再現）。

### Topic 7: プリセット永続化の形式と保存先

- **Context**: CSW-13, CSW-14, Requirement 11 で「カメラリスト + 各カメラ初期 transform デフォルト + Local Volume 構成 + アクティブカメラ」を名前付きプリセットとして保存し、デバウンス後にファイルへフラッシュする。保存先・形式を確定する必要がある（Requirement 11.10）。
- **Sources Consulted**:
  - `character-selection-tab` の CS-8 / CS-9 プリセット永続化方式
  - `stage-lighting-volume-tab` の SL-8 / SL-9 プリセット永続化方式（同一パターン）
  - Unity `Application.persistentDataPath` の扱い（OS ごとの配置、Editor と Standalone で同一パスが提供されること）
- **Findings**:
  - CS / SL タブが採用する方針は「`Application.persistentDataPath` 配下の JSON ファイル、タブ種別ごとに 1 ファイル、JSON 内に複数プリセット（name → PresetPayload）を格納」。本 spec も同方針を踏襲するのが自然。
  - 初期 transform は float（position × 3, rotation quaternion × 4, focalLength × 1）程度の小さなペイロードなので JSON で十分。Local Volume 構成は Override 種別名 + enabled + param 値の Dictionary で記述。
  - ファイル名は `camera-switcher-presets.json` を既定とし、設定 API から差し替え可能（利用者プロジェクトが別パスを指定できる構造、Requirement 11.10）。
- **Implications**:
  - `IPresetStore` を adapter として定義し、既定実装は `FileSystemPresetStore`（JSON + `Application.persistentDataPath`）。テスト用に `InMemoryPresetStore` を提供（Requirement 15.4）。
  - デバウンス具体値は **500 ms**（SL-9 と同値を推奨）を既定とし、`TimeProvider` 抽象（Requirement 15.8）で差し替え可能。
  - 破損ファイルは `*.bak.{timestamp}` にリネームしてフォールバック（Requirement 11.7、CS-11 と同思想）。

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| Hexagonal (Ports & Adapters) | タブ core に UCAPI シリアライザ / OSC / IPC / Volume metadata / Preset store を全て port として抽象化 | 差し替え前提（CSW-2）に最適、テストダブル容易、Wave 2 スペック群と整合 | Port 数が多くなり初学者にオーバヘッド | **採用**。差し替え要件が CSW-2 で要件化されているため必須。 |
| MVVM | View（UXML）・ViewModel（状態）・Model（IPC/OSC/プリセット） | Unity UI Toolkit の Databinding と親和 | 差し替えポイントがモデル層だけに集中しない、テストが ViewModel 中心に偏り OSC adapter の単体試験がやりにくい | 不採用。Hexagonal の「Domain」は ViewModel 相当として内包可能。 |
| Monolithic MonoBehaviour | 1 枚の `CameraSwitcherTabBehaviour` に全処理を集約 | 初期実装が速い | CSW-2 の差し替え要件を満たせない、テスト困難、OSC とプリセット I/O が UI スレッドに漏れる | 不採用。 |

**選定結果**: Hexagonal (Ports & Adapters)。核ドメインに `CameraSwitcherCoordinator`（タブ全体の状態機械）、port に `IUcapiFlatRecordSerializer` / `IUcapiOscEmitter` / `IUiCommandClient`（ui-toolkit-shell 経由）/ `IUiSubscriptionClient`（同）/ `IPresetStore` / `ITimeProvider` を配置。

## Design Decisions

### Decision: カメラ transform は OSC、UI 操作は WebSocket（CSW-1 の具体化）

- **Context**: カメラ状態の毎フレーム連続値（60 Hz 想定）と UI 操作の離散コマンドを同一チャネルで送るべきかの判断。
- **Alternatives Considered**:
  1. すべて core-ipc-foundation（WebSocket/JSON）に乗せる — D-7 の state coalesce に頼ればサイズ的には許容だが、JSON 毎フレームエンコードの負荷と 1 MB 上限（D-11）近辺への接近、UCAPI バイナリ互換性の喪失が懸念。
  2. すべて OSC に乗せる — UI 操作（create/delete/active-set 等）のタイムアウト契約と Request/Response を UDP 上に独自実装する必要が生じ、`core-ipc-foundation` の D-8 と重複。
  3. 二系統分離（本案）— transform は OSC、UI 操作は WebSocket。
- **Selected Approach**: 二系統分離。`IUcapiOscEmitter` port を独立に定義し、`IUiCommandClient` / `IUiSubscriptionClient`（ui-toolkit-shell 公開）は一切 transform を扱わない。
- **Rationale**: CSW-1 の要件通り。UCAPI Flat Record はバイナリ POD 前提のため JSON 化の意味がない。UI 操作は Request/Response と FIFO event 保持を必要とするため WebSocket の既存契約を活用。
- **Trade-offs**: 二チャネル管理の複雑性（接続断の独立扱い、起動順序制御）。本 spec の Lifecycle セクションで Gated 初期化を明示することで緩和。
- **Follow-up**: Requirement 10（OSC ライフサイクル）と Requirement 12（フェイルセーフ）の実装時に、OSC 断と WebSocket 断の診断状態独立露出を試験する。

### Decision: OSC ライブラリとして hecomi/uOSC を採用

- **Context**: Unity 向け OSC ライブラリ 3 候補（uOSC / OscCore / extOSC）の比較（R-CSW-5）。
- **Alternatives Considered**:
  1. uOSC — MIT、byte[] blob を 1 引数で扱える、スレッドモデルが CSW-8 と整合、UPM 対応。
  2. OscCore — 高速（ゼロアロケーション重視）だが API が型文字列ドリブンで 138 byte blob 送信には uOSC の方が素直。
  3. extOSC — UI ドリブン API、機能豊富だが高頻度送信用途でのオーバヘッドが懸念。
  4. 自作 — OSC 1.0 の送信サブセットは小さいが CSW-15 / Requirement 10 のライフサイクル管理との整合検証が新規作業になる。
- **Selected Approach**: **uOSC v2.2.0 以降** を UPM 参照で採用。`IUcapiOscEmitter` 実装（`UoscFlatRecordEmitter`）で uOSC の `uOscClient.Send(address, byte[])` を呼ぶ。
- **Rationale**: MIT ライセンスで同梱・派生自由。Flat Record を 1 argument の blob として自然に送れる。動的ポート切替 API が CSW-5（cameraId 確定後送信開始）と整合。スレッドモデル（送信バックグラウンド、コールバックメインスレッド）が CSW-8 / D-3 と整合。
- **Trade-offs**: uOSC のアクティブメンテナンス頻度は低い（最新 2023-01）。将来メンテが途絶えたら OscCore へ差し替え可能（adapter 分離で軽量）。
- **Follow-up**: 実装時に uOSC の `uOscClient` が PlayMode 停止時に UDP ソケットを確実に解放することを検証（Requirement 10.2, 10.4）。

### Decision: OSC アドレスパターン `/ucapi/camera/{cameraId}/flat`、Flat Record を 1 引数 blob

- **Context**: CSW-6 の暫定値の確定（R-CSW-2）。UCAPI 側のアドレス規約が現時点で公開文書から確認できないため、本 spec で仮固定する必要がある。
- **Alternatives Considered**:
  1. `/ucapi/camera/{cameraId}/flat` — 階層化、cameraId で受信ディスパッチ容易、UCAPI 名前空間プレフィクス。
  2. `/camera/{cameraId}` — 短いが UCAPI 側が将来別アドレス規約を採る場合に衝突リスク。
  3. `/vtuber-system-base/camera/{cameraId}/ucapi-flat` — プロジェクト固有プレフィクスで衝突回避だが長い。
- **Selected Approach**: `/ucapi/camera/{cameraId}/flat` を本 spec の既定アドレスとして固定。設定ファイルで `addressPrefix`（既定 `/ucapi/camera`）を差し替え可能にし、UCAPI 側が別規約を公開した場合は設定変更で追従できる構造とする。
- **Rationale**: UCAPI 名前空間を尊重しつつ、cameraId による多重化（CSW-6）を自然に表現。プレフィクス分離で将来追従可能。
- **Trade-offs**: UCAPI 本家の正規アドレス規約が公開されたとき、既存プリセットや設定ファイルが旧プレフィクスを参照している場合に変更が必要。移行は設定ファイル変更のみで完了する軽量さを保つ。
- **Follow-up**: UCAPI リポジトリの OSC 関連 issue/ドキュメント更新を定期監視（R-CSW-2 残留）。

### Decision: プレビューカメラはメイン出力側所有、UI 側は「映す cameraId」のみ指示

- **Context**: CSW-3 / CSW-16 と SL-1 の整合。UI 側でプレビューカメラを所有すると、メイン出力側シーン（キャラクター / ステージ / Light）へのアクセスが Wave 2 の境界を跨ぐため難しい。
- **Alternatives Considered**:
  1. UI 側所有 — UI 側でプレビュー Camera GameObject を生成しメイン出力シーンのオブジェクトを映す。Wave 2 の境界を越えるシーン共有が必要。
  2. メイン出力側所有（本案）— プレビュー Camera はメイン出力側が生成、UI 側は「この cameraId を映したプレビュー RenderTexture が欲しい」と IPC で要求、返ってきた RenderTexture ID を UI Toolkit に `VisualElement.style.backgroundImage` で貼る（または Unity の CustomRenderTexture を指す）。
  3. 完全別シーン — UI 側でステージ・Light・キャラクターの軽量クローンを持つ。実装コストが大きく SL-1 の方針と矛盾。
- **Selected Approach**: メイン出力側所有方式。UI 側は `camera/preview/attach` イベント（cameraId + size + fps）で要求し、メイン出力側が RenderTexture を作成、`camera/{cameraId}/preview/handle` state でハンドル（`texturePath` または GPU shared resource ID）を返却。UI 側は `RenderTexture` を参照して `VisualElement.style.backgroundImage` 相当に設定。
- **Rationale**: SL-1（引きカメラもメイン出力側所有）と整合。シーン所有境界を侵犯しない。
- **Trade-offs**: IPC 経路で RenderTexture ハンドルをやり取りする際のシリアライズ形式は Unity 独自（同プロセス共有のため GPU メモリ共有可、参照渡しで足りる）。Unity の `UnityEngine.RenderTexture` は同プロセス内でシングルトン Service Locator 経由で取得可能な設計とする。
- **Follow-up**: `output-renderer-shell` の拡張で `IOutputSceneRoots.GetPreviewTexture(cameraId)` 相当 API を要求することを Adjacent Expectation として Boundary Commitments に記載。本 spec では IPC 契約と UI 側の貼付け方法のみ定義。

### Decision: プリセット JSON ファイル保存、デバウンス 500 ms

- **Context**: CSW-14 / Requirement 11 の具体値確定。
- **Alternatives Considered**:
  1. JSON + Application.persistentDataPath + 500 ms デバウンス（CS-9 / SL-9 と同方針）
  2. ScriptableObject 保存（Editor 専用、Standalone で使えない）
  3. 即時保存（デバウンスなし、I/O 回数が多すぎ）
- **Selected Approach**: JSON（UTF-8, 整形あり、`camera-switcher-presets.json`）+ `Application.persistentDataPath` + 500 ms デバウンス。破損検出時は `*.bak.{unixMs}` にリネームして初回起動扱い。
- **Rationale**: CS / SL タブと統一することでオペレーター体験と保守容易性が揃う。500 ms は UX（保存待ち感が出ない）と I/O 低減の妥協点として SL-9 で採用された値を踏襲。
- **Trade-offs**: アプリ強制終了時に最大 500 ms 分の変更が失われる可能性。Requirement 11.4 で `OnApplicationQuit` 等での強制フラッシュを要件化して緩和。
- **Follow-up**: 実装時に `TimeProvider` port を用意し、テストで時刻を進める単体試験を用意（Requirement 15.8）。

## Risks & Mitigations

- **R-CSW-1: OSC デフォルトポート選定** — OSC の慣例 `9000` / `8000` は DAW や VRChat と衝突しがち。本 spec 既定は `57300` を提案（dynamic/private 範囲、47808 AVR/OSC 慣例とも被らない）。設定ファイルで上書き可能（Requirement 4.2）。UCAPI 標準ポートが将来公開された場合は既定値を切替可能な構造を維持。
- **R-CSW-4: Unity → UCAPI 単位差** — Rotation matrix 変換・座標系（左手/右手）・Physical Camera 有効化の要否を UCAPI4Unity API 確認時に棚卸し。Sanitize 層（NaN/Inf チェック、Requirement 3.5）で異常値を前段階で弾く。
- **R-CSW-11: メイン出力側 OSC 受信実装の所在** — 本 spec は契約のみ定義。`output-renderer-shell` の拡張か独立アダプタ層かは本 spec の関知外（Adjacent Expectation として記載）。
- **R-CSW-12: UCAPI C++ DLL 配布** — Native Plugin フォルダ同梱を既定とし、UCAPI4Unity UPM の指示に従う。Addressables 経由は非目標。
- **R-CSW-13: 差し替え前提 API の粒度** — 「cameraId + active-set」「OSC アドレスプレフィクス」「Local Volume 連動契約」を **最小契約** として Boundary Commitments に明示。将来の PVW/PGM / トランジションは後方互換拡張として設計余地を残す。
- **R-CSW-14: 編集対象カメラとアクティブカメラの UX 分離** — 「編集対象 ≠ アクティブ」を既定とし、ハイライト色の区別（`vsb-camera-card--editing` vs `vsb-camera-card--active`）で UI 表現。
- **R-CSW-15: OSC 時刻同期** — UCAPI の timecode は Flat Record 内に埋め込まれる。送信時刻（`UnityEngine.Time.timeAsDouble`）から UCAPI tc に変換する責務は Serializer adapter に閉じる。高度な補間・ジッタ吸収は本フェーズ非目標（Requirement 5.4 の「最新値優先」止まり）。
- **R-CSW-16-1: マルチプレビュー GPU 負荷** — 最大カメラ数 8 / サムネイル 192×108 / サムネイル 15 fps / アクティブ 640×360 / 60 fps を既定とし、設定ファイルで調整可能とする。実機計測は実装フェーズで。
- **R-CSW-9-2: プレビュー描画と OSC 送信の同期** — プレビュー描画は PanelSettings の Display 1 フレームに従い、OSC 送信は `LateUpdate` 末尾で対象カメラ最終 transform を送る。両者が齟齬を起こしても transform 値は整合する（Unity 1 フレームで確定）ため問題なし。

## References

- [UniversalCamerawork (UCAPI) — GitHub](https://github.com/Hidano-Dev/UniversalCamerawork) — Flat Record 128 byte + Header 10 byte + CRC16-CCITT + MessagePack 対応の共通フォーマット
- [UCAPI4Unity（未公開 UPM）](https://github.com/Hidano-Dev/UniversalCamerawork) — 本 spec では「UCAPI4Unity の公開 API」を adapter から利用する前提（具体 API は実装時に確認）
- [SceneViewStyleCameraController — GitHub](https://github.com/Hidano-Dev/SceneViewStyleCameraController) — Unity Editor の Scene ビュー相当の操作感を提供するコンポーネント
- [hecomi/uOSC — GitHub](https://github.com/hecomi/uOSC) — MIT、byte[] blob 対応、UPM 配布、送信バックグラウンド + コールバックメインスレッド
- [stella3d/OscCore — GitHub](https://github.com/stella3d/OscCore) — 代替候補（ゼロアロケーション重視、差し替え時に参照）
- [Iam1337/extOSC — GitHub](https://github.com/Iam1337/extOSC) — 代替候補（UI ドリブン、機能豊富）
- `D:\Personal\Repositries\VTuberSystemBase\.kiro\specs\core-ipc-foundation\design.md` — 上流 spec、D-3 / D-4 / D-5 / D-7 / D-8 / D-9 / D-10 / D-11 契約
- `D:\Personal\Repositries\VTuberSystemBase\.kiro\specs\ui-toolkit-shell\design.md` — 上流 spec、`IUiCommandClient` / `IUiSubscriptionClient` / `ITabLifecycleHandle` / `TabId.CameraSwitcher`
- `D:\Personal\Repositries\VTuberSystemBase\.kiro\specs\output-renderer-shell\design.md` — 上流 spec、`IOutputSceneRoots` / `IOutputCommandDispatcher` / `CamerasRoot` / `VolumeRoot`
- `D:\Personal\Repositries\VTuberSystemBase\.kiro\specs\camera-switcher-tab\requirements.md` — 本 spec の要件（15 要件、CSW-1〜CSW-16 決定）

Sources:
- [UniversalCamerawork (UCAPI) — GitHub](https://github.com/Hidano-Dev/UniversalCamerawork)
- [SceneViewStyleCameraController — GitHub](https://github.com/Hidano-Dev/SceneViewStyleCameraController)
- [hecomi/uOSC — GitHub](https://github.com/hecomi/uOSC)
- [stella3d/OscCore — GitHub](https://github.com/stella3d/OscCore)
- [Iam1337/extOSC — GitHub](https://github.com/Iam1337/extOSC)
