# Requirements Document

## Project Description (Input)
output-renderer-shell

VTuberSystemBase のメイン出力（Display 2+）の土台を提供する spec。Display 2 以降に全画面表示されるメイン出力シーン（ステージ + キャラクター配置の場）を構築し、IPC 経由のコマンドを受信してシーンに反映するディスパッチャを提供する。

スコープ:
- メイン出力シーンの初期構成（ルート GameObject 階層、デフォルトカメラ、デフォルトライト、URP 設定、空の Global Volume）
- Display 2+ への全画面表示切替（暫定実装。RuntimeDisplaySelector が入るまでは `Display.displays[n].Activate()` ベースで固定挙動）
- IPC 受信 → シーン反映のディスパッチャ（各タブ spec から呼ばれる Command 受け口を整備）
- メイン出力にはエラーダイアログ・デバッグログを一切描画しない描画分離
- スタンドアロン／Editor 両対応

非目標:
- RuntimeDisplaySelector との連携（spec #7 に分離）
- キャラクター・ステージ・カメラ個別機能（spec #4-6 に分離）
- UI Toolkit シェルそのもの（spec #3 に分離）

対応要件: docs/requirements.md の §3.1, §3.3, §6.2
上位計画: docs/spec-breakdown.md の spec #2（Wave 2、core-ipc-foundation に依存）

上流決定の継承（spec #1 core-ipc-foundation から）:
- D-1: 単一 Unity アプリ内で UI/メイン出力を論理分離、LocalHost ループバック通信
- D-4: メイン出力側が WebSocket サーバ、UI 側がクライアント
- D-3: 受信コールバックは常に Unity メインスレッドで配信
- D-9: PlayMode 開始〜停止の区間のみ常駐、Edit モードでは常駐しない
- D-10: state（coalesce 対象）と event（FIFO 必須）の区別を受け取り側で尊重する

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応
言語: 日本語で生成（CLAUDE.md の規約に従う）

## Open Questions and Decisions (Dig)

本セクションは dig インタビューで確定した設計上の決定事項を記録する（本 spec 固有）。上流 spec である `core-ipc-foundation` の D-1〜D-11 は暗黙に継承される。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| OR-1 | Display 2 不在時の起動挙動 | **Display 1 にフォールバック描画**して起動する。その際、警告ログを UI 側／コンソールに出力して誤配信を検出可能にする。 | テスト配信・リハーサル時の DX が良い。停止させると開発者が都度回避する必要がある。配信事故リスクは UI 側の警告表示と運用フローで軽減。 | 中（運用中に Display 2 切断事故が起きた場合の誤配信リスク） |
| OR-2 | 複数クライアント同時接続時の state 競合 | **Last-write-wins**。メイン出力は最後に到着した state コマンドを常に採用する。イベントは到着順に全て適用。 | 本フェーズは実質単一クライアント運用。将来 WebUI + LAN の多重運用が具体化するフェーズまで、キャラクタ単位の排他制御等は導入しない（YAGNI）。 | 低（本フェーズでの競合頻度は非常に低い） |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase における **メイン出力側シェル（Output Renderer Shell）** を定義する。Display 2 以降に全画面表示される「配信に載る映像」の土台であり、具体的には以下の 3 つの責務を持つ：

1. **シーン骨格の提供**：空のステージルート、デフォルトカメラ、デフォルトライト、URP 設定、空の Global Volume を備えた最小構成のメイン出力シーンを用意する。
2. **ディスプレイ振り分けの暫定実装**：Display 2+ への全画面表示切替を `Display.displays[n].Activate()` ベースで実現しつつ、将来の RuntimeDisplaySelector（spec #7）差し込みのための抽象化を確保する。
3. **IPC 受信 → シーン反映のディスパッチャ**：`core-ipc-foundation`（spec #1）の受信側エンドポイントとして振る舞い、UI 側（および将来の外部クライアント）から受け取ったコマンドを Unity メインスレッドで処理してシーンに反映する Command 受け口を提供する。

