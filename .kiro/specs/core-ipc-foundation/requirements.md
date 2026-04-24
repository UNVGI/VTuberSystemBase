# Requirements Document

## Project Description (Input)
core-ipc-foundation

VTuberSystemBase の UI プロセス（Display 1）とメイン出力プロセス（Display 2+）を LocalHost 経由で疎通させる共通基盤。将来の LAN タブレット UI・ブラウザ WebUI への差し替えを見据え、トランスポートとメッセージ層を抽象化する。

スコープ:
- LocalHost 通信の抽象インタフェース定義（送受信・購読・Request/Response）
- 具体トランスポートの実装1本（TCP / WebSocket のいずれかを設計フェーズで確定）
- メッセージスキーマとシリアライゼーション（JSON / MessagePack のいずれかを確定）
- ビルド時・Editor PlayMode 時の両方で動作する接続マネージャ
- 接続断・再接続の基本ハンドリング

非目標:
- LAN／WebUI からの接続（インタフェースだけ用意し実装は将来フェーズ）
- 認証／暗号化

対応要件: docs/requirements.md の §3.2, §3.3, §6.3
上位計画: docs/spec-breakdown.md の spec #1（Wave 1、他の全 spec が本 spec に依存）

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応
言語: 日本語で生成（CLAUDE.md の規約に従う）

## Open Questions and Decisions (Dig)

本セクションは dig インタビューで確定した設計上の決定事項を記録する。各決定は下段の EARS Acceptance Criteria から参照される。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| D-1 | プロセス topology | **単一 Unity アプリ内で UI と メイン出力を論理分離し、LocalHost ループバックで IPC する**。外部クライアント（LAN タブレット UI / WebUI）は将来同じソケットに外部から接続する拡張で受け入れる。 | 起動・RDS 連携がシンプル。Wave 1 のスコープを最小化できる。抽象化の目的（外部 UI 差し替え）はインタフェース維持で達成可能。 | 低 |
| D-2 | 具体トランスポート | **WebSocket (RFC 6455) で確定**。設計フェーズでの再選定は行わない。 | 将来の WebUI クライアントがブラウザ由来で WebSocket 一択。最初から WS に統一することで二重実装を回避。LocalHost でも実用上のレイテンシ劣化は無視できる。 | 低 |
| D-3 | 受信コールバックのスレッド契約 | **常に Unity メインスレッドで呼び出す**。I/O はワーカースレッドで行い、PlayerLoop / SynchronizationContext 経由でディスパッチする。 | 本基盤上に乗る想定メッセージ頻度は低（〜100 Hz 以下）。各 spec 実装者が Unity API を自由に呼べる単純な契約にすることで、各 spec におけるスレッドマーシャリングの重複実装とバグ作り込みを防ぐ。最大 1 フレーム（〜16ms）の遅延は配信要件上許容。 | 低 |
| D-4 | サーバ／クライアントのロール | **メイン出力側が WebSocket サーバ、UI 側はクライアント**。将来の LAN タブレット UI・ブラウザ WebUI・他社製 UI は、すべて同じサーバに外部から接続するクライアントとして統一的に扱う。 | メイン出力を「権威ある状態所有者」と位置づけ、UI は表示・入力層に徹する関心分離。トポロジー変更なしで将来の外部 UI を受け入れられる。 | 低 |
| D-5 | シリアライゼーション | **JSON / WebSocket テキストフレーム**で確定。 | 本基盤の想定メッセージ頻度は低く、性能差は実用上問題にならない。人間可読性はデバッグ・トラブルシュートの生産性に直結。将来 WebUI（ブラウザ）側でも JSON が自然。必要になった場合は D-2 同様に別トランスポートとして MessagePack を追加可能。 | 低 |
| D-6 | ポート指定方針 | **設定ファイル（デフォルト値付き）で構成可能**。未指定時はデフォルト（値は設計フェーズで確定）を使用。 | スタンドアロン多重起動が必要な場合にポート変更で回避可能。将来 WebUI 側でも設定から URL を決められる。自動空きポート方式は WebUI 側の URL 固定運用と相性が悪いため却下。 | 低 |
| D-7 | 受信キュー溢れ時の方針 | **同一トピックは最新値で coalesce、異種トピック／イベント型は FIFO 保持**。 | UI スライダー等の高頻度操作で中間値が飛んでも最終値に追従できれば視覚的には問題ない。一方、「Light 生成」「カメラ切替」等のイベント型はロスト不可のため coalesce 対象外。キュー上限超過時のメモリ暴走を防止。 | 中（トピック分類設計が必要） |
| D-8 | Request/Response のデフォルトタイムアウト | **5 秒デフォルト、リクエスト単位で上書き可能**。 | LocalHost の応答は通常数 ms 以内。5 秒以上無応答は実装上の問題の可能性が高い。重い処理（アセット読込）は呼び出し側が意図してタイムアウトを伸ばせる。 | 低 |
| D-9 | Editor ドメインリロード時の挙動 | **PlayMode 開始時に起動、PlayMode 停止時に完全シャットダウン。Edit モードでは常駐しない**。ドメインリロードに跨る状態維持は試みない。 | シンプルで予測しやすい。再起動が常に「確実に新しい状態」で始まるため、PlayMode 繰り返しによる隠れた状態バグを回避。Editor 独自の DomainUnload 対応コードが不要。 | 低 |
| D-10 | state / event の宣言方法 | **送信 API を 2 系統に分ける**：`PublishState(topic, payload)`（同一トピックで coalesce 対象）と `PublishEvent(topic, payload)`（FIFO 必須）。Request/Response はさらに別 API として独立させる。 | 送信側コードを読むだけで意図が明示される。受信側キュー実装は envelope の kind フィールドだけを見れば coalesce 可否を判断できる。ドキュメント・レビューの生産性にも寄与。 | 低 |
| D-11 | メッセージサイズ上限 | **1 メッセージあたり 1 MB**を上限とし、超過は送信時に即時エラー、受信時はフレーム破棄＋ログ出力。 | UI コマンドは通常 KB オーダーなので実用上影響なし。偶発的な巨大ペイロード（シリアライズ事故・無限ループ）からメモリを保護。将来 LAN 経由時の回線占有対策にもなる。 | 低 |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase における **UI プロセス（Display 1）** と **メイン出力プロセス（Display 2+）** を LocalHost 経由で疎結合に連携させるための共通基盤「Core IPC Foundation」を定義する。

