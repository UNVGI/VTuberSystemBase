# Requirements Document

## Project Description (Input)
ui-toolkit-shell

Display 1 に表示する UI Toolkit ベースのメインウィンドウと、3 タブ切替機構、アセット事前ロード基盤を提供する spec。

スコープ:
- UI Toolkit のルート UIDocument と 3 タブ切替のタブバー
- 3 タブ分の UIDocument を起動時に一括ロードする仕組み
- 表示/非表示切替のみで切り替えるタブ遷移（都度インスタンス化しない）
- 非同期 AssetBundle 読み込み基盤（別スレッド、Completion 通知）
- タブ別 UXML/USS の配置規約とスキン差し替えポイントの設計
- 共通 UI コンポーネント（スライダー、カラーピッカー、番号付きリスト等）の置き場

非目標:
- 各タブの機能実装（spec #4-6 に分離）
- UI スキンそのもののデザイン（利用者側アセット）
- メイン出力側の描画・IPC ディスパッチャ（spec #2 に分離）

対応要件: docs/requirements.md の §3.1, §4, §6.1
上位計画: docs/spec-breakdown.md の spec #3（Wave 2、core-ipc-foundation に依存）

上流決定の継承:
- D-1: 単一 Unity アプリ内で UI/メイン出力を論理分離、LocalHost ループバック通信
- D-3: 受信コールバックは常に Unity メインスレッドで配信
- D-4: UI 側は IPC クライアントとして振る舞う
- D-5: メッセージは JSON / WebSocket テキストフレーム
- D-9: PlayMode 開始〜停止の区間のみ常駐、Edit モードでは常駐しない
- D-10: UI 側は PublishState / PublishEvent を使い分けて送信する

重要な性能要件: docs/requirements.md §4.2 により、タブ切替時にメイン出力を 1 フレームでもフリーズさせない。可能な限り起動時に全てロードし、どうしても必要な AssetBundle 等は別スレッドで非同期読み込みする。

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応
言語: 日本語で生成（CLAUDE.md の規約に従う）

## Open Questions and Decisions (Dig)

本セクションは本 spec 固有の設計上の決定事項を記録する。上流 spec である `core-ipc-foundation` の D-1〜D-11 は暗黙に継承される。明示的な仮置きは以下の通り。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| UI-1 | 起動時一括プリロードの厳格度 | **3 タブ分の UIDocument（VisualTreeAsset + StyleSheet）は Unity アプリ起動時に全てロードしアタッチ完了させる。タブ機能（spec #4〜#6）が必要とする追加アセットのうち AssetBundle / Addressable に格納されるものは、本シェルが提供する非同期ロード基盤を介して別スレッドで取得する**。プリロード完了前はタブ切替 UI を非活性表示する。 | docs/requirements.md §4.2 および §6.1 の「タブ切替時にメイン出力を 1 フレームでもフリーズさせない」を構造的に満たす最短経路。メインスレッドを確実にブロックしない契約を shell 層で担保することで、各タブ spec の実装者がロード戦略を個別に考えずに済む。 | 中（起動時間が増える可能性。設計フェーズでロード時間の上限を決定する） |
| UI-2 | タブ切替の表示制御方式 | **全タブの UIDocument は生成後、ルートパネルに常駐させ、タブ切替は `display: flex/none`（USS）または `visible` プロパティによる表示/非表示切替のみで行う。`UIDocument` の enable/disable、VisualTreeAsset の再 clone、instantiate は行わない**。 | docs/requirements.md §4.3 を直接反映。都度インスタンス化するとガベージ発生と UIDocument 初期化コストでタブ切替に可視の遅延が発生し得る。表示切替のみであればレイアウト計算のみで収まり、メイン出力の描画フレームへの影響を最小化できる。 | 低 |
| UI-3 | スキン差し替えポイントの粒度 | **USS（StyleSheet）差し替えを一次契約とし、UXML（VisualTreeAsset）差し替えはオプションとして受け付ける**。各タブの UXML には安定した USS セレクタ命名規約（クラス名プレフィクス）を導入し、利用者側プロジェクトは USS 追加差し込みで見た目を変更可能にする。UXML 差し替えは構造互換性を利用者が担保する前提で拡張点として用意する。 | docs/requirements.md §6.3 の「UI スキン差し替え」および §10 の「UI スキンの差し替え粒度」オープンイシューを、破壊的変更リスクが低い USS 優先に寄せる。多くの VTuber 配信現場は色調・フォント変更だけで足り、UXML 構造差し替えまで必要な利用者は限定的。 | 中（USS セレクタ命名規約の安定性がそのままスキン互換性となるため、命名の不用意な変更が破壊的変更になる） |
| UI-4 | 共通コンポーネント置き場の配布形態 | **本 spec 内に「共通 UI コンポーネントライブラリ」アセンブリを 1 本設け、タブ spec（#4〜#6）はそこを依存参照する**。コンポーネントは UXML カスタムコントロール + USS + C# ロジックの 3 点セットで提供する。 | タブ間で UI パーツ（スライダー、カラーピッカー、番号付きリスト、トグルグループ等）の重複実装を避ける。将来タブを追加する利用者プロジェクトも同じライブラリを再利用できる。 | 低 |
| UI-5 | Command 送信 API のロール | **UI 側は IPC クライアントとして、タブ spec から呼ばれる「Command 送信 API」を提供する**。タブ spec は自身のコマンド定義を送信 API に登録し、送信 API は `core-ipc-foundation` の `PublishState` / `PublishEvent` / `Request` を適切に呼び分ける（D-4, D-10 の継承）。受信（メイン出力側からの state/event）もメインスレッド配信（D-3）で受け付ける。 | `output-renderer-shell` のディスパッチャと対称な構造にすることで、タブ spec の実装者は「送信側」「受信側」の両方で同じメンタルモデルを使える。各タブが個別にトランスポートを呼ばない契約を shell で強制する。 | 低 |
| UI-6 | 非同期アセットロード基盤の実装方針 | **Addressables を標準とする**。非同期ロード API の実装は Unity 公式の `Addressables.LoadAssetAsync` / `LoadSceneAsync` 等を一次採用し、その上に本 shell の Completion コールバックを Unity メインスレッドで発火する薄い抽象を被せる。素の AssetBundle API の公開はしない。 | 利用者プロジェクト側のアセット管理が Unity 公式ツール（Addressables Groups Window）で標準化され、学習コストが低い。CDN / ローカル両対応で配信スタジオの運用選択肢が広い。低レイヤ API の公開は YAGNI。 | 低 |
| UI-7 | UI 状態の永続化範囲 | **永続化しない**。毎回アプリ起動時にデフォルトタブ（初期アクティブタブは設計フェーズで確定）から開始する。ウィンドウサイズ・位置等は Unity 標準挙動に委ねる。 | 本フェーズは配信オペレーション特化。状態保持の実装コスト・Display 設定変更時のウィンドウ復元エッジケース対応を避ける。各タブ固有の設定（選択アバター・ステージ・カメラ等）の永続化は各タブ spec の責務として分離される。 | 低 |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase の **UI 側シェル（UI Toolkit Shell）** を定義する。Display 1 に表示されるオペレーター向け GUI の土台であり、具体的には以下の 4 つの責務を持つ：