本 spec は **メイン出力側のシェル** に限定され、UI Toolkit シェル（spec #3）や各タブ機能（spec #4〜#6）、RDS 連携（spec #7）は本 spec の責務外である。本フェーズでは **「配信画面を安全に描き続ける器」** を確立することを最優先とし、機能ロジックは後続 spec に委ねる契約境界を明確化する。

## Boundary Context

- **In scope**:
  - メイン出力シーンの初期構成（ルート GameObject 階層、デフォルトカメラ、デフォルトライト、URP アセット参照、空の Global Volume）
  - Display 2+ への全画面表示切替の暫定実装（`Display.displays[n].Activate()` ベース）と将来 RDS 差し込みのための抽象インタフェース
  - `core-ipc-foundation` のサーバロール起動（D-4）およびコマンド受信 → シーン反映のディスパッチャ
  - 各タブ spec から呼び出される Command 受け口（コマンド登録／解除のハンドラ登録 API）
  - 受信コマンドの Unity メインスレッド実行の保証（D-3 の継承）
  - state 系コマンドの冪等性保持と event 系コマンドの逐次適用（D-10 の継承）
  - メイン出力サーフェスから UI・デバッグログ・エラーダイアログ等オペレーター向け描画を完全排除
  - スタンドアロンビルドと Unity Editor PlayMode の両対応（D-9 の継承）
  - UI クライアント未接続時のフェイルセーフ動作（メイン出力は単独で描画継続）
- **Out of scope**:
  - `RuntimeDisplaySelector` との実接続（spec #7 に分離、本 spec は抽象インタフェースのみ提供）
  - キャラクター・ステージ・カメラ・ライト・Global Volume の **個別機能ロジック**（spec #4〜#6 の責務。本 spec は受け口だけを用意する）
  - UI Toolkit シェル本体（spec #3）
  - IPC トランスポート／メッセージスキーマそのもの（spec #1 `core-ipc-foundation` の責務）
  - カメラ状態の OSC 伝送（spec #6 の責務）
  - シーン状態の永続化・保存・復元（各タブ spec および後続フェーズの責務）
- **Adjacent expectations**:
  - `core-ipc-foundation`（spec #1）の抽象インタフェースとサーバロール実装が利用可能であること
  - `ui-toolkit-shell`（spec #3）および各タブ spec（#4〜#6）は本 spec が公開する Command 受け口へ IPC 経由でコマンドを送る
  - `runtime-display-selector-integration`（spec #7）は本 spec のディスプレイ切替抽象を差し替え対象として利用する
  - 本 spec は他 spec の実装が不在でも単独でメイン出力シーンを起動・描画・停止できること

---

### Requirement 1: メイン出力シーンの初期構成

**Objective:** メイン出力側の開発者として、各タブ spec がコマンドを送る前から最小限のシーン骨格が存在する状態を担保したい。そうすれば後続 spec はルート構造に関する前提を共有でき、シーン起動直後でも安全に描画が成立する。

#### Acceptance Criteria

1. The Output Renderer Shell shall メイン出力シーン専用のルート GameObject 階層（ステージルート、キャラクタールート、ライトルート、カメラルート、Volume ルート等）を起動時に生成する。
2. The Output Renderer Shell shall URP 設定済みの **デフォルトカメラ** を 1 台配置し、ステージ全体が映る初期 Transform で起動する。
3. The Output Renderer Shell shall 真っ黒画面を回避するための **デフォルトライト**（Directional Light 1 基を想定）をライトルート配下に配置する。
4. The Output Renderer Shell shall プロジェクトに設定済みの URP Asset（Renderer Pipeline Asset）をメイン出力側で参照する状態で起動する。
5. The Output Renderer Shell shall **空の Global Volume** を Volume ルート配下に配置し、各タブ spec が Override を差し込める拡張点として公開する。
6. When メイン出力シーンが起動したとき、the Output Renderer Shell shall 上記のルート階層・デフォルトカメラ・デフォルトライト・Global Volume を **任意のコマンド受信前** にすべて準備完了にする。
7. The Output Renderer Shell shall ルート GameObject 階層の各ノードに、各タブ spec が参照するための安定した命名規約または参照取得 API（サービスロケータ相当）を提供する。
8. Where 後続 spec（#4〜#6）がキャラクター・ステージ・ライト・カメラ・Volume を追加生成する場合, the Output Renderer Shell shall 対応するルート配下への配置を受け入れる構造を維持する。