本基盤は、トランスポート層（TCP / WebSocket 等）とメッセージ層（JSON / MessagePack 等）を抽象化することで、将来的な LAN タブレット UI・ブラウザベース WebUI・別 PC UI などへの差し替えを可能にする。本フェーズではその抽象インタフェースの確立と具体実装 1 本の提供、そしてスタンドアロン／Editor PlayMode 両対応の接続マネージャの実現を目的とする。

本 spec は他の全 spec（output-renderer-shell, ui-toolkit-shell, 各タブ spec）の基盤となるため、**コントラクトの安定性** と **最小限の責務** を最優先する。

## Boundary Context

- **In scope**:
  - LocalHost 通信の抽象インタフェース（送信・受信・購読 Pub/Sub・Request/Response）
  - 具体トランスポート実装 1 本（TCP または WebSocket、設計フェーズで確定）
  - メッセージスキーマとシリアライゼーション（JSON または MessagePack、設計フェーズで確定）
  - スタンドアロンビルド／Unity Editor PlayMode の両方で動作する接続マネージャ
  - 接続断の検出と自動再接続の基本ハンドリング
  - 将来の LAN／WebUI 接続を見据えた拡張可能なインタフェース設計（インタフェースのみ）
- **Out of scope**:
  - LAN 上のタブレット端末や WebUI クライアントからの実接続実装
  - 認証・認可・暗号化（TLS 等）
  - カメラ状態伝送そのもの（OSC を用いるため本 spec の管轄外、ただし将来 OSC 以外の経路に載せる場合の抽象化は検討できる形を残す）
  - UI シェル・メイン出力シェル・各タブの機能ロジック（他 spec の責務）
  - メッセージの永続化・リプレイ・タイムライン録画