1. **UI Toolkit ルート UIDocument の提供**：Display 1 に全画面相当で表示される単一のルート UIDocument と、3 タブ切替のタブバーを提供する。
2. **3 タブ分 UIDocument の起動時一括プリロード**：キャラクター選択 / ステージ・ライティング / カメラスイッチャーの 3 タブの UIDocument（VisualTreeAsset + StyleSheet）を起動時にすべて生成・アタッチし、タブ切替は表示/非表示のみで行うことで **メイン出力を 1 フレームもフリーズさせない**（docs/requirements.md §4.2, §6.1）。
3. **非同期アセットロード基盤の提供**：AssetBundle / Addressable 由来の追加アセットを必要とするタブのため、別スレッドでロードを行いメインスレッドで Completion 通知を受けるための共通基盤を提供する。
4. **IPC クライアントと Command 送信口の提供**：`core-ipc-foundation`（spec #1）のクライアントロール（D-4 の継承）として `output-renderer-shell`（spec #2）に接続し、タブ spec が自身のコマンドを登録・送信できる Command 送信 API と、メイン出力側から返ってくる state / event / response を Unity メインスレッド（D-3 の継承）で受け取る購読口を提供する。

加えて、各タブ spec（#4〜#6）が共通して使う UI パーツ（スライダー、カラーピッカー、番号付きリスト、トグルグループ等）を提供する **共通 UI コンポーネントライブラリ** と、利用者プロジェクトがフォークせずに見た目を差し替えられる **スキンカスタマイズ拡張点** も本 spec の責務である。

本 spec は **UI 側のシェル** に限定され、各タブの機能ロジック（spec #4〜#6）、UI スキンそのもののデザイン（利用者側の責務）、メイン出力側の描画・ディスパッチャ（spec #2）は本 spec の責務外である。本フェーズでは **「タブを安全に抱え続ける器」** を確立することを最優先とし、機能ロジックは後続 spec に委ねる契約境界を明確化する。

## Boundary Context

- **In scope**:
  - Display 1 上に表示される UI Toolkit ルート UIDocument とその常駐ライフサイクル
  - 3 タブ切替用のタブバー（Character Selection / Stage-Lighting / Camera-Switcher の 3 項目）とアクティブタブ表示
  - 3 タブ分 UIDocument（VisualTreeAsset + StyleSheet）の起動時一括プリロードとルートパネルへの常駐アタッチ
  - 表示/非表示切替のみによるタブ遷移（UIDocument 再インスタンス化・再 clone は行わない）
  - AssetBundle / Addressable 由来アセットの別スレッド非同期ロード基盤と、Unity メインスレッドへの Completion 通知機構
  - タブ spec から呼ばれる Command 送信 API（`PublishState` / `PublishEvent` / `Request` の呼び分け）
  - メイン出力側から到着する state / event / response の購読口（Unity メインスレッド配信、D-3 の継承）
  - タブ別 UXML/USS の配置規約（フォルダ構造・命名規則・USS セレクタ命名）
  - スキン差し替えのための拡張点（USS 差し込み + オプションでの UXML 差し替え）
  - 共通 UI コンポーネントライブラリ（スライダー、カラーピッカー、番号付きリスト、トグルグループ等）
  - スタンドアロンビルドと Unity Editor PlayMode の両対応（D-9 の継承）
  - メイン出力側未接続時のフェイルセーフ動作（UI はスタンドアロンで起動・操作可能であり、接続確立後に反映を再開する）