---

### Requirement 2: Display 2+ への全画面表示切替（暫定実装 + 差し替え可能な抽象）

**Objective:** 配信運用者として、メイン出力が確実に Display 2 以降の物理ディスプレイに全画面で出る状態を得たい。そうすれば OBS 等のキャプチャソフトから即座に配信ソースとして取り込める。加えて将来の開発者として、RDS リリース後に暫定実装を安全に差し替えたい。

**Note:** 本 spec の Display 切替は暫定実装である（docs/spec-breakdown.md §3 の spec #2 / §spec #7）。暫定実装には `Display.displays[n].Activate()` を用いるが、ディスパッチャおよび他の上位コードがこの暫定実装に直接依存しないよう、抽象インタフェースを経由する設計を必須とする（RDS 差し込み時にディスパッチャ側コードを書き換えないため）。

#### Acceptance Criteria

1. The Output Renderer Shell shall **ディスプレイ切替サービス**を抽象インタフェースとして定義し、暫定実装（`Display.displays[n].Activate()` ベース）を同インタフェースの 1 実装として提供する。
2. When メイン出力が起動したとき、the Output Renderer Shell shall ディスプレイ切替サービスを介して Display 2 以降の物理ディスプレイをアクティベートし、メイン出力カメラの出力先を当該ディスプレイに割り当てる。
3. The Output Renderer Shell shall メイン出力を割り当て先ディスプレイで **全画面表示**（ウィンドウ枠・タイトルバー非表示）として起動する。
4. If 起動時に Display 2 以降の物理ディスプレイが検出できなかった場合, the Output Renderer Shell shall **Display 1 にフォールバック描画**して起動を継続し、UI 側／コンソールに警告ログを出力する（see OR-1）。
4a. When Display 2 フォールバックが発生した状態で運用が継続される場合、the Output Renderer Shell shall UI 側で誤配信リスクを検出可能にするために、ディスプレイ割り当て状態を診断 API から取得可能な形で公開する（see OR-1, Requirement 9.8）。
5. The Output Renderer Shell shall ディスパッチャおよび各タブ spec 向けの Command 受け口から、ディスプレイ切替サービスの **具体実装クラスへの直接依存** を排除する。
6. Where `runtime-display-selector-integration`（spec #7）が将来組み込まれる場合, the Output Renderer Shell shall ディスプレイ切替サービスの実装を RDS ベースへ差し替えるだけで、ディスパッチャ・シーン初期化・Command 受け口のコードを変更せずに運用可能な構造を維持する。
7. The Output Renderer Shell shall 暫定実装においてアクティベート対象とするディスプレイインデックスを、外部から（設定ファイルまたはディスプレイ切替サービスの初期化引数を想定）変更可能な形で公開する。
8. The Output Renderer Shell shall スタンドアロンビルドと Unity Editor PlayMode のいずれにおいても、ディスプレイ切替サービスを同一の抽象インタフェース経由で起動する。

---

### Requirement 3: IPC 受信とコマンドディスパッチャ

**Objective:** 後続 spec（#4〜#6）の開発者として、IPC 経由で送られるコマンドを「メイン出力側のシーンに反映するための単一の入口」を得たい。そうすれば各タブは自分のコマンド定義とハンドラ登録だけに集中でき、受信・スレッド調整・振り分けの重複実装を避けられる。