- **Adjacent expectations**:
  - `output-renderer-shell`（spec #2）は本基盤の受信側エンドポイントを利用してコマンドを受け取る
  - `ui-toolkit-shell`（spec #3）および各タブ spec（#4〜#6）は本基盤の送信側エンドポイントを利用してコマンドを発行する
  - 本 spec 単体で通信動作が検証可能であること（エコーサーバや自己ループでテスト可能）
  - 他 spec の作業を止めないよう、Wave 1 終了時点でインタフェースと最小動作が確定していること

---

### Requirement 1: LocalHost 通信の抽象インタフェース定義
**Objective:** 基盤利用者（上位 spec の開発者）として、具体トランスポートに依存しない統一 API を用いたい。そうすれば将来のトランスポート差し替え時に、上位コードを書き換えずに済む。

#### Acceptance Criteria
1. The Core IPC Foundation shall **PublishState / PublishEvent / Subscribe / Request / Response** の通信プリミティブを備えた抽象インタフェースを提供する（see D-10）。
2. The Core IPC Foundation shall 抽象インタフェース層から具体トランスポート実装（WebSocket 等）への直接的な型依存を排除する。
3. When 上位 spec が抽象インタフェース経由でメッセージを送信したとき、the Core IPC Foundation shall 具体トランスポート実装の種別を上位から意識させずに送信を完遂する。
4. When 上位 spec が特定のトピックまたはチャネルを購読したとき、the Core IPC Foundation shall 対応するメッセージの受信を購読コールバックへ**Unity メインスレッドで**配信する（see D-3）。
5. When 上位 spec が Request を発行したとき、the Core IPC Foundation shall 対応する Response を一意に対応付けて**Unity メインスレッドで**返却する、もしくは**デフォルト 5 秒（リクエスト単位で上書き可能）**のタイムアウトを通知する（see D-3, D-8）。
6. The Core IPC Foundation shall 抽象インタフェースを独立したアセンブリ定義（asmdef）として提供し、具体実装アセンブリに依存しない参照方向を維持する。
7. The Core IPC Foundation shall I/O 受信処理はワーカースレッドで実施しつつ、上位 spec へのコールバック／Response 返却は常に Unity メインスレッド上で行うディスパッチ機構を内包する（see D-3）。

---

### Requirement 2: 具体トランスポート実装の提供
**Objective:** システム運用者として、本フェーズで動作する具体トランスポートを 1 本持ちたい。そうすれば UI ↔ メイン出力の疎通が実際に成立する。

#### Acceptance Criteria
1. The Core IPC Foundation shall **WebSocket (RFC 6455)** を具体トランスポート実装として提供する（see D-2）。
2. The Core IPC Foundation shall 具体トランスポート実装を抽象インタフェースの実装クラスとして提供し、上位 spec がインタフェース経由でのみ利用可能な構造とする。
3. When メイン出力側エンドポイントが起動されたとき、the Core IPC Foundation shall WebSocket サーバとして所定のポートで受信待受を開始する（see D-4）。
4. When UI 側エンドポイントが起動されたとき、the Core IPC Foundation shall WebSocket クライアントとしてメイン出力側サーバへ接続要求を発行する（see D-4）。
5. While 接続が確立されている間, the Core IPC Foundation shall 双方向のメッセージ送受信を成立させる。
6. If 使用予定のポートが既に占有されていた場合、the Core IPC Foundation shall 起動失敗を明示的なエラーとして通知し、メイン出力の描画を阻害しない形で上位へ伝搬する。
7. The Core IPC Foundation shall 接続先ホスト・ポート等の基本パラメータを**設定ファイルから読み込み、未指定時はデフォルト値にフォールバックする**形で公開する（see D-6）。
8. The Core IPC Foundation shall 同一サーバに対して複数のクライアント（本フェーズでは組み込み UI、将来的には LAN/WebUI クライアント）が同時接続可能な構造を備える（see D-4）。