- **Out of scope**:
  - 各タブの機能ロジック（spec #4〜#6 の責務。本 spec はタブの「入れ物」と「共通部品」のみ提供）
  - UI スキン（色、フォント、背景等）の具体デザイン資産（利用者プロジェクト側の責務）
  - メイン出力シーンの初期構成・ディスプレイ切替・ディスパッチャ（spec #2 の責務）
  - `core-ipc-foundation` のトランスポート・シリアライゼーション・接続管理そのもの（spec #1 の責務。本 spec はクライアントとして抽象インタフェース経由で利用するのみ）
  - カメラ状態の OSC 伝送（spec #6 の責務）
  - UI 操作状態の永続化（各タブ spec および後続フェーズの責務）
- **Adjacent expectations**:
  - `core-ipc-foundation`（spec #1）の抽象インタフェースとクライアントロール実装が利用可能であること
  - `output-renderer-shell`（spec #2）がサーバロールで待ち受けており、本 spec がクライアントとして接続対象とすること
  - 各タブ spec（#4〜#6）は本 spec が公開する UIDocument 配置規約、Command 送信 API、共通 UI コンポーネントライブラリを利用して機能を実装する
  - 本 spec は後続 spec の実装が不在でも単独で起動・3 タブ空枠描画・タブ切替・IPC 接続試行までを完結できること
  - Display 1 への表示先割り当ては本 spec の要件レベルで定義するが、将来的な Display 振り分けの抽象化は `runtime-display-selector-integration`（spec #7）の責務との整合性を維持すること

---

### Requirement 1: UI Toolkit ルート UIDocument の提供と Display 1 表示

**Objective:** オペレーターとして、Display 1 を開いた直後から 3 タブ構成のメインウィンドウが表示されている状態を得たい。そうすれば起動と同時に UI 操作へ移れ、配信前準備の立ち上がりが早くなる。

#### Acceptance Criteria

1. The UI Toolkit Shell shall UI Toolkit の **ルート UIDocument** を 1 つ提供し、起動時に生成・アクティベートする。
2. The UI Toolkit Shell shall ルート UIDocument を **Display 1**（`UIDocument.panelSettings.targetDisplay = 0`、または同等の指定）に割り当てる。
3. The UI Toolkit Shell shall ルート UIDocument の初期レイアウトとして **タブバー領域** と **タブコンテンツ領域** を備えた階層構造を構築する。
4. When Unity アプリケーションが起動したとき、the UI Toolkit Shell shall ルート UIDocument・タブバー・タブコンテンツ領域の構築を **任意のタブ操作可能化前** に完了する。
5. The UI Toolkit Shell shall ルート UIDocument を本 spec のアセンブリ定義（asmdef）内で生成・管理し、タブ spec（#4〜#6）からは公開 API 経由のみで参照させる。
6. Where `runtime-display-selector-integration`（spec #7）が将来組み込まれる場合, the UI Toolkit Shell shall Display 1 への割り当て実装を RDS ベースへ差し替え可能な抽象点として公開する。
7. The UI Toolkit Shell shall ルート UIDocument をメイン出力サーフェス（Display 2+）に **一切描画しない**（Display 1 専用に限定する）。

---

### Requirement 2: 3 タブ構成のタブバーとタブ切替機構

**Objective:** オペレーターとして、3 つのタブをアプリ実行中いつでも行き来し、かつ切替時にメイン出力映像が乱れない状態を得たい。そうすれば配信中でも安全に設定画面を開閉できる。

**Note:** docs/requirements.md §4.2 により、タブ切替時にメイン出力を 1 フレームでもフリーズさせないことが必須要件である。本要件は UI-2 の決定（表示/非表示切替のみ）に基づく。

#### Acceptance Criteria