**Note:** 本 spec のディスパッチャは `core-ipc-foundation`（spec #1）の抽象インタフェースに依存し、**メイン出力側が WebSocket サーバロール**（D-4 の継承）として動作する前提である。受信コールバックは **常に Unity メインスレッド** で呼び出される（D-3 の継承）。

#### Acceptance Criteria

1. The Output Renderer Shell shall `core-ipc-foundation` の抽象インタフェースを利用して、メイン出力側をサーバロールとして起動する（see D-4）。
2. The Output Renderer Shell shall 受信したコマンドを **コマンド種別（topic / kind）** 別のハンドラへ振り分ける **ディスパッチャ** を提供する。
3. The Output Renderer Shell shall 各タブ spec が自身のコマンドハンドラをディスパッチャへ **登録／解除** するための公開 API を提供する。
4. When コマンドがトランスポートから受信されたとき、the Output Renderer Shell shall 登録済みハンドラの呼び出しを **Unity メインスレッド上** で実施する（see D-3）。
5. When 受信したコマンドに対応するハンドラが登録されていなかったとき、the Output Renderer Shell shall 当該コマンドを破棄し、診断ログへ記録する。
6. If ハンドラ実行中に例外が送出された場合, the Output Renderer Shell shall 例外を捕捉して診断ログへ記録したうえで、メイン出力の描画ループおよびディスパッチャ自体を停止させない。
7. The Output Renderer Shell shall ディスパッチャを本 spec のアセンブリ定義（asmdef）内に隔離し、各タブ spec からは公開 API 経由のみでハンドラ登録を受け付ける。
8. The Output Renderer Shell shall `core-ipc-foundation` の Request/Response プリミティブに対応する応答型コマンドのハンドラ登録についても、同一ディスパッチャ上で登録可能にする。

---

### Requirement 4: State / Event コマンドの配信規律

**Objective:** 後続 spec の開発者として、state 系コマンド（スライダー値等）が連続して来ても冪等に反映され、event 系コマンド（Light 生成・カメラ切替等）は漏れずに順序通り適用される保証を得たい。そうすれば各タブ spec の実装者は配信セマンティクスに関する前提を共有でき、個別に流量制御や順序管理を書く必要がなくなる。

**Note:** `core-ipc-foundation` は送信側で `PublishState` / `PublishEvent` を分離しており（D-10）、受信側では state を同一トピックで coalesce、event を FIFO で保持する（D-7）。本 spec のディスパッチャは受信側としてこの契約を尊重する。

#### Acceptance Criteria

1. The Output Renderer Shell shall 受信エンベロープの `kind` フィールドを参照し、`state` / `event` / `request` / `response` の種別に応じて異なるハンドラ呼び出し規律を適用する（see D-10）。
2. When `kind = state` のコマンドが連続して到着したとき、the Output Renderer Shell shall 同一トピックの先行値を最新値で上書きする（coalesce）振る舞いを許容し、ハンドラ実装が冪等であることを前提として最新値のみ反映する（see D-7）。
3. When `kind = event` のコマンドが到着したとき、the Output Renderer Shell shall 受信順（FIFO）でハンドラを呼び出し、いかなる場合も取りこぼしを発生させない（see D-7）。
4. The Output Renderer Shell shall 各タブ spec が登録する state ハンドラに対して、ハンドラ実装が **冪等**（同一入力で同一結果となる）であることをドキュメントで明示的に要求する契約を定義する。
5. The Output Renderer Shell shall state ハンドラと event ハンドラを **異なる登録 API** として公開し、登録時点で種別を明示的に選択させる。
6. If 登録された種別とエンベロープの `kind` が不整合であった場合, the Output Renderer Shell shall 当該コマンドを破棄し、診断ログへ記録する。
7. The Output Renderer Shell shall `request` / `response` 系コマンドは coalesce の対象外とし、相関 ID を伴う 1 対 1 の応答として扱う（see D-10）。
8. When 複数クライアント（組み込み UI / 将来の LAN UI / WebUI 等）が同一トピックに対して state コマンドを送信したとき、the Output Renderer Shell shall **Last-write-wins** で最後に到着した値を採用する（see OR-2）。
9. When 複数クライアントから event コマンドが到着したとき、the Output Renderer Shell shall 到着順（FIFO）で全てのイベントを適用し、クライアント単位のロック等は行わない（see OR-2）。