---

### Requirement 3: メッセージスキーマとシリアライゼーション
**Objective:** 基盤利用者として、構造化されたメッセージを扱う共通の型定義とシリアライズ方式が欲しい。そうすれば各 spec のコマンド／イベント定義を同じ土台に載せられる。

**Note:** WebSocket テキストフレーム上で JSON を伝送する（see D-2, D-5）。

#### Acceptance Criteria
1. The Core IPC Foundation shall メッセージエンベロープ（種別、識別子、相関 ID、ペイロード等を含む共通包装構造）を定義する。
2. The Core IPC Foundation shall **JSON** をシリアライゼーション方式として、**WebSocket テキストフレーム**で伝送する（see D-5）。
3. When 上位 spec がメッセージを送信したとき、the Core IPC Foundation shall エンベロープを JSON シリアライズし、WebSocket テキストフレームとして送信する。
4. When トランスポートからテキストフレームを受信したとき、the Core IPC Foundation shall JSON デシリアライズしてエンベロープを復元し、購読者へ配信する。
5. If 受信したテキストが不正な JSON またはスキーマ不一致であった場合、the Core IPC Foundation shall エラーをログ出力し、当該メッセージを破棄して後続処理を継続する。
6. The Core IPC Foundation shall Request と Response を相関させるための相関 ID をエンベロープに含める。
7. Where 将来のスキーマ進化が必要となる場合, the Core IPC Foundation shall 未知フィールドを受信しても処理を継続できる後方互換性方針を採用する。
8. The Core IPC Foundation shall エンベロープに **`kind` フィールド**（`state` / `event` / `request` / `response` のいずれか）を含め、受信側での coalesce 可否判断に用いる（see D-7, D-10）。
9. If 送信メッセージのシリアライズ後サイズが **1 MB** を超える場合、the Core IPC Foundation shall 送信を拒否し、呼び出し側にエラーを返す（see D-11）。
10. If 受信メッセージのサイズが **1 MB** を超える場合、the Core IPC Foundation shall 当該フレームを破棄してエラーをログ出力し、後続処理を継続する（see D-11）。

---

### Requirement 4: 接続マネージャ（スタンドアロン／Editor 両対応）
**Objective:** システム運用者として、ビルド後のスタンドアロン実行時と Unity Editor PlayMode の両方で、同じ通信挙動を得たい。そうすれば開発体験と本番挙動の差異を最小化できる。

**Note:** D-1 により本基盤は単一 Unity アプリ内でサーバ（メイン出力側）とクライアント（UI 側）の両ロールを同居させる。D-9 により Editor では PlayMode 開始〜停止の区間のみ常駐し、Edit モードでは一切常駐しない。

#### Acceptance Criteria
1. The Core IPC Foundation shall 接続ライフサイクル（起動・停止・再起動）を一元管理する接続マネージャを提供する。
2. When Unity アプリケーションがスタンドアロンビルドとして起動したとき、the Core IPC Foundation shall 接続マネージャを自動初期化し通信を可能な状態にする。
3. When Unity Editor が PlayMode に入ったとき、the Core IPC Foundation shall 接続マネージャを自動初期化し通信を可能な状態にする（see D-9）。
4. When Unity Editor が PlayMode を終了したとき、the Core IPC Foundation shall 接続マネージャを完全にシャットダウンし、ソケット・スレッド・メモリ等のリソースを解放する（see D-9）。
5. When Unity アプリケーションが終了要求を受けたとき、the Core IPC Foundation shall 接続マネージャを安全にシャットダウンし、未送信メッセージの扱いを定義済みの方針に従って処理する。
6. While PlayMode の開始と停止が繰り返される間, the Core IPC Foundation shall ポート占有やリソースリークを発生させずに毎回クリーンに再初期化する。
7. The Core IPC Foundation shall スタンドアロン時と Editor PlayMode 時で、上位 spec から見た API 挙動を同一に保つ。
8. The Core IPC Foundation shall Unity Editor の **Edit モード** では接続マネージャを起動しない（see D-9）。
9. The Core IPC Foundation shall Editor の **ドメインリロード** をまたいだ状態維持を試みず、PlayMode 開始のたびに新しいライフサイクルで初期化する（see D-9）。