1. The UI Toolkit Shell shall **Character Selection / Stage-Lighting / Camera-Switcher** の 3 つのタブを識別する論理枠を提供する。
2. The UI Toolkit Shell shall タブバー上に 3 タブそれぞれに対応する **切替操作 UI（ボタン相当）** を配置し、現在アクティブなタブを視覚的に識別可能にする。
3. When オペレーターがタブ切替操作を行ったとき、the UI Toolkit Shell shall 指定タブの UIDocument を表示状態にし、他タブの UIDocument を非表示状態にする（see UI-2）。
4. The UI Toolkit Shell shall タブ切替を **UIDocument の再インスタンス化・再 clone・VisualTreeAsset の再生成を行わずに**、USS の `display`（または `visible`）プロパティによる表示/非表示切替のみで実現する（see UI-2）。
5. The UI Toolkit Shell shall タブ切替処理の実行を Unity メインスレッド上で完結させ、I/O 待ちやアセットロード待ちのために切替を保留しない。
6. While 任意のタブがアクティブである間, the UI Toolkit Shell shall アプリ実行中の任意のタイミングで他タブへの切替を受け付ける。
7. If 起動時プリロードがまだ完了していない段階でタブ切替操作が行われた場合, the UI Toolkit Shell shall 切替操作 UI を非活性表示にしてタブ切替を発生させず、プリロード完了後に活性化する（see Requirement 3）。
8. The UI Toolkit Shell shall タブ切替イベントを各タブ spec が購読可能な形で公開し、非アクティブ化・再アクティブ化のタイミングでタブ spec 側が状態保存・復帰処理を挿入できる拡張点を備える。
9. The UI Toolkit Shell shall タブ切替処理中にメイン出力（Display 2+）の描画フレームを中断・遅延させる API 呼び出し（例: 同期 I/O、メインスレッドブロッキング処理）を一切含まない（see docs/requirements.md §4.2, §6.1）。

---

### Requirement 3: 起動時一括プリロード

**Objective:** オペレーターとして、起動完了後はタブ切替がすべて即時に行える状態を得たい。そうすれば配信前のリハーサル・本番中のタブ遷移で UI 応答待ちが発生しない。

**Note:** docs/requirements.md §4.2 および §6.1 の「タブ切替時にメイン出力を 1 フレームでもフリーズさせない」を構造的に満たすため、UI-1 の決定に従い 3 タブ分の UIDocument を起動時に一括プリロードする。AssetBundle / Addressable 等の動的アセットは Requirement 4 の非同期ロード基盤で扱う。

#### Acceptance Criteria

1. When Unity アプリケーションが起動したとき、the UI Toolkit Shell shall **3 タブ分すべての UIDocument（VisualTreeAsset + StyleSheet）** をルートパネル配下に生成・アタッチ完了させる（see UI-1）。
2. The UI Toolkit Shell shall プリロードが完了するまでの期間、タブバー上の切替操作 UI を **非活性表示** に保つ（see Requirement 2 第 7 項）。
3. When プリロードが完了したとき、the UI Toolkit Shell shall タブバー上の切替操作 UI を活性化し、初期アクティブタブ（値は設計フェーズで確定）を表示状態にする。
4. The UI Toolkit Shell shall プリロードの対象を **VisualTreeAsset・StyleSheet・共通 UI コンポーネントライブラリが参照するリソース** に限定し、AssetBundle / Addressable 等の動的アセットは Requirement 4 の非同期ロード基盤へ委譲する。
5. If プリロード対象のいずれかの読み込みに失敗した場合, the UI Toolkit Shell shall 失敗事由を診断ログへ記録し、該当タブのみ非活性表示のまま他タブの表示を継続する（UI 全体の起動は中断しない）。
6. The UI Toolkit Shell shall プリロード完了後は、**タブ切替時に VisualTreeAsset のロード・パース・clone を再実行しない** ことを構造的に保証する（see UI-2）。
7. The UI Toolkit Shell shall プリロード進捗（例: 読み込み済みタブ数 / 全タブ数）を診断 API から取得可能な形で公開する。

---

### Requirement 4: 非同期 AssetBundle / Addressable ロード基盤

**Objective:** タブ spec の開発者として、タブ機能がビルド時に同梱できないアセット（ステージ Prefab、アバター、バンドル化されたテクスチャ等）を必要とする場合に、メインスレッドを止めずに取得する共通手段を得たい。そうすれば各タブで個別にスレッディングコードを書かずに済み、メイン出力のフレームレートを守れる。

**Note:** docs/requirements.md §4.2 と §6.1 の「メインスレッドをブロックしないこと（別スレッド必須）」を直接反映する。本基盤は shell が提供し、各タブは API 利用者として扱う。Completion 通知は D-3 の継承により Unity メインスレッドで行う。

#### Acceptance Criteria

1. The UI Toolkit Shell shall **Addressables** を一次実装とする **非同期ロード API** を提供する（see UI-6）。素の AssetBundle API を公開しない（タブ spec は Addressables 経由のみでアセットを取得する）。
2. When タブ spec が非同期ロード API を呼び出したとき、the UI Toolkit Shell shall **ワーカースレッド（または Unity が提供する非同期ロード機構）** 上で実際の I/O を行い、Unity メインスレッドをブロックしない。
3. When 非同期ロードが完了したとき、the UI Toolkit Shell shall Completion コールバックを **Unity メインスレッド上** で呼び出す（see D-3）。
4. If 非同期ロードが失敗した場合, the UI Toolkit Shell shall 失敗事由（例外・パス・アセット識別子）を Completion コールバックに渡し、診断ログへも記録する。
5. While 非同期ロードが進行中である間, the UI Toolkit Shell shall 呼び出し元タブの UI を操作可能な状態に保ち、プレースホルダ表示または非活性化でロード待ちを示す手段を提供する（see docs/requirements.md §4.2 第 3 項）。
6. The UI Toolkit Shell shall 非同期ロード API を介した処理が **メイン出力（Display 2+）の描画フレームに干渉しない** ことを設計上保証する（別スレッド実施・メインスレッドでの完了通知のみ）。
7. The UI Toolkit Shell shall 同一アセットに対する重複ロード要求を抑止するか、または安全に複数 Completion を配信する構造を備える（具体方針は設計フェーズで確定）。
8. Where タブ spec がロード済みアセットの解放を要求する場合, the UI Toolkit Shell shall アンロード API を提供し、メモリ解放と参照カウント整合性を保つ。
9. The UI Toolkit Shell shall 非同期ロード API の呼び出し状況（進行中件数、失敗件数等）を診断 API から取得可能な形で公開する。