---

### Requirement 5: メイン出力サーフェスからの UI・診断出力の完全排除

**Objective:** 配信運用者として、メイン出力が OBS 等で取り込まれそのまま配信に載るため、オペレーター向けの UI・ログ・エラーダイアログが一切映り込まない状態を担保したい。そうすれば配信事故を構造的に回避できる。

**Note:** docs/requirements.md §2.3 および §6.2 により、メイン出力ディスプレイには UI・デバッグログ・エラーダイアログを一切描画しない。重大エラー発生時も、メイン出力側は現状描画を維持し、UI 側（Display 1）にのみ通知する。

#### Acceptance Criteria

1. The Output Renderer Shell shall メイン出力カメラが描画するレイヤー／カリングマスクを、オペレーター UI レイヤーを含まない構成で初期化する。
2. The Output Renderer Shell shall メイン出力に対してオンスクリーン GUI（`OnGUI` / `IMGUI` / UI Toolkit のランタイム PanelSettings 等）を **一切アタッチしない**。
3. If 本 spec またはディスパッチャが例外・警告・診断メッセージを出力する場合, the Output Renderer Shell shall メイン出力サーフェス（Display 2+）に当該メッセージを描画せず、Unity コンソールまたは UI 側（Display 1）への通知経路のみを用いる。
4. If Unity 既定のエラーダイアログ・クラッシュダイアログ・Development Build のオーバーレイがメイン出力側で表示される可能性がある場合, the Output Renderer Shell shall 当該オーバーレイをメイン出力ディスプレイ上で抑止するか、または別ディスプレイへ回避する構成を採用する（具体手段は設計フェーズで確定）。
5. If ディスパッチャまたはシーン初期化で重大エラーが発生した場合, the Output Renderer Shell shall メイン出力の描画継続を阻害せず、直前の描画状態を維持したうえで、UI 側に診断情報を通知する。
6. The Output Renderer Shell shall 各タブ spec から登録されるハンドラに対して、メイン出力サーフェスに GUI・テキスト・デバッグオーバーレイを描画することを **禁止する契約** をドキュメントで明示的に要求する。
7. Where 開発者がメイン出力の描画状態を目視確認したい場合, the Output Renderer Shell shall 診断表示を **UI 側（Display 1）またはコンソール** でのみ提供し、メイン出力サーフェスは汚染しない。

---

### Requirement 6: スタンドアロンビルドと Unity Editor PlayMode の両対応

**Objective:** システム運用者および開発者として、ビルド後のスタンドアロン実行時と Unity Editor PlayMode の両方で、同一のメイン出力挙動を得たい。そうすれば開発中の検証と本番運用の挙動差を最小化でき、配信前リハーサルが Editor PlayMode で完結する。

**Note:** D-9 の継承により、Editor では PlayMode 開始〜停止の区間のみ常駐し、Edit モードでは常駐しない。ドメインリロードに跨る状態維持は試みない。

#### Acceptance Criteria