---

### Requirement 5: 接続断・再接続ハンドリング
**Objective:** システム運用者として、一時的な接続断に起因する運用中断を避けたい。そうすれば配信中にプロセス間の通信が切れても自動復帰できる。

**Note:** D-1 により Wave 1 の接続は単一プロセス内ループバックのため、通常運用で接続断は発生しにくい。本要件は主に以下の場面をカバーする：
(a) クライアント（UI 側）起動時にサーバ（メイン出力側）がまだ待受前で生じる**起動レース**、
(b) 将来の外部クライアント（LAN/WebUI）がネットワーク事情で切断される場面、
(c) 例外的なソケット強制切断。

#### Acceptance Criteria
1. When トランスポート層で接続断が検出されたとき、the Core IPC Foundation shall 接続断イベントを上位 spec へ通知する。
2. When クライアント側が接続失敗または接続断を検出したとき、the Core IPC Foundation shall 所定のバックオフ戦略に従って再接続を試行する。
3. When 再接続が成功したとき、the Core IPC Foundation shall 接続回復イベントを上位 spec へ通知する。
4. While 接続断状態が継続している間, the Core IPC Foundation shall 購読および Request/Response の API 呼び出しに対し、失敗または保留を定義済みの方針で通知する（上位がクラッシュしない契約を維持する）。
5. If 再接続試行が所定の上限回数または上限時間を超えたとき、the Core IPC Foundation shall 再接続を停止し恒常的接続不能状態として上位へ通知する。
6. The Core IPC Foundation shall 接続状態（接続中 / 切断 / 再接続試行中 / 恒常的切断）を上位 spec が参照できる形で公開する。
7. If メイン出力側で接続断が検出された場合、the Core IPC Foundation shall メイン出力の描画継続を阻害する動作（例外の表面化やダイアログ表示等）を行わない。
8. When PlayMode 停止またはアプリ終了が契機で発生する切断であったとき、the Core IPC Foundation shall 再接続を試行せず、通常シャットダウンとして扱う（see D-9）。

---

### Requirement 6: 将来拡張に向けたインタフェースの前方互換性
**Objective:** 将来の開発者として、LAN タブレット UI や WebUI を本基盤に後付けで接続できるよう、インタフェースを事前に整えておきたい。そうすれば本フェーズ以降の拡張コストを抑えられる。

#### Acceptance Criteria
1. The Core IPC Foundation shall 抽象インタフェースに、LocalHost 以外のホスト・ポート設定を受け入れ可能な拡張点を内包する。
2. The Core IPC Foundation shall 複数の具体トランスポート実装を同居可能とする構造（インタフェース単一・実装複数）を採用する。
3. Where 将来 WebSocket ベースの WebUI クライアントを受け入れる拡張が行われる場合, the Core IPC Foundation shall 既存上位 spec の送受信コードを変更せずにトランスポートを追加できる。
4. Where 将来 LAN タブレット UI 等の外部クライアントを受け入れる拡張が行われる場合, the Core IPC Foundation shall 本フェーズで定義したメッセージエンベロープをそのまま共有できるスキーマ設計を維持する。
5. The Core IPC Foundation shall 本フェーズでは LAN／WebUI クライアントの実接続実装を行わない（インタフェース準備のみ）。
6. The Core IPC Foundation shall 認証・暗号化を本フェーズでは実装せず、将来拡張余地を残す形でインタフェース責務を汚染しない。

---

### Requirement 7: 観測性・診断可能性
**Objective:** システム開発者・運用者として、通信問題の切り分けを容易に行いたい。そうすれば上位 spec で発生する不具合が通信起因か機能起因かを迅速に判別できる。