---

### Requirement 5: IPC クライアントロールと Command 送信口

**Objective:** タブ spec の開発者として、`output-renderer-shell` に対して送るコマンド（state / event / request）を登録・送信するための単一の入口を得たい。そうすれば各タブは自身のコマンド定義に集中でき、トランスポート呼び出し・スレッドマーシャリング・クライアント接続管理の重複実装を避けられる。

**Note:** 本 spec は `core-ipc-foundation`（spec #1）のクライアントロール（D-4 の継承）を用いる。送信側 API は D-10 の継承に基づき `PublishState` / `PublishEvent` / `Request` を明示的に使い分ける。受信コールバックは D-3 の継承により Unity メインスレッドで配信される。シリアライゼーションは D-5 の継承により JSON / WebSocket テキストフレームで行われる。

#### Acceptance Criteria

1. The UI Toolkit Shell shall `core-ipc-foundation` の抽象インタフェースを利用して、UI 側を **クライアントロール** として起動する（see D-4）。
2. The UI Toolkit Shell shall タブ spec がコマンドを発行するための **Command 送信 API** を提供し、`PublishState` / `PublishEvent` / `Request` に対応した異なる呼び出し口を公開する（see D-10, UI-5）。
3. When タブ spec が state 系コマンドを送信したとき、the UI Toolkit Shell shall `core-ipc-foundation` の `PublishState` を介してメイン出力側サーバへ送信する（see D-10）。
4. When タブ spec が event 系コマンドを送信したとき、the UI Toolkit Shell shall `core-ipc-foundation` の `PublishEvent` を介してメイン出力側サーバへ送信する（see D-10）。
5. When タブ spec が Request を発行したとき、the UI Toolkit Shell shall `core-ipc-foundation` の Request/Response プリミティブを介して送信し、Response を Unity メインスレッド上で呼び出し元へ返却する（see D-3, D-8）。
6. The UI Toolkit Shell shall タブ spec がメイン出力側から到着する state / event / response を購読するための **受信購読 API** を提供し、コールバックを Unity メインスレッド上で呼び出す（see D-3）。
7. The UI Toolkit Shell shall タブ spec が自身の購読を登録・解除するための公開 API を提供し、タブのライフサイクル（プリロード完了、アクティブ化、非アクティブ化、解放）と整合させる。
8. The UI Toolkit Shell shall Command 送信 API・受信購読 API を本 spec のアセンブリ定義（asmdef）内に隔離し、タブ spec からは公開 API 経由のみで利用可能にする。
9. If Command 送信 API が `core-ipc-foundation` の送信エラー（接続未確立・サイズ上限超過等）を受け取った場合, the UI Toolkit Shell shall エラーを呼び出し元タブへ伝搬し、UI クラッシュ・描画停止を発生させない。
10. The UI Toolkit Shell shall タブ spec による直接的なトランスポート呼び出し（WebSocket クライアント実装への直接依存）を禁止する構造を維持し、すべての送受信を本 shell の API 経由に限定する。

---

### Requirement 6: タブ UXML / USS の配置規約とスキン差し替え拡張点

**Objective:** タブ spec の開発者として、自タブの UXML / USS / 共通コンポーネント利用を既定の場所に置くだけで shell に認識される規約を得たい。加えて利用者プロジェクトの運用者として、パッケージをフォークせずに見た目（色・フォント・余白等）を差し替えられる拡張点を得たい。そうすれば各社の配信ブランドに合わせた UI スキンを適用可能になる。

**Note:** UI-3 の決定により、USS 差し替えを一次契約とし、UXML 差し替えはオプションとして受け付ける。

#### Acceptance Criteria