1. When Unity アプリケーションがスタンドアロンビルドとして起動したとき、the Output Renderer Shell shall シーン初期構成・ディスプレイ切替・ディスパッチャ起動を自動的に実施する。
2. When Unity Editor が PlayMode に入ったとき、the Output Renderer Shell shall スタンドアロン時と同一手順でシーン初期構成・ディスプレイ切替・ディスパッチャ起動を自動的に実施する（see D-9）。
3. When Unity Editor が PlayMode を終了したとき、the Output Renderer Shell shall ディスパッチャ・ディスプレイ切替サービス・シーン生成リソースを完全に解放し、Edit モードに残留物を残さない（see D-9）。
4. While PlayMode の開始と停止が繰り返される間, the Output Renderer Shell shall リソースリークやディスプレイ割り当ての残留なしに毎回クリーンに再初期化する。
5. The Output Renderer Shell shall Unity Editor の **Edit モード** ではディスパッチャ・ディスプレイ切替サービスを起動しない（see D-9）。
6. The Output Renderer Shell shall ドメインリロードに跨る状態維持を試みず、PlayMode 開始のたびに新しいライフサイクルで初期化する（see D-9）。
7. The Output Renderer Shell shall スタンドアロン時と Editor PlayMode 時で、後続 spec から見た Command 受け口および描画挙動を同一に保つ。
8. Where Editor PlayMode でディスプレイ切替の挙動が OS 制約により異なる場合, the Output Renderer Shell shall Editor 固有の挙動差を設計フェーズで明文化し、暫定実装の範囲内で許容される差異としてドキュメントに記載する。

---

### Requirement 7: UI クライアント未接続時のフェイルセーフ

**Objective:** 配信運用者として、UI クライアント（Display 1 側）がまだ起動していない／接続が切れている状況でも、メイン出力が単独で描画を継続している状態を担保したい。そうすれば UI 側の障害・起動順序レース・運用ミスがそのまま配信事故に直結しない。

**Note:** `core-ipc-foundation` の Requirement 5 は接続断時にメイン出力の描画継続を阻害しない契約を規定している（spec #1 Requirement 5 Acceptance Criteria 7）。本 spec はこの契約を上位層で具現化する責務を負う。

#### Acceptance Criteria

1. When メイン出力起動時に UI クライアントが未接続であったとき、the Output Renderer Shell shall シーン初期構成とディスプレイ切替を正常に完了し、デフォルトカメラで初期シーンの描画を継続する。
2. While UI クライアントが未接続の状態が継続している間, the Output Renderer Shell shall メイン出力の描画ループを中断・停止させず、デフォルト状態での描画を維持する。
3. When UI クライアントが後から接続したとき、the Output Renderer Shell shall ディスパッチャ経由で到着するコマンドを通常通り受信・反映開始する。
4. When UI クライアントとの接続が一時的に切断されたとき、the Output Renderer Shell shall 現在のシーン状態（直前に適用されたコマンドの結果）を保持したまま描画を継続する。
5. If `core-ipc-foundation` から接続断・再接続イベントが通知された場合, the Output Renderer Shell shall 当該イベントを診断ログへ記録するのみに留め、メイン出力サーフェスへの視覚的通知を行わない（see Requirement 5）。
6. The Output Renderer Shell shall UI 未接続を理由としたクラッシュ・例外伝搬・描画停止を発生させない。
7. Where 将来の外部クライアント（LAN タブレット UI / WebUI）が同一サーバに接続する場合, the Output Renderer Shell shall 組み込み UI クライアントの接続有無と独立に、外部クライアントからのコマンドを受け付ける構造を維持する（see D-4）。

---

### Requirement 8: 本 spec 単体での検証可能性

**Objective:** spec オーナーとして、本シェルを後続 spec（#3〜#6）の実装を待たずに検証したい。そうすれば Wave 2 の完了を Wave 3 の完了に引きずられずに判定でき、後続 spec が本シェルに依存する前に品質を担保できる。

#### Acceptance Criteria