#### Acceptance Criteria
1. The Core IPC Foundation shall 接続開始・確立・切断・再接続の各イベントをログに出力する。
2. When メッセージ送信または受信でエラーが発生したとき、the Core IPC Foundation shall エラー内容・メッセージ種別・相関 ID（存在する場合）を含む診断情報をログ出力する。
3. The Core IPC Foundation shall ログ出力をメイン出力（Display 2+）に描画させず、UI 側（Display 1）またはコンソールへのみ流す運用が可能な形で提供する。
4. Where 開発者がデバッグ用途で詳細ログを必要とする場合, the Core IPC Foundation shall ログレベルを外部から切替可能にする。
5. The Core IPC Foundation shall 診断に必要な最小限の統計（例: 現在の接続状態、再接続試行回数等）を外部から取得可能な形で公開する。

---

### Requirement 8: 基盤単体での検証可能性
**Objective:** spec オーナーとして、本基盤を他 spec から独立して検証したい。そうすれば Wave 2 以降の spec が本基盤に依存する前に品質を担保できる。

#### Acceptance Criteria
1. The Core IPC Foundation shall 自己ループ（同一プロセス内でのサーバ・クライアント双方向通信）による動作検証手段を備える。
2. When 本基盤単体のテスト実行が行われたとき、the Core IPC Foundation shall 送受信・Pub/Sub・Request/Response・接続断再接続の各機能を検証するテストケースを提供する。
3. The Core IPC Foundation shall Unity Editor PlayMode での手動検証手順（最小サンプルシーンまたは同等物）を提供する。
4. The Core IPC Foundation shall 他 spec（output-renderer-shell / ui-toolkit-shell）の実装が存在しなくても、本基盤単体で起動・動作・停止のライフサイクルを完結できる構造とする。

---

### Requirement 9: メッセージキューと配信セマンティクス

**Objective:** 基盤利用者として、UI の高頻度操作（スライダー等）でも配信が詰まらず、かつイベントが漏れない契約が欲しい。そうすれば上位 spec で個別に流量制御を書く必要がなくなる。

#### Acceptance Criteria
1. The Core IPC Foundation shall 受信側でメインスレッド配信キューを備え、`kind = state` のメッセージは同一トピックの先行メッセージを最新値で上書き（coalesce）する（see D-7, D-10）。
2. The Core IPC Foundation shall `kind = event` のメッセージは coalesce の対象外とし、受信順（FIFO）で必ず配信する（see D-7, D-10）。
3. The Core IPC Foundation shall `kind = request` / `kind = response` のメッセージは相関 ID で一意に対応付け、coalesce の対象外とする（see D-10）。
4. While 配信キューが溢れそうな負荷がかかった状態, the Core IPC Foundation shall state の coalesce により無制限なメモリ消費を発生させない。
5. If event の滞留が事前定義した上限（値は設計フェーズで確定）を超えた場合、the Core IPC Foundation shall 警告ログを出力し、事象を観測可能にする（破棄はしない）。
6. The Core IPC Foundation shall PublishState / PublishEvent を明示的に異なる API として公開し、呼び出し側が意図を混同できない形にする（see D-10）。

---

## Dig Summary

### 実施サマリ
- **ラウンド数**: 4 ラウンド（Round 1: 3 問 / Round 2: 3 問 / Round 3: 3 問 / Round 4 + 補足: 3 問）
- **質問総数**: 10 問
- **決定数**: 11 件（D-1〜D-11）

### 主要な発見（Key Discoveries）
1. **Topology は単一 Unity アプリ**: 「プロセス」という用語は物理 OS プロセスではなく論理分離を指していることを確定。単一 Unity アプリ内で UI とメイン出力を LocalHost ループバックで疎結合する最小構成で、将来の外部 UI（LAN/WebUI）は同じサーバへの追加クライアントとして受け入れる。Wave 1 のスコープが大きく絞り込まれた（D-1）。
2. **メイン出力 = サーバ、UI = クライアント**: メイン出力を権威ある状態所有者と位置付けることで、組み込み UI と将来の WebUI・LAN UI を**同一のクライアントコントラクト**で統一的に扱える。将来のトポロジー変更が不要（D-4）。
3. **state / event の API 分離**: 単に「メッセージを送る」ではなく、`PublishState`（最新値で上書き OK）と `PublishEvent`（漏らしてはいけない）を API レベルで分ける。D-7 の coalesce 戦略が実装可能となり、UI 高頻度操作時のキュー溢れを構造的に防ぐ（D-7, D-10）。