1. The UI Toolkit Shell shall タブごとの UXML（VisualTreeAsset）および USS（StyleSheet）の配置フォルダ規約と命名規則を定義する。
2. The UI Toolkit Shell shall タブ UXML のルート要素および主要要素に対する **USS セレクタ命名規約**（クラス名プレフィクス等）を定義し、利用者プロジェクトからの USS 追加差し込みで見た目を変更可能にする（see UI-3）。
3. When ルート UIDocument がプリロード中にタブ UXML を読み込むとき、the UI Toolkit Shell shall **利用者プロジェクト側の追加 USS** が存在する場合にそれをルートパネルへ追加適用する（デフォルト USS の上に重ねる形で差し替え効果を得る）。
4. Where 利用者プロジェクトが UXML そのものを差し替える場合, the UI Toolkit Shell shall 既定の VisualTreeAsset 参照を置き換え可能な拡張点（例: パッケージ設定・ScriptableObject 参照・名前付き解決）を提供する（see UI-3）。
5. The UI Toolkit Shell shall タブ spec 側の UXML / USS 配置規約違反（必須クラス名欠落、規約外の配置パス等）を起動時に検出し、診断ログへ記録する。
6. If 利用者プロジェクト側で UXML を差し替えた結果として必須要素が不足していた場合, the UI Toolkit Shell shall 該当タブのみを非活性表示に留め、他タブの表示と shell 全体の起動を継続する。
7. The UI Toolkit Shell shall スキン差し替え拡張点を **本パッケージをフォークせずに** 利用可能な形で提供する（利用者プロジェクト内の USS / UXML 追加のみで完結）。
8. The UI Toolkit Shell shall 既定の USS / UXML を本パッケージ内に同梱し、利用者プロジェクトが何も差し替えない場合でも視認可能な既定スキンで起動する。

---

### Requirement 7: 共通 UI コンポーネントライブラリ

**Objective:** タブ spec の開発者として、スライダー、カラーピッカー、番号付きリスト、トグルグループ等のパーツを各タブで重複実装せずに使い回したい。そうすればタブ間で UI の見た目・挙動の一貫性が保たれ、実装コストも下がる。

**Note:** UI-4 の決定により、共通 UI コンポーネントは本 spec の一部として提供し、タブ spec から依存参照させる。

#### Acceptance Criteria

1. The UI Toolkit Shell shall 以下のカテゴリを最低限カバーする **共通 UI コンポーネントライブラリ** を提供する：スライダー（数値入力）、カラーピッカー（RGB/HSV 相当）、番号付きリスト（可変長の整列リスト）、トグルグループ（排他選択）。
2. The UI Toolkit Shell shall 各共通コンポーネントを **UXML カスタムコントロール + USS + C# ロジック** の 3 点セットで提供し、UXML 内から直接参照可能な形にする。
3. The UI Toolkit Shell shall 各共通コンポーネントに対して Requirement 6 で定義した USS セレクタ命名規約を適用し、スキン差し替え経路から見た目を変更可能にする。
4. The UI Toolkit Shell shall 各共通コンポーネントの値変更・確定・選択変更等のイベントを、タブ spec が購読可能な形で公開する。
5. The UI Toolkit Shell shall 共通 UI コンポーネントライブラリを **独立したアセンブリ定義（asmdef）** として提供し、タブ spec からはライブラリ asmdef を参照することで利用可能にする。
6. Where タブ spec が共通コンポーネント以外の独自 UI パーツを必要とする場合, the UI Toolkit Shell shall タブ spec 自身で独自コンポーネントを実装することを妨げない（ライブラリの利用は任意）。
7. The UI Toolkit Shell shall 共通コンポーネントが内部的にメインスレッドをブロックする処理（同期 I/O、重い再レイアウト等）を行わない構造を維持する。

---

### Requirement 8: スタンドアロンビルドと Unity Editor PlayMode の両対応

**Objective:** 配信運用者および開発者として、ビルド後のスタンドアロン実行時と Unity Editor PlayMode の両方で同一の UI 挙動を得たい。そうすれば開発中の UI 検証と本番運用の挙動差を最小化でき、配信前リハーサルが Editor PlayMode で完結する。

**Note:** D-9 の継承により、Editor では PlayMode 開始〜停止の区間のみ常駐し、Edit モードでは常駐しない。ドメインリロードに跨る状態維持は試みない。

#### Acceptance Criteria

1. When Unity アプリケーションがスタンドアロンビルドとして起動したとき、the UI Toolkit Shell shall ルート UIDocument 生成・プリロード・IPC クライアント接続試行を自動的に実施する。
2. When Unity Editor が PlayMode に入ったとき、the UI Toolkit Shell shall スタンドアロン時と同一手順でルート UIDocument 生成・プリロード・IPC クライアント接続試行を自動的に実施する（see D-9）。
3. When Unity Editor が PlayMode を終了したとき、the UI Toolkit Shell shall ルート UIDocument・プリロード済みアセット・非同期ロード中のワーカー・IPC クライアント接続を完全に解放し、Edit モードに残留物を残さない（see D-9）。
4. While PlayMode の開始と停止が繰り返される間, the UI Toolkit Shell shall リソースリークや UIDocument 重複生成を発生させずに毎回クリーンに再初期化する。
5. The UI Toolkit Shell shall Unity Editor の **Edit モード** ではルート UIDocument・プリロード・IPC クライアントを起動しない（see D-9）。
6. The UI Toolkit Shell shall ドメインリロードに跨る状態維持を試みず、PlayMode 開始のたびに新しいライフサイクルで初期化する（see D-9）。
7. The UI Toolkit Shell shall スタンドアロン時と Editor PlayMode 時で、タブ spec から見た UI 配置規約・Command 送信 API・共通コンポーネント API・受信購読 API の挙動を同一に保つ。