1. The Output Renderer Shell shall 後続 spec（ui-toolkit-shell / 各タブ spec）の実装が不在でも、単独でシーン初期構成・ディスプレイ切替・ディスパッチャ起動を完遂できる構造とする。
2. The Output Renderer Shell shall `core-ipc-foundation` の自己ループ機構（spec #1 Requirement 8）を活用して、ダミーコマンドを自プロセス内で送受信し、ディスパッチャが正しくハンドラを呼び出すことを検証する手段を備える。
3. The Output Renderer Shell shall Unity Editor PlayMode での手動検証手順（最小サンプルシーンまたは同等物）を提供する。
4. When 本シェル単体のテスト実行が行われたとき、the Output Renderer Shell shall シーン初期構成・ディスプレイ切替サービスの抽象経由呼び出し・ディスパッチャの振り分け・UI 未接続時のフェイルセーフ挙動を検証するテストケースを提供する。
5. The Output Renderer Shell shall 暫定実装のディスプレイ切替サービスに対し、テスト時に差し替え可能な **モック実装** を受け入れる構造を備える（物理ディスプレイに依存しないテストを可能にする）。

---

### Requirement 9: 観測性・診断可能性

**Objective:** システム開発者・運用者として、メイン出力側で発生する不具合が「シーン初期化起因」「ディスプレイ切替起因」「ディスパッチャ／ハンドラ起因」のいずれかを即座に切り分けたい。そうすれば後続 spec のハンドラ実装でのバグと本シェル自体のバグを混同せずに済む。

**Note:** 本要件の診断出力は Requirement 5 の「メイン出力サーフェス描画禁止」契約に従い、UI 側（Display 1）またはコンソールへのみ流す。

#### Acceptance Criteria

1. The Output Renderer Shell shall シーン初期構成の各段階（ルート生成・カメラ配置・ライト配置・URP 参照・Global Volume 配置）の開始・完了・失敗をログ出力する。
2. The Output Renderer Shell shall ディスプレイ切替サービスの起動・アクティベート対象ディスプレイ番号・割り当て結果・失敗事由をログ出力する。
3. The Output Renderer Shell shall ディスパッチャへのハンドラ登録・解除、および受信コマンドの種別（topic / kind）ごとの振り分け結果を、ログレベルに応じて出力する。
4. When ディスパッチャがハンドラ未登録のコマンドを受信したとき、the Output Renderer Shell shall 当該コマンドの topic と kind を含む診断情報をログ出力する。
5. When ハンドラ実行中に例外が発生したとき、the Output Renderer Shell shall 例外内容・topic・相関 ID（存在する場合）を含む診断情報をログ出力する（see Requirement 3）。
6. The Output Renderer Shell shall 診断ログをメイン出力サーフェスに描画せず、UI 側（Display 1）またはコンソールへのみ流す（see Requirement 5）。
7. Where 開発者がデバッグ用途で詳細ログを必要とする場合, the Output Renderer Shell shall ログレベルを外部から切替可能にする。
8. The Output Renderer Shell shall 診断に必要な最小限の状態（例: 現在のシーン初期化フェーズ、ディスプレイ割り当て状態、登録済みハンドラ数）を外部から取得可能な形で公開する。

---

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案適用、要件レベル厳選）
- **質問数**: 2 問 / 決定数: 2 件（OR-1, OR-2）
- **継承**: core-ipc-foundation の D-1, D-3, D-4, D-9, D-10 を上流決定として継承
- **主要な発見**:
  - Display 2 不在時の挙動は「停止」ではなく「Display 1 フォールバック＋警告」が採択された。運用上のテスト配信・開発 DX を優先し、誤配信リスクは UI 警告と診断 API で補う方針。
  - 複数クライアント競合は last-write-wins に単純化。本フェーズでは競合頻度が非常に低いため、クライアント単位排他制御は YAGNI で導入しない。
- **残留リスク**:
  - R-1: Display 2 フォールバック中の UI 側警告表示デザイン（具体 UX は spec #3 ui-toolkit-shell で設計）
  - R-2: 将来の複数クライアント運用が具体化したときの競合解決方針（キャラクタ単位ロック／排他フラグ等）は別 spec で検討