### 全決定一覧

| ID | トピック | 決定 | 根拠要点 | リスク |
| --- | --- | --- | --- | --- |
| D-1 | Process topology | 単一 Unity アプリ + LocalHost ループバック | Wave 1 スコープ最小化 | 低 |
| D-2 | トランスポート | WebSocket (RFC 6455) で確定 | 将来 WebUI とトランスポート統一 | 低 |
| D-3 | コールバックスレッド | 常に Unity メインスレッド | 各 spec のマーシャリング重複実装を回避 | 低 |
| D-4 | サーバロール | メイン出力側がサーバ、UI はクライアント | 将来外部 UI の受け入れ口を固定 | 低 |
| D-5 | シリアライゼーション | JSON / WebSocket テキストフレーム | 可読性・WebUI 親和性 | 低 |
| D-6 | ポート指定 | 設定ファイル + デフォルト値 | 多重起動・URL 固定運用の両立 | 低 |
| D-7 | キュー溢れ時の方針 | state は coalesce、event は FIFO | UI 高頻度操作のメモリ保護 | 中 |
| D-8 | Request タイムアウト | デフォルト 5 秒、リクエスト単位で上書き可 | 暗黙の永遠待ち回避 | 低 |
| D-9 | Editor ライフサイクル | PlayMode のみ常駐、ドメインリロード跨ぎなし | 実装単純化・予測可能性 | 低 |
| D-10 | state/event 宣言方法 | PublishState / PublishEvent の API 分離 | 意図をコード上で明示 | 低 |
| D-11 | メッセージサイズ上限 | 1 MB、超過は拒否 | メモリ保護・LAN 時の回線保護 | 低 |

### 残留リスク（設計フェーズで継続検討）

- **R-1: デフォルトポート番号の選定**: WebSocket 用に使用するデフォルトポート（49152〜65535 の dynamic/private 範囲から選定）が未確定。設計フェーズで他 VTuber 系ツールとの衝突可能性を踏まえて決定する。
- **R-2: イベントキューの上限値**: Requirement 9.5 の「event 滞留上限」の具体値（例: 1000 件）が未確定。設計フェーズで想定ワークロードを測って決定。
- **R-3: 再接続バックオフのパラメータ**: 指数バックオフの初期値・倍率・最大間隔・試行上限が未確定。Wave 1 ではクライアントが単一プロセス内に同居するため遭遇頻度は低いが、設計フェーズで標準的な値（例: 初期 250ms、2 倍、上限 5 秒、試行 10 回）を置く。
- **R-4: PlayerLoop ディスパッチの具体タイミング**: Update / LateUpdate / FixedUpdate のいずれで配信するかが未確定。メイン出力側で描画との競合を避ける配置を設計フェーズで検討。
- **R-5: 設定ファイルのフォーマット・配置**: JSON / ScriptableObject / Resources / StreamingAssets のいずれを使うかが未確定。プロジェクト全体の設定管理方針（今後 `.kiro/steering/` で確立される可能性あり）と揃える。
- **R-6: スキーマ進化の具体方針**: 未知フィールド無視の方針は決めたが、互換性が壊れる変更時の versioning 戦略（envelope の protocol_version フィールド等）が未設計。

### 次のアクション

1. 本要件書（`.kiro/specs/core-ipc-foundation/requirements.md`）をレビューし、必要であれば追加・修正を指示する。
2. 承認後、`/kiro:spec-design core-ipc-foundation` で設計フェーズへ進む。R-1〜R-6 は設計フェーズの出発点として引き継ぐ。