---

### Requirement 9: メイン出力側未接続時のフェイルセーフ

**Objective:** 配信運用者として、メイン出力側（Display 2+ / spec #2）がまだ起動していない／接続が切れている状況でも、UI シェル自体は起動し操作を受け付け続けたい。そうすれば起動順序レースや一時的な通信断で UI が使えなくなる事態を避けられる。

**Note:** `core-ipc-foundation` の Requirement 5 により、UI クライアントは接続失敗・接続断時に自動再接続を試みる（spec #1 Requirement 5 Acceptance Criteria 2）。本 spec は上位層でこの契約を具現化する責務を負う。

#### Acceptance Criteria

1. When UI 起動時にメイン出力側サーバが未起動であったとき、the UI Toolkit Shell shall ルート UIDocument 生成・プリロードを正常に完了し、タブバーを表示してオペレーター操作を受け付ける。
2. While メイン出力側との接続が未確立の状態が継続している間, the UI Toolkit Shell shall UI の描画・タブ切替・操作 UI 応答を中断・停止させない。
3. When メイン出力側サーバが後から起動し接続が確立されたとき、the UI Toolkit Shell shall Command 送信 API 経由の送信を通常通り開始する。
4. If Command 送信 API 呼び出し時に接続が未確立であった場合, the UI Toolkit Shell shall 定義済みの方針（例: エラー返却、または接続確立待ちでの保留）に従って呼び出し元タブへ通知し、UI クラッシュ・例外伝搬・描画停止を発生させない。
5. When `core-ipc-foundation` から接続断・再接続イベントが通知されたとき、the UI Toolkit Shell shall 接続状態を UI 上の診断表示または通知領域で確認可能な形で公開する（具体 UX は設計フェーズで確定）。
6. The UI Toolkit Shell shall メイン出力側が `output-renderer-shell` の Requirement 2 に従い Display 1 フォールバック描画中である場合（OR-1 の継承相当）、UI 側で **誤配信リスクの警告** を表示するための UI 通知経路を提供する（具体 UX は設計フェーズで確定）。
7. The UI Toolkit Shell shall メイン出力側との接続有無と独立に、タブ切替・共通 UI コンポーネント動作・非同期ロード基盤を機能させる。

---

### Requirement 10: 本 spec 単体での検証可能性

**Objective:** spec オーナーとして、本シェルを後続 spec（#4〜#6）の実装を待たずに検証したい。そうすれば Wave 2 の完了を Wave 3 の完了に引きずられずに判定でき、後続 spec が本シェルに依存する前に品質を担保できる。

#### Acceptance Criteria

1. The UI Toolkit Shell shall 後続 spec（character-selection-tab / stage-lighting-volume-tab / camera-switcher-tab）の実装が不在でも、単独でルート UIDocument 生成・3 タブ空枠プリロード・タブ切替・IPC クライアント接続試行を完遂できる構造とする。
2. The UI Toolkit Shell shall タブ空枠（ダミーコンテンツ）を shell 側で提供し、タブ spec が存在しないタブ位置でも切替操作 UI を押下可能にする（内容は空 VisualTreeAsset 相当）。
3. The UI Toolkit Shell shall `core-ipc-foundation` の自己ループ機構（spec #1 Requirement 8）を活用して、ダミーコマンドを自プロセス内で送受信し、Command 送信 API と受信購読 API が正しく機能することを検証する手段を備える。
4. The UI Toolkit Shell shall Unity Editor PlayMode での手動検証手順（最小サンプルシーンまたは同等物）を提供する。
5. When 本シェル単体のテスト実行が行われたとき、the UI Toolkit Shell shall ルート UIDocument 生成・プリロード完了判定・タブ切替の表示/非表示挙動・非同期ロード基盤の Completion 配信・Command 送信 API の呼び分け・メイン出力側未接続時のフェイルセーフ挙動を検証するテストケースを提供する。
6. The UI Toolkit Shell shall IPC クライアント部分について、テスト時に差し替え可能な **モック実装**（`core-ipc-foundation` の抽象インタフェースに対するテストダブル）を受け入れる構造を備える。
7. The UI Toolkit Shell shall 非同期ロード基盤について、テスト時に差し替え可能な **モック実装**（即時完了するフェイク、任意の失敗を注入できるフェイク等）を受け入れる構造を備える。

---

### Requirement 11: 観測性・診断可能性

**Objective:** システム開発者・運用者として、UI 側で発生する不具合が「ルート UIDocument 起因」「プリロード起因」「タブ切替起因」「非同期ロード起因」「IPC 送受信起因」のいずれかを即座に切り分けたい。そうすれば後続タブ spec のハンドラ実装でのバグと本シェル自体のバグを混同せずに済む。

#### Acceptance Criteria

1. The UI Toolkit Shell shall ルート UIDocument 生成・プリロード開始・各タブ UIDocument アタッチ完了・プリロード完了・失敗事由をログ出力する。
2. The UI Toolkit Shell shall タブ切替操作の発生時刻・切替元タブ・切替先タブ・切替所要時間をログ出力する。
3. The UI Toolkit Shell shall 非同期ロード基盤の開始・完了・失敗・アンロードの各イベントを、対象アセット識別子とともにログ出力する。
4. The UI Toolkit Shell shall Command 送信 API の呼び出し・送信結果（成功／失敗）・Request の相関 ID・Response 受信をログレベルに応じて出力する。
5. The UI Toolkit Shell shall 受信購読 API で到着した state / event / response の種別と topic をログレベルに応じて出力する。
6. When IPC クライアントで接続断・再接続イベントが発生したとき、the UI Toolkit Shell shall 当該イベントを診断ログへ記録する。
7. The UI Toolkit Shell shall 診断ログを Unity コンソール、または UI 側の診断表示領域にのみ流し、**メイン出力サーフェス（Display 2+）には一切描画しない**（`output-renderer-shell` Requirement 5 との整合性を保つ）。
8. Where 開発者がデバッグ用途で詳細ログを必要とする場合, the UI Toolkit Shell shall ログレベルを外部から切替可能にする。
9. The UI Toolkit Shell shall 診断に必要な最小限の状態（例: プリロード進捗、現在アクティブタブ、非同期ロード進行中件数、IPC 接続状態、登録済み Command 送信ハンドラ数）を外部から取得可能な形で公開する。

---

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案適用、要件レベル仮置き）
- **決定数**: 5 件（UI-1〜UI-5）
- **継承**: core-ipc-foundation の D-1, D-3, D-4, D-5, D-9, D-10 を上流決定として継承。output-renderer-shell の OR-1（Display 2 フォールバック時の UI 警告）は本 spec の Requirement 9 に横展開。
- **主要な発見**:
  - タブ切替時のメイン出力フリーズ禁止要件（docs/requirements.md §4.2, §6.1）を満たすため、「起動時プリロード」と「表示/非表示切替のみによるタブ遷移」を構造的契約として shell に埋め込む方針を採択（UI-1, UI-2）。
  - UI スキン差し替えは USS 差し込みを一次契約に据え、UXML 差し替えは構造互換性を利用者が担保する前提のオプションとする（UI-3）。docs/requirements.md §10 のオープンイシューに対する仮置き回答。
  - 共通 UI コンポーネントをタブ spec 間で重複実装させないため、本 spec 内に独立 asmdef のライブラリを同梱する（UI-4）。
  - Command 送信 API を `output-renderer-shell` のディスパッチャと対称な構造で提供することで、タブ spec の実装者に「送信」「受信」で一貫したメンタルモデルを提供する（UI-5）。
- **残留リスク**:
  - R-1: プリロード所要時間の上限（起動時間 vs タブ切替応答性のトレードオフ）は設計フェーズで確定が必要。
  - R-2: USS セレクタ命名規約の具体ルール（クラス名プレフィクス、BEM 風等）と、規約違反検出の具体ロジックは設計フェーズで確定。
  - R-3: UXML 差し替え拡張点の具体解決機構（ScriptableObject / Addressable / 名前付き解決）は設計フェーズで確定。
  - R-4: 非同期ロード基盤の重複ロード抑止ポリシー（参照カウント / キャッシュ / 共有 Task）は設計フェーズで確定。
  - R-5: 接続未確立時の Command 送信 API 挙動（エラー返却 vs 保留キュー）は設計フェーズで確定。output-renderer-shell 側の挙動と整合させる必要あり。
  - R-6: メイン出力側 Display 1 フォールバック時の UI 警告 UX（OR-1 の R-1）は本 spec 範囲内で設計フェーズで確定。
  - R-7: 初期アクティブタブの既定値（Character Selection / Stage-Lighting / Camera-Switcher のいずれ）は設計フェーズで確定。
  - R-8: プリロード失敗タブの再試行ポリシー（手動リトライ UI の要否等）は設計フェーズで確定。

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案、要件レベル厳選）
- **質問数**: 2 問 / 本 spec 固有の決定: 2 件（UI-6, UI-7）
- **継承**: core-ipc-foundation の D-1, D-3, D-4, D-5, D-9, D-10、output-renderer-shell の OR-1
- **本 spec 固有の主要決定**:
  - UI-6 Addressables を標準とする（素の AssetBundle API は公開しない）
  - UI-7 UI 状態は永続化しない（毎回デフォルトタブ開始）
  - UI-1〜UI-5 はエージェント生成時の妥当なデフォルトを採用（プリロード戦略、表示切替、スキン拡張、共通コンポーネント、送受信 API の対称構造）
- **残留リスク**:
  - R-1: 初期アクティブタブ（Character Selection / Stage-Lighting / Camera-Switcher のどれか）は設計フェーズで確定
  - R-2: USS セレクタ命名規約の安定 API の明確化（UI-3 のスキン契約を壊さない運用ルール）
