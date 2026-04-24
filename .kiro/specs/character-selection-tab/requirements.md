# Requirements Document

## Project Description (Input)
character-selection-tab

MoCap アクター（Slot）とアバターの対応付け、および個別設定を行う UI タブを提供する spec。ゲームのキャラクター選択 UI を参考に MoCap アクターをプレイヤーに見立てたデザインとする。

スコープ:
- RealtimeAvatarController（com.hidano.realtimeavatarcontroller）の組み込みと依存設定
- ゲームのキャラクター選択画面風の UI（Slot 一覧 × アバター候補）
- Slot ↔ アバターの割り当て／変更 UI
- アバター個別設定の UI（RealtimeAvatarController が提供する設定項目を露出）
- 選択・設定状態を IPC で output-renderer-shell に送信しメイン出力側へ反映
- 選択状態の永続化（ファイル保存／復元、形式は設計フェーズで確定）

非目標:
- アバターアセットそのもの（利用者側の責務）
- RealtimeAvatarController 本体の機能追加
- 他タブ（ステージ、カメラ）の機能
- UI Toolkit シェル基盤そのもの（spec #3 に分離）

対応要件: docs/requirements.md の §4.1, §5.1
上位計画: docs/spec-breakdown.md の spec #4（Wave 3、output-renderer-shell と ui-toolkit-shell に依存）

上流決定の継承:
- D-1: 単一 Unity アプリ + LocalHost ループバック
- D-3: 受信コールバックは Unity メインスレッド
- D-4: UI 側クライアント / メイン出力側サーバ
- D-10: PublishState / PublishEvent の使い分け
- OR-2: state 競合は last-write-wins
- UI-6: 追加アセットは Addressables 経由
- UI-7: タブ共通 UI 状態は永続化しない（タブ固有設定は本 spec の責務）

採用パッケージ: https://github.com/Hidano-Dev/RealtimeAvatarController
- Slot ベースの MoCap ソース管理
- アバタープロバイダ抽象化
- モーションパイプライン

環境: Unity 6.3 URP / Windows x86 / スタンドアロンと Editor PlayMode 両対応
言語: 日本語で生成（CLAUDE.md の規約に従う）

## Open Questions and Decisions (Dig)

本セクションは本 spec 固有の設計上の決定事項を記録する。上流 spec（`core-ipc-foundation`, `output-renderer-shell`, `ui-toolkit-shell`）の決定（D-1, D-3, D-4, D-5, D-9, D-10, D-11, OR-1, OR-2, UI-1〜UI-7）は暗黙に継承される。本 spec 固有の暗黙デフォルトは以下の通り。

| ID | トピック | 決定内容 | 根拠 | リスク |
| --- | --- | --- | --- | --- |
| CS-1 | RealtimeAvatarController（以下 RAC）インスタンスの配置 | **RAC ランタイム本体はメイン出力側に常駐する**。UI 側はコマンド発行層に徹し、RAC の Slot／アバター／モーションパイプラインを直接参照しない。UI ↔ RAC 間のやり取りはすべて `core-ipc-foundation` の IPC（state / event / request）経由で行う。 | MoCap 受信と描画が同一側に集約され、モーションデータの伝送オーバーヘッドと UI 側からの直接 GameObject 操作が原理的に不要になる（§3.2 の「UI から直接メイン出力側オブジェクトを触らない」契約と整合）。将来の LAN/WebUI クライアントも同じ IPC 契約でキャラクター選択を行える（D-4）。 | 低 |
| CS-2 | Slot ライフサイクルの所有者 | **Slot のライフサイクル（生成・破棄・個数管理）は RAC の提供する機構に準拠する**。UI 側は Slot 一覧を IPC 経由で取得・購読し、UI 上の「プレイヤーカード」として表示する。Slot 個数の増減 API は RAC が提供する範囲で露出する（本 spec では Slot 生成の独自管理機構を実装しない）。 | RAC 本体機能追加は非目標（docs/spec-breakdown.md #4）。Slot 機構の二重実装を避け、RAC 側の責務境界を尊重する。 | 中（RAC の API 変更に追従する必要がある。設計フェーズで具体 API を棚卸しする） |
| CS-3 | Slot の空状態の扱い | **Slot はアバター未割り当て（empty）状態を持つことを許可する**。UI はプレイヤーカードを empty 状態で表示し、オペレーターは任意のタイミングでアバターを割り当てられる。`割り当て解除（reset）` も離散操作として提供する。 | 配信現場では「アクターは参加しているがアバターは未選定」「アバターを一度外して別のものに差し替える」運用が発生する。state コマンドで「空」を明示的に表現するほうが、null と未送信が区別できず事故につながる運用よりも安全。 | 低 |
| CS-4 | アバター識別の観点（何をもって「同じアバター」とみなすか） | **UI ↔ メイン出力の IPC では Addressables の安定アドレス（string key）をアバター識別子として用いる**。人間可読の表示名は別フィールドで伝送する（ローカライズ・同名ハンドリングのため ID と表示名を分離）。 | UI-6 により追加アセットは Addressables 経由。Addressables の key は利用者プロジェクトの GroupWindow で一元管理され、Git で差分管理できる。Prefab GUID / アセットパス依存は Editor と Build で差分が出やすく不安定。 | 中（Addressables key の運用規約は利用者側の責務。規約違反を診断ログで検出する） |
| CS-5 | アバター個別設定スキーマの所有者 | **アバター個別設定のスキーマ（どのアバターが何の設定項目を持つか）は RAC 側が権威**。UI は設定項目の定義を IPC の Request で取得し、そのメタデータ（項目名・型・レンジ・既定値）から動的に UI コントロールを生成する（静的に UI を手書きしない）。 | RAC 本体機能追加は非目標。アバターごとに設定項目が異なる可能性があり、UI 側に個別 UI を固定実装するとアバター追加ごとに UI 改修が必要になる。動的 UI 生成は拡張性が高い。`ui-toolkit-shell` の共通 UI コンポーネントライブラリ（UI-4）を使い回せる。 | 中（RAC 側の設定メタデータ API が未確定の場合、当面は最小共通項目セットに絞る。設計フェーズで RAC の実 API を棚卸す） |
| CS-6 | 連続値設定と離散操作の伝送方式 | **連続値設定（スライダー値、数値フィールド、カラーピッカー等）は `PublishState`（coalesce 対象）で送信する**。**離散操作（reload avatar, reset slot, apply preset 等）は `PublishEvent`（FIFO 必須）で送信する**。 | D-10 と UI-5 の直接反映。UI スライダー操作等の高頻度連続値は中間値を落としても最終値が反映されれば視覚的には支障ない。一方 reload / reset は漏らしてはならない。 | 低 |
| CS-7 | state / event のトピック粒度 | **state トピックは `slot/{slotId}/assignment`（割当）、`slot/{slotId}/settings/{key}`（設定値）等、Slot 単位または設定項目単位で細分化する**。event トピックは `slot/{slotId}/command`（操作種別を payload で区別）等の Slot 単位に集約する。 | coalesce は同一トピックで効く（D-7）。設定項目ごとにトピックを分けると、スライダーごとに独立して coalesce が効き、別項目の中間値を巻き込まない。event は種類が少なく payload 差で済むため、トピック爆発を避ける。 | 中（トピック命名の具体規約は設計フェーズで確定。命名規約が後から変わると互換性影響が出る） |
| CS-8 | 保存対象の範囲 | **永続化対象は「Slot ↔ アバター割当」「アバター個別設定値」「`これまでに使用したアバターの履歴/カタログ選好（任意）`」に限定する**。Slot 個数や RAC 自体のグローバル設定は永続化対象外（それらは RAC の所掌またはプロジェクト設定）。 | UI-7 の補足として「タブ固有設定」の境界を明確化する。スコープを絞り、保存ファイルの肥大化と復元時のエッジケースを減らす。 | 低 |
| CS-9 | 保存タイミング | **設定変更のたびに即時保存（デバウンス付き）を行い、アプリ終了イベントでもフラッシュする**。明示的な「保存ボタン」は設けない（設計フェーズで UX 再検討可）。 | 配信現場では「保存忘れ」が配信事故に直結する。デバウンス（例: 500ms 無操作で保存）で I/O 頻度とデータ保全性を両立する。 | 中（I/O 失敗時の UX は設計フェーズで確定） |
| CS-10 | 復元タイミング | **アプリ起動（PlayMode 開始含む）時に、メイン出力側との IPC 接続確立後に保存ファイルを読み込み、state コマンドとしてメイン出力へ送信する**。これによりメイン出力側は「通常運用中の state 受信」と同一経路で復元を処理できる。 | 復元経路を通常 state 経路と一致させると、メイン出力側のハンドラ実装が 1 本化される。起動時専用の特殊 API を追加しない（YAGNI）。 | 低 |
| CS-11 | 不可用アバター（保存されたアドレスが解決できない）への対応 | **該当 Slot を empty 状態に落としたうえで UI 上に警告バッジを表示する**。他 Slot の復元は中断せず続行する。 | 配信直前にアバターアセットが差し替えられているケース（Addressables key 変更、プロジェクト配布漏れ等）に対して、サイレントな誤割り当てを発生させない。他の Slot は復元して運用を続けられる。 | 中（警告 UX は設計フェーズで確定。復元ログは診断 API で提供） |
| CS-12 | 設定保存の単位 | **名前付きプリセットを複数保持し切替えられる形とする**。CS-8 の保存対象（Slot ↔ アバター割当 / 個別設定値）は「プリセット単位」で保存される。本タブは `create / rename / duplicate / delete / activate` のプリセット CRUD UI と、アクティブプリセットの表示を提供する。プリセット切替時は通常 state 経路（CS-10）で一括適用する。 | 配信シーンごと（朝配信、週末コラボ等）の切替運用を 1 ボタンで可能にする。プリセット切替時のメイン出力側への適用は「複数 state コマンドの束」として通常経路で送るため、メイン出力側の特殊ハンドラは不要。 | 中（プリセット数・命名衝突・切替中の半端な適用状態の扱いを設計フェーズで確定） |
| CS-13 | アバター候補一覧 UI のプレビュー表現 | **ストアドサムネイル（Addressables に同梱される PNG 等の静止画）を使用する**。サムネイルアセットのキーはアバター識別子から導出する規約（例: `{avatar_key}.thumbnail`）とする。サムネイル未設定のアバターはデフォルト画像にフォールバックする。 | ランタイム 3D プレビューは GPU コスト増でメイン出力 fps を脅かすため不採用。ストアドサムネイルは Addressables 側に任せることで UI 側の実装を単純化でき、利用者プロジェクトの裁量でサムネイル品質を決められる。 | 低 |

---

## Requirements

## Introduction

本 spec は、VTuberSystemBase における **キャラクター選択タブ（Character Selection Tab）** を定義する。Display 1 側の UI Toolkit シェル（spec #3 `ui-toolkit-shell`）内に配置される 3 タブのうち 1 つとして、以下の責務を持つ：

1. **プレイヤーカード（Slot）一覧の表示**：MoCap アクター（RealtimeAvatarController の Slot）を「ゲームのプレイヤー」に見立てたカード UI として列挙し、現在の割当状態・設定状態をオペレーターに提示する。
2. **アバター候補一覧の表示と割当 UI**：利用可能なアバター（Addressables で登録されたもの）を「選べるキャラクター」として提示し、各 Slot に割当・変更・解除する操作を提供する。
3. **アバター個別設定 UI**：各アバターが提供する個別設定項目（RAC が定義するメタデータに基づく）を UI コントロールとして動的に生成し、操作結果を state コマンドとしてメイン出力へ送信する。
4. **IPC 契約に基づく状態同期**：Slot ↔ アバター割当と個別設定は `ui-toolkit-shell` の Command 送信 API を介してメイン出力側へ送信し、メイン出力側では RAC 本体がモーション適用を行う。離散操作（reload, reset）は `PublishEvent` で、連続値は `PublishState` で送る（D-10）。
5. **選択状態の永続化と復元**：タブ内で操作された割当・個別設定をファイルに保存し、次回起動時に自動復元する（UI-7 の補足として、タブ固有設定は本 spec の責務）。
6. **失敗ハンドリング**：Addressables ロード失敗、RAC からのエラー応答、保存アバターの解決失敗等について、UI 側で安全に縮退し、メイン出力描画に波及させない。

本 spec は **UI 側のタブ機能** に限定される。RAC ランタイム本体の振る舞い、Slot 機構の実装、モーションパイプラインの内部、IPC トランスポートそのもの、UI Toolkit シェル基盤、メイン出力シーンの描画、他タブの機能は本 spec の責務外である（CS-1, CS-2 参照）。

## Boundary Context

- **In scope**:
  - キャラクター選択タブの UIDocument（VisualTreeAsset + StyleSheet）と、`ui-toolkit-shell` の UXML/USS 配置規約（UI-3, UI-4）への適合
  - プレイヤーカード（Slot）一覧 UI：Slot 状態（empty / assigned / error）の可視化
  - アバター候補一覧 UI：Addressables から解決されたアバター一覧（CS-4）の表示
  - Slot ↔ アバター割当操作 UI（割当・変更・解除）
  - アバター個別設定 UI（RAC メタデータから動的生成、CS-5）
  - 連続値設定の `PublishState` 送信（CS-6）と離散操作の `PublishEvent` 送信（CS-6）
  - メイン出力側からの state / event / response の購読（Slot 一覧変化、RAC エラー、割当確定応答等）
  - タブ固有設定の永続化（割当・個別設定）と起動時復元（CS-8, CS-9, CS-10）
  - Addressables ベースのアバターアセット読込み（UI-6）をトリガする UI 側フロー（実際の I/O は `ui-toolkit-shell` の非同期ロード基盤に委譲）
  - 不可用アバター・RAC エラー・Addressables ロード失敗時の縮退挙動（CS-11）
  - タブのアクティブ化／非アクティブ化時の購読登録／解除（`ui-toolkit-shell` Requirement 2 Acceptance Criteria 8 の拡張点を利用）
  - スタンドアロンビルドと Unity Editor PlayMode の両対応（D-9 の継承）
- **Out of scope**:
  - RAC ランタイム本体（Slot 機構、アバタープロバイダ、モーションパイプライン）の実装・改修（CS-1, CS-2。RAC は採用パッケージをそのまま使用）
  - メイン出力側での RAC インスタンス起動・Slot 生成・アバター適用・モーション適用そのもの（メイン出力側の責務。本 spec は IPC 契約を定義し、実装は `output-renderer-shell` のディスパッチャ経由で RAC を叩く層が担う）
  - アバターアセット本体のデザイン・リグ・テクスチャ（利用者プロジェクトの責務）
  - `core-ipc-foundation` のトランスポート・シリアライゼーション（spec #1 の責務）
  - `ui-toolkit-shell` のルート UIDocument・タブ切替機構・Command 送信 API 実装・非同期ロード基盤実装（spec #3 の責務。本 spec はこれらの公開 API の利用者）
  - `output-renderer-shell` のシーン初期化・ディスパッチャ（spec #2 の責務）
  - 他タブ（ステージ・ライティング、カメラスイッチャー）の機能
  - MoCap ハードウェア設定 UI（RAC がカバーする範囲はそちらに委ね、UI 側で追加の機材設定画面は提供しない）
  - カメラ切替・ステージ切替・Volume 編集（他タブの責務）
  - タブ共通 UI 状態（アクティブタブ、ウィンドウサイズ等）の永続化（UI-7 により永続化しない）
- **Adjacent expectations**:
  - `core-ipc-foundation`（spec #1）の抽象インタフェースが利用可能で、`PublishState` / `PublishEvent` / `Request` / 受信購読が Unity メインスレッド配信で使えること（D-3, D-10）
  - `ui-toolkit-shell`（spec #3）が Command 送信 API・受信購読 API・共通 UI コンポーネントライブラリ・非同期ロード基盤・タブ配置規約を公開していること（UI-4, UI-5, UI-6）
  - `output-renderer-shell`（spec #2）のディスパッチャが、本 spec で定義する state / event / request の topic を受け付けるハンドラを登録できる構造であること（ディスパッチャ側の実装アダプタは別 spec または本 spec の派生で提供される想定）
  - 採用パッケージ RealtimeAvatarController（v0.1.0 相当）が Unity 6000.3.10f1+ で利用可能であり、Slot・アバタープロバイダ・モーションパイプラインの公開 API をメイン出力側コードから呼び出せること
  - 利用者プロジェクトが Addressables Groups で必要なアバターアセットを登録・ビルドしていること（CS-4）

---

### Requirement 1: キャラクター選択タブ UIDocument の配置と UI Toolkit シェル統合

**Objective:** タブ spec の開発者として、本タブを `ui-toolkit-shell` の 3 タブのうち 1 枠に正しく載せ、起動時一括プリロード・表示/非表示切替のみのタブ遷移・メイン出力を 1 フレームもフリーズさせない要件を満たす形で統合したい。そうすればタブ独自のロード戦略を書かずに済み、シェル側の契約で性能要件を構造的に担保できる。

**Note:** 本要件は `ui-toolkit-shell` の Requirement 1（ルート UIDocument）・Requirement 2（タブ切替）・Requirement 3（起動時一括プリロード）の契約を受け入れる側の責務を定義する。

#### Acceptance Criteria

1. The Character Selection Tab shall 本タブ専用の UIDocument（VisualTreeAsset）および StyleSheet を、`ui-toolkit-shell` が定義する UXML/USS 配置規約（UI-3 相当）に従って提供する。
2. The Character Selection Tab shall 本タブのルート要素および主要要素に対して、`ui-toolkit-shell` の USS セレクタ命名規約（クラス名プレフィクス等）を適用し、スキン差し替え経路（UI-3）から見た目を変更可能にする。
3. When `ui-toolkit-shell` が起動時プリロードを実行したとき、the Character Selection Tab shall 本タブの VisualTreeAsset・StyleSheet を同期的にアタッチ完了させ、タブ切替時に再 clone や再生成を発生させない（see UI-1, UI-2）。
4. When 本タブがアクティブ化されたとき、the Character Selection Tab shall USS の `display` / `visible` プロパティによる表示化のみで UI を提示し、VisualTreeAsset の再ロード・メインスレッドブロッキング処理を行わない（see UI-2）。
5. When 本タブが非アクティブ化されたとき、the Character Selection Tab shall `ui-toolkit-shell` が公開するタブ切替イベント（see UI Requirement 2 Acceptance Criteria 8）に応じて、購読解除やストリーミング UI の一時停止など必要な状態保存処理を行う。
6. The Character Selection Tab shall 本タブの UI 構築・表示切替処理において、メイン出力（Display 2+）の描画フレームに干渉する同期 I/O・メインスレッドブロッキング処理を一切含まない（see docs/requirements.md §4.2, §6.1）。
7. The Character Selection Tab shall 本タブのアセンブリ定義（asmdef）を独立させ、`ui-toolkit-shell` の公開 API と `core-ipc-foundation` の抽象インタフェース以外に直接依存しない参照方向を維持する。
8. Where 利用者プロジェクトが本タブの UXML を差し替える場合, the Character Selection Tab shall `ui-toolkit-shell` の UXML 差し替え拡張点（UI-3）を経由させ、必須要素の欠落があれば診断ログへ記録する。

---

### Requirement 2: Slot（プレイヤーカード）一覧 UI

**Objective:** 配信オペレーターとして、MoCap アクターごとにカード状の UI を並べ、各 Slot の「空き／割当済／エラー」の状態が一目で分かる画面を得たい。そうすれば配信前準備中にアクター参加状況とアバター割当漏れを即座に把握できる。

**Note:** Slot ライフサイクルは RAC 本体が所有し（CS-2）、本タブは IPC 経由で取得した Slot 一覧を可視化する層に徹する（CS-1）。

#### Acceptance Criteria

1. The Character Selection Tab shall メイン出力側から購読した **Slot 一覧 state**（topic 例: `slots/list`、具体名は設計フェーズで確定）を元に、各 Slot に対応する **プレイヤーカード UI** を画面上に列挙する。
2. The Character Selection Tab shall 各プレイヤーカードに、少なくとも以下の情報を可視化する：Slot 識別子、現在の割当アバター（empty / アバター表示名 / エラー）、個別設定を開くためのエントリポイント。
3. When Slot 一覧 state が更新されたとき（Slot 追加・削除・状態変化）、the Character Selection Tab shall プレイヤーカード UI を追従更新し、UIDocument の再インスタンス化を発生させない（see UI-2）。
4. The Character Selection Tab shall 各 Slot が **empty 状態**（アバター未割当）を取り得ることを UI 上で明示的に区別できる視覚表現を提供する（see CS-3）。
5. When Slot がアバター未割当（empty）であるとき、the Character Selection Tab shall 該当プレイヤーカードからアバター割当操作を開始できる UI を提供する（see Requirement 4）。
6. When Slot にアバターが割当済みであるとき、the Character Selection Tab shall 該当プレイヤーカードからアバター変更・個別設定・割当解除（reset）・再読込（reload）の各操作を開始できる UI を提供する。
7. If Slot の状態が **エラー状態**（RAC からエラー通知あり、または割当アバターが解決不能）であった場合, the Character Selection Tab shall 該当プレイヤーカード上に警告バッジを表示し、操作系 UI を縮退させつつ、エラー詳細を UI 側診断領域から参照可能にする（see Requirement 7, CS-11）。
8. The Character Selection Tab shall Slot 一覧の表示順を安定的な順序（例: Slot 識別子昇順、または RAC が付与する順序）で固定し、state 更新のたびに順序が揺らがないようにする。
9. While Slot 一覧 state がまだメイン出力側から受信できていない間, the Character Selection Tab shall プレイヤーカード領域にプレースホルダまたは「接続待ち」表示を提示し、UI 操作は非活性化する。

---

### Requirement 3: アバター候補一覧 UI

**Objective:** 配信オペレーターとして、利用可能なアバター候補を「選べるキャラクター」として一覧表示し、ゲームのキャラクター選択画面のように直感的に選べる UI を得たい。そうすれば配信直前のアバター差し替えが素早く行える。

**Note:** アバター識別は Addressables の安定アドレス（string key）で行う（CS-4）。実際のアセットロードは `ui-toolkit-shell` の非同期ロード基盤（UI-6）経由とし、UI は識別子・表示名・サムネイル相当のメタデータのみで一覧を構成する（本体アセットの事前全ロードは要求しない）。

#### Acceptance Criteria

1. The Character Selection Tab shall 利用可能なアバター候補の一覧（アバター識別子、表示名、任意でサムネイル相当のメタデータ）をメイン出力側から **Request/Response** または **state** で取得する（具体方式は設計フェーズで確定、topic 例: `avatars/catalog`）。
2. The Character Selection Tab shall 取得したアバター候補を **候補一覧 UI**（ゲームのキャラクター選択画面風のグリッドまたはリスト）として表示する。
3. The Character Selection Tab shall 各候補項目に、アバター識別子に対応する **表示名**（CS-4）を可視化する。
4. The Character Selection Tab shall **各アバターのストアドサムネイル**（Addressables に同梱される PNG 等、アバター識別子から規約に従って導出したキーで解決）を候補一覧 UI に表示する（see CS-13）。サムネイル描画は `ui-toolkit-shell` の非同期ロード基盤（UI-6）経由で行い、読込中はプレースホルダを表示する。
4a. If 特定アバターのサムネイルが Addressables で解決できなかった場合, the Character Selection Tab shall **デフォルトサムネイル画像**（本 spec パッケージに同梱）にフォールバックし、診断ログに記録する（see CS-13）。
5. When オペレーターが候補項目を選択したとき、the Character Selection Tab shall 次のステップ（どの Slot に割り当てるかの選択、または既選択 Slot への即時割当）に進める UI フローを提供する（see Requirement 4）。
6. The Character Selection Tab shall アバター識別子として **Addressables の安定 key**（CS-4）を保持し、UI 上ではユーザー可読の表示名を用いる（識別子と表示名を分離して管理する）。
7. If アバター候補メタデータの取得に失敗した場合, the Character Selection Tab shall 候補一覧領域に再試行可能なエラー表示を提示し、本タブ全体および他タブの動作を阻害しない。
8. If アバター候補に重複した識別子が含まれていた場合, the Character Selection Tab shall 重複を検出し、診断ログへ記録したうえで一意化してから UI に反映する。
9. The Character Selection Tab shall 候補一覧を、メイン出力側から **state 更新**（アバター追加・削除）が通知されたとき自動更新する（再起動を要求しない）。

---

### Requirement 4: Slot ↔ アバター割当操作

**Objective:** 配信オペレーターとして、プレイヤーカード（Slot）とアバター候補を結び付ける操作を、ドラッグ相当・タップ相当の直感的な動作で完結させたい。そうすれば配信前リハーサル・本番中のアバター差し替えを短時間で行える。

**Note:** 割当は連続値ではなく「どのアバターを選んだか」という選択状態であるため、**本タブでは `PublishState`（coalesce 対象）で送信する**（CS-6）。割当直後に別アバターへ変更された場合、中間値は視覚上スキップされても構わない（最終選択が反映されれば目的を達する）。`reset`（割当解除）は離散操作として `PublishEvent` で送る。

#### Acceptance Criteria

1. The Character Selection Tab shall 任意の Slot に対して任意のアバター候補を割当するための UI フロー（例: カード選択 → 候補選択 → 確定、またはカード ⇔ 候補のドラッグ相当）を提供する。
2. When オペレーターが Slot へアバターを割り当てる操作を確定したとき、the Character Selection Tab shall `ui-toolkit-shell` の Command 送信 API を介して **state コマンド**（topic 例: `slot/{slotId}/assignment`）を送信し、payload にアバター識別子（Addressables key）を含める（see CS-4, CS-6, CS-7, UI-5）。
3. When オペレーターが Slot の割当解除（reset）を操作したとき、the Character Selection Tab shall `ui-toolkit-shell` の Command 送信 API を介して **event コマンド**（topic 例: `slot/{slotId}/command`、payload で `reset` を指定）を送信する（see CS-3, CS-6, CS-7）。
4. When オペレーターが Slot のアバター再読込（reload）を操作したとき、the Character Selection Tab shall **event コマンド**（payload で `reload` を指定）を送信し、UI 側では当該カードをローディング表示に切り替える。
5. While 割当操作の確定から メイン出力側の適用完了通知を待機している間, the Character Selection Tab shall 該当プレイヤーカード上に進行中表示（スピナー等）を提示し、同一 Slot への重複操作を抑止または直列化する。
6. When メイン出力側から Slot 状態の反映完了 state（または完了イベント）が通知されたとき、the Character Selection Tab shall 該当プレイヤーカードの割当表示を更新し、進行中表示を解除する。
7. The Character Selection Tab shall 複数の Slot に対して並列に割当操作を受け付け、同時進行中でも UI をブロックしない。
8. If 割当 state コマンド送信後に一定時間（設計フェーズで確定）メイン出力側からの反映通知が届かなかった場合, the Character Selection Tab shall 進行中表示をタイムアウト扱いに変更し、警告を提示したうえでオペレーターが再試行できる状態に戻す（UI クラッシュ・描画停止を発生させない）。
9. If メイン出力側から割当失敗イベント（アバター解決不能、RAC 側エラー等）が通知された場合, the Character Selection Tab shall 該当プレイヤーカードをエラー状態（Requirement 2 Acceptance Criteria 7）に切り替え、失敗事由を UI 側診断領域で参照可能にする（see CS-11, Requirement 7）。
10. The Character Selection Tab shall 同一 Slot への連続割当操作について、`PublishState` の coalesce 特性により最終値のみがメイン出力で採用されることを UI 側でも前提に据え、中間操作の副作用を UI 状態として蓄積しない（冪等な UI 状態設計）。

---

### Requirement 5: アバター個別設定 UI

**Objective:** 配信オペレーターとして、アバターごとに提供される個別設定（表情バイアス、ボーンスケール、ブレンドシェイプ重み等）を、アバター種別を問わず統一された UI 体験で操作したい。そうすればアバター追加のたびに UI を手書きする手間を省き、設定項目の追加・削除にも UI が自動追従する。

**Note:** 設定スキーマの権威は RAC 側（CS-5）。UI 側は設定メタデータを IPC（Request）で取得し、メタデータ（項目名・型・レンジ・既定値）に従って共通 UI コンポーネントライブラリ（UI-4: スライダー、カラーピッカー、番号付きリスト、トグルグループ等）から適切なコントロールを動的に生成する。値の変更は `PublishState`（CS-6）で送信する。

#### Acceptance Criteria

1. When オペレーターがプレイヤーカードから個別設定を開いたとき、the Character Selection Tab shall 当該 Slot に割当済みアバターの **設定メタデータ** を Request でメイン出力側から取得する（topic 例: `slot/{slotId}/settings/schema`、see UI-5, CS-5）。
2. The Character Selection Tab shall 取得した設定メタデータ（項目名・型・レンジ・既定値・表示名等）に従って、`ui-toolkit-shell` の共通 UI コンポーネントライブラリ（UI-4）から適切なコントロールを **動的に生成** する（静的な UI を手書きしない）。
3. When オペレーターが設定値を変更したとき、the Character Selection Tab shall 設定項目単位のトピック（topic 例: `slot/{slotId}/settings/{key}`、see CS-7）に対して `PublishState` を送信する（see CS-6）。
4. The Character Selection Tab shall 高頻度連続値の変更（スライダーのドラッグ中等）に対して、`PublishState` の coalesce 特性（D-7）に任せることを前提に、UI 側での流量制御（スロットリング等）を最小限に留める。
5. While オペレーターが設定値をドラッグ等で連続変更している間, the Character Selection Tab shall メイン出力描画に干渉する同期処理を行わず、UI のレスポンス性を維持する。
6. The Character Selection Tab shall 設定メタデータで規定された値の範囲・型を UI 側でもバリデーションし、範囲外の送信を抑止する。
7. When メイン出力側から設定値の現在状態 state が通知されたとき、the Character Selection Tab shall UI 上の設定コントロール表示を当該値に追従更新する（ただしオペレーターが操作中のコントロールは設計フェーズで確定する方針で競合解消する）。
8. Where 設定メタデータに「プリセット適用」等の離散操作が含まれる場合, the Character Selection Tab shall 当該操作を `PublishEvent` として送信し、連続値とは別トピックで扱う（see CS-6）。
9. If 設定メタデータ取得 Request がタイムアウトまたは失敗した場合, the Character Selection Tab shall 個別設定領域にエラー表示を提示し、再試行 UI を提供したうえで、本タブ全体の動作を阻害しない。
10. When オペレーターが Slot のアバターを切り替えたとき、the Character Selection Tab shall 旧アバターの設定 UI を破棄し、新アバターの設定メタデータを再取得して UI を再生成する（スキーマがアバターごとに異なる可能性を前提とする）。
11. The Character Selection Tab shall 設定 UI 生成時のメタデータ不整合（必須フィールド欠落、未知の型等）を診断ログへ記録し、当該項目のみスキップして他項目の UI を提示する。

---

### Requirement 6: Addressables ベースのアバターアセットロード連携

**Objective:** 配信オペレーターとして、割当操作を行ったらアバターアセットが裏で非同期ロードされ、ロード完了後にメイン出力へ反映されるフローを、UI 側のフリーズなしで体験したい。そうすれば大きなアバターアセットでも UI 操作が止まらず、配信継続性を損なわない。

**Note:** UI-6 により追加アセットは Addressables 経由、非同期ロード API は `ui-toolkit-shell` が提供する。UI 側はトリガと進捗表示のみを担当し、実 I/O はシェル基盤に委譲する。実際のアバター Instantiate・RAC への設定はメイン出力側で行われるが、Addressables のキー解決可否チェックや UI プレビュー用のメタデータ取得は UI 側でも発生し得る（設計フェーズで境界を確定）。

#### Acceptance Criteria

1. The Character Selection Tab shall アバターアセットの本体ロードをメイン出力側に委ねる設計とし、UI 側ではアバター識別子（Addressables key、CS-4）のみを送信する（see Requirement 4）。
2. Where UI 側でアバターサムネイル・プレビューメタデータ等の補助アセットを読み込む必要がある場合, the Character Selection Tab shall `ui-toolkit-shell` の非同期ロード API（UI-6）経由でのみ読み込み、素の AssetBundle API や Unity 同期 API を呼び出さない。
3. When 非同期ロードが開始したとき、the Character Selection Tab shall 該当 UI 要素にプレースホルダ表示または非活性化を適用し、オペレーターの他操作は継続可能な状態を維持する。
4. When 非同期ロードが完了したとき、the Character Selection Tab shall Completion コールバック（Unity メインスレッド配信、D-3）で UI を更新する。
5. If 非同期ロードが失敗した場合, the Character Selection Tab shall 失敗事由を診断ログに記録し、当該 UI 要素にエラー表示を提示したうえで、タブ全体の動作を阻害しない（see Requirement 7）。
6. The Character Selection Tab shall Addressables key に対するロードを **重複起動しない** よう、`ui-toolkit-shell` の重複抑止機構（UI Requirement 4 Acceptance Criteria 7）に準拠して同一キー再要求を安全に扱う。
7. Where 利用者プロジェクトが Addressables Groups にアバターを登録していない状態で起動した場合, the Character Selection Tab shall アバター候補一覧が空であることをオペレーターに明示的に伝え、Slot 割当操作を非活性化する（see Requirement 3 第 7 項）。
8. The Character Selection Tab shall アバター本体 Instantiate のタイミング・寿命管理はメイン出力側（RAC 含む）の責務であることを前提とし、UI 側で本体 GameObject の参照を保持しない。

---

### Requirement 7: 失敗・縮退ハンドリングと不可用アバター対応

**Objective:** 配信オペレーターとして、アバターアセットが壊れていた・存在しなかった・RAC がエラーを返した等の異常時でも、タブや UI シェル全体がクラッシュせず、問題箇所だけが縮退表示される状態を得たい。そうすれば配信中の部分的な障害が全体停止に発展せず、運用継続性を確保できる。

**Note:** 本要件は CS-11 の方針（不可用アバター→ empty + 警告、他 Slot は継続）を具現化し、`output-renderer-shell` Requirement 5（メイン出力描画にエラー UI を出さない）・`ui-toolkit-shell` Requirement 9（フェイルセーフ）と整合する。

#### Acceptance Criteria

1. If Addressables key に対応するアバターが解決不能（登録なし・バージョン不一致等）であった場合, the Character Selection Tab shall 該当 Slot を empty 状態に戻し、プレイヤーカード上に警告バッジを表示する（see CS-11）。
2. If メイン出力側から RAC 由来のエラー event（アバター読込失敗、モーションパイプライン初期化失敗等）が通知された場合, the Character Selection Tab shall 該当 Slot をエラー状態（Requirement 2 Acceptance Criteria 7）に切り替え、他 Slot の表示・操作は継続する。
3. If `ui-toolkit-shell` の非同期ロードが失敗した場合, the Character Selection Tab shall 当該 UI 要素のみをエラー表示に切り替え、タブ全体のレンダリング・他の UI 要素の応答性を維持する（see Requirement 6）。
4. If Command 送信 API が接続未確立エラーまたはサイズ上限超過エラー（D-11）を返した場合, the Character Selection Tab shall エラーを UI 側診断領域に記録し、UI クラッシュ・描画停止を発生させない（see UI-5）。
5. If 設定値バリデーションで範囲外入力が発生した場合, the Character Selection Tab shall 送信を抑止し、UI 上でバリデーションエラーを該当コントロール近傍に表示する（see Requirement 5 第 6 項）。
6. The Character Selection Tab shall いかなる失敗経路においても、メイン出力（Display 2+）へ警告・エラー UI を描画しない（see OR-1 の UI 側責務、ui-toolkit-shell Requirement 11 第 7 項）。
7. While メイン出力側との IPC 接続が切断している間, the Character Selection Tab shall 割当・設定 UI を安全に非活性化または保留状態に切り替え、接続回復後に復帰可能な状態を維持する（see ui-toolkit-shell Requirement 9）。
8. When IPC 接続が回復したとき、the Character Selection Tab shall Slot 一覧およびアバター候補一覧を再取得し、UI を現時点のメイン出力側状態に同期する。
9. The Character Selection Tab shall 失敗経路で発生した診断情報（失敗トピック・対象 Slot 識別子・失敗事由）をログ出力し、UI 側診断領域からも参照可能にする（see Requirement 9）。

---

### Requirement 8: 設定の永続化と復元

**Objective:** 配信オペレーターとして、前回のタブ終了時点の「Slot ↔ アバター割当」「アバター個別設定値」が、次回起動時に自動で復元される状態を得たい。そうすれば毎日の配信準備を短縮でき、割当漏れによる事故も減らせる。

**Note:** UI-7 により `ui-toolkit-shell` 自体は UI 状態を永続化しないが、タブ固有設定の永続化は本 spec の責務（spec Project Description の明記および CS-8 参照）。保存対象は CS-8 で限定し、タイミングは CS-9、復元経路は CS-10 に従う。保存ファイルの配置・フォーマット（JSON / バイナリ等）は設計フェーズで確定する。

#### Acceptance Criteria

1. The Character Selection Tab shall 永続化対象を **「Slot ↔ アバター割当」** および **「各 Slot のアバター個別設定値」** とし、これらを **名前付きプリセット単位** で保存する（see CS-8, CS-12）。
1a. The Character Selection Tab shall 複数のプリセットを保持し、オペレーターが以下のプリセット操作を UI から実行できるようにする：**新規作成（create）**、**名前変更（rename）**、**複製（duplicate）**、**削除（delete）**、**アクティブ化（activate/switch）**（see CS-12）。
1b. The Character Selection Tab shall 現在アクティブなプリセット名を UI 上に明示的に表示する（see CS-12）。
1c. When オペレーターがアクティブプリセットを切り替えたとき、the Character Selection Tab shall 切替先プリセットの内容を通常の state コマンド経路（Requirement 4, 5）で送信し、メイン出力側の Slot / 設定を一括適用する（see CS-10, CS-12）。
1d. If プリセット新規作成時に既存プリセットと重複する名前が指定された場合, the Character Selection Tab shall 作成を拒否してバリデーションエラーを UI に表示する（see CS-12）。
2. The Character Selection Tab shall 永続化対象外とするもの（Slot 個数、RAC のグローバル設定、タブ切替状態、ウィンドウ配置等）を保存ファイルに含めない（see CS-8, UI-7）。
3. When 永続化対象の値（割当・個別設定）が変更されたとき、the Character Selection Tab shall 変更を内部バッファに蓄積し、**デバウンス**（具体値は設計フェーズで確定）経過後にファイルへフラッシュする（see CS-9）。
4. When Unity アプリケーションが正常終了（スタンドアロンの OnApplicationQuit、PlayMode 停止等）を迎えたとき、the Character Selection Tab shall 保留中の未フラッシュ変更をファイルへ書き出す（see CS-9, D-9）。
5. When 本タブが起動し、`core-ipc-foundation` の IPC 接続が確立したとき、the Character Selection Tab shall 保存ファイルを読み込み、各 Slot の割当・個別設定を **通常の state コマンド経路**（Requirement 4, Requirement 5）で送信して復元する（see CS-10）。
6. If 保存ファイルが存在しない（初回起動）場合, the Character Selection Tab shall 復元処理をスキップし、メイン出力側の現在状態をそのまま UI に反映する。
7. If 保存ファイルの読み込みまたはパースに失敗した場合, the Character Selection Tab shall エラーを診断ログに記録し、破損ファイルをバックアップ（リネーム等）したうえで初回起動扱いにフォールバックする。
8. If 保存されたアバター識別子（Addressables key）がアバター候補一覧に存在しなかった場合, the Character Selection Tab shall 該当 Slot を empty 状態に戻し、プレイヤーカード上に「アバター解決不能」警告を表示する（see CS-11, Requirement 7）。
9. If 保存ファイル書き込みに失敗した場合（ディスク容量不足、権限エラー等）, the Character Selection Tab shall エラーを診断ログに記録し、UI 上に保存失敗通知を提示したうえで次回変更時に再試行する（UI クラッシュ・描画停止を発生させない）。
10. The Character Selection Tab shall 保存ファイルの配置・フォーマットを設計フェーズで確定することを要件として明記し、利用者プロジェクトでの配置場所（Application.persistentDataPath 配下等）が差し替え可能な構造を維持する。
11. When 復元 state コマンド送信中に一部 Slot の送信が失敗した場合, the Character Selection Tab shall 失敗 Slot のみを empty／エラー状態で UI に反映し、他 Slot の復元は継続する（see CS-11）。

---

### Requirement 9: 観測性・診断可能性

**Objective:** 開発者・配信運用者として、本タブで発生する不具合が「UI 起因」「IPC 送受信起因」「RAC 起因」「Addressables 起因」「永続化 I/O 起因」のいずれかを即座に切り分けたい。そうすれば問題切り分けに要する時間を最小化し、本番配信中でも迅速に対応できる。

**Note:** 本要件の診断出力は `output-renderer-shell` Requirement 5 および `ui-toolkit-shell` Requirement 11 第 7 項に従い、UI 側（Display 1）またはコンソールへのみ流し、メイン出力（Display 2+）に一切描画しない。

#### Acceptance Criteria

1. The Character Selection Tab shall 本タブの初期化・UIDocument アタッチ完了・アバター候補取得・Slot 一覧購読登録・永続化読込の各段階の開始・完了・失敗をログ出力する。
2. The Character Selection Tab shall Slot ↔ アバター割当操作（state 送信）およびアバター個別設定変更（state 送信）の送信元 Slot 識別子・topic・送信時刻をログレベルに応じて出力する。
3. The Character Selection Tab shall 離散操作（reset, reload, プリセット適用等の event 送信）の対象 Slot 識別子・操作種別・送信時刻をログ出力する。
4. When メイン出力側から RAC 由来のエラー event を受信したとき、the Character Selection Tab shall 対象 Slot 識別子・エラー種別・受信時刻を診断ログに記録する。
5. When 非同期ロード（Addressables 経由）の失敗イベントを受信したとき、the Character Selection Tab shall 対象 Addressables key・失敗事由を診断ログに記録する（see Requirement 6）。
6. When 永続化ファイルの読込・書込に失敗したとき、the Character Selection Tab shall ファイル識別子・失敗事由を診断ログに記録する（see Requirement 8）。
7. The Character Selection Tab shall 診断ログを Unity コンソールまたは UI 側診断領域（`ui-toolkit-shell` が提供するもの）にのみ流し、メイン出力サーフェスへ一切描画しない。
8. Where 開発者がデバッグ用途で詳細ログを必要とする場合, the Character Selection Tab shall ログレベルを外部から切替可能にする（`ui-toolkit-shell` Requirement 11 第 8 項と整合）。
9. The Character Selection Tab shall 診断に必要な最小限の状態（現在の Slot 数、割当済み Slot 数、エラー Slot 数、進行中の操作件数、永続化最終保存時刻、IPC 接続状態）を外部から取得可能な形で公開する。

---

### Requirement 10: スタンドアロンビルドと Unity Editor PlayMode の両対応

**Objective:** 配信運用者および開発者として、ビルド後のスタンドアロン実行時と Unity Editor PlayMode の両方で、本タブの挙動（UI 表示、割当操作、設定操作、永続化）が同一であることを得たい。そうすれば開発中の検証と本番運用の挙動差を最小化でき、Editor PlayMode で配信前リハーサルが完結する。

**Note:** D-9 の継承により、Editor では PlayMode 開始〜停止の区間のみ常駐し、Edit モードでは常駐しない。ドメインリロードに跨る状態維持は試みない。永続化ファイルは Edit モードからも読めるファイルとして存在するが、本タブの処理ロジック自体は PlayMode 内で動作する。

#### Acceptance Criteria

1. When Unity アプリケーションがスタンドアロンビルドとして起動し、`ui-toolkit-shell` のプリロードが完了したとき、the Character Selection Tab shall 本タブの UI 初期化・Slot 一覧購読・永続化復元を自動的に実施する。
2. When Unity Editor が PlayMode に入ったとき、the Character Selection Tab shall スタンドアロン時と同一手順で UI 初期化・Slot 一覧購読・永続化復元を実施する（see D-9）。
3. When Unity Editor が PlayMode を終了したとき、the Character Selection Tab shall 保留中の未フラッシュ永続化データを書き出し、購読を解除し、内部状態をクリーンアップして Edit モードに残留物を残さない（see D-9, Requirement 8 第 4 項）。
4. While PlayMode の開始と停止が繰り返される間, the Character Selection Tab shall 購読重複・UI 要素重複生成・永続化ファイルのロック残存を発生させず、毎回クリーンに再初期化する。
5. The Character Selection Tab shall Unity Editor の **Edit モード** では本タブの実行時ロジック（UI 初期化、IPC 購読、永続化読込等）を起動しない（see D-9）。
6. The Character Selection Tab shall ドメインリロードに跨る状態維持を試みず、PlayMode 開始のたびに永続化ファイルから復元する（see D-9, CS-10）。
7. The Character Selection Tab shall スタンドアロン時と Editor PlayMode 時で、オペレーターから見た UI 挙動・割当操作レイテンシ特性・永続化挙動を同一に保つ。

---

### Requirement 11: 本 spec 単体での検証可能性

**Objective:** spec オーナーとして、本タブを `output-renderer-shell` 側の RAC アダプタ実装や実アバターアセットがそろう前に検証したい。そうすれば Wave 3 の 3 タブを並行開発する際に、モックを介して本タブの UI と IPC 契約を独立に検証できる。

**Note:** 本要件は `ui-toolkit-shell` Requirement 10（単体検証）と `core-ipc-foundation` Requirement 8（自己ループ）を活用する。RAC 本体をモックアウトするため、メイン出力側で RAC の代わりに応答するテストダブルを用意することで、本タブの全挙動を実行可能にする。

#### Acceptance Criteria

1. The Character Selection Tab shall 実アバターアセットおよび RAC 本体が実接続されていなくても、IPC 契約上のモック応答（アバター候補一覧、Slot 一覧、設定メタデータ、state 反映応答、エラー event）を与えるテストダブルと連携して、UI の全表示・操作経路を実行できる構造とする。
2. The Character Selection Tab shall `core-ipc-foundation` の自己ループ機構（spec #1 Requirement 8）と `ui-toolkit-shell` のモック受容構造（UI Requirement 10 第 6 項）を利用して、Command 送信 API 経由の state / event / request を自プロセス内で送受信するテストケースを提供する。
3. The Character Selection Tab shall 永続化 I/O 部分について、ファイルシステムに依存しない差し替え可能なストレージ抽象（メモリ上ダブル等）を受け入れる構造を備える。
4. The Character Selection Tab shall Unity Editor PlayMode での手動検証手順（最小サンプルシーンまたは同等物、モックアバター候補・モック Slot を含む）を提供する。
5. When 本タブ単体のテスト実行が行われたとき、the Character Selection Tab shall 次の挙動を検証するテストケースを提供する：Slot 一覧の UI 反映、Slot empty 状態の表示、アバター割当 state 送信、reset/reload event 送信、アバター個別設定スキーマからの動的 UI 生成、設定値変更 state 送信、不可用アバター復元時の empty + 警告挙動、IPC 切断中のフェイルセーフ挙動、永続化の書込・読込・破損フォールバック。
6. The Character Selection Tab shall テスト時に Addressables の代わりに差し替え可能なアセット解決モック（アバター候補・サムネイル・存在判定）を受け入れる構造を備える。
7. The Character Selection Tab shall テスト時に時刻（デバウンスタイマー、タイムアウト等）を制御可能にするための時刻抽象を受け入れる構造を備える（設計フェーズで具体 API を確定）。

---

## Dig Summary

- **ラウンド数**: 1 ラウンド（A 案、要件レベル厳選、上流 spec 決定の積極的継承）
- **本 spec 固有の決定**: 11 件（CS-1〜CS-11）
- **継承**:
  - `core-ipc-foundation` の D-1（単一 Unity アプリ + LocalHost）、D-3（メインスレッド配信）、D-4（UI クライアント）、D-5（JSON / WebSocket）、D-9（PlayMode のみ常駐）、D-10（PublishState / PublishEvent）、D-11（1 MB 上限）
  - `output-renderer-shell` の OR-1（Display 2 フォールバック時の UI 警告）、OR-2（state 競合 last-write-wins）
  - `ui-toolkit-shell` の UI-1（起動時プリロード）、UI-2（表示/非表示切替）、UI-3（USS 差し替え）、UI-4（共通 UI コンポーネントライブラリ）、UI-5（Command 送信 API）、UI-6（Addressables）、UI-7（タブ共通 UI 状態永続化なし）
- **主要な発見（本 spec 固有）**:
  - RAC ランタイム本体をメイン出力側に常駐させ、UI 側はコマンド発行層に徹する構造（CS-1）。これにより「UI から直接メイン出力側オブジェクトを触らない」（docs/requirements.md §3.2）契約と、将来の LAN/WebUI クライアント対応（D-4）が同時に成立する。
  - Slot はアバター未割当（empty）状態を持ち得ることを明示的に要件化（CS-3）。配信現場の実運用（アクター参加済だがアバター未選）を safely に表現し、null との区別を設計上担保する。
  - アバター識別は Addressables key を第一級の識別子として、表示名と分離（CS-4）。Prefab GUID やアセットパスではなく、利用者プロジェクトが Groups Window で管理する安定 key を採用することで、ビルド差分やローカライズに強い構造にする。
  - 個別設定 UI は RAC の設定メタデータから動的生成（CS-5）。アバター追加のたびに UI 改修を行う運用を避け、共通 UI コンポーネントライブラリ（UI-4）を最大限再利用する。
  - 連続値は state、離散操作は event の役割分担を要件レベルで固定（CS-6）。UI 高頻度操作時のキュー溢れを `core-ipc-foundation` の coalesce（D-7）に構造的に任せ、UI 側の流量制御コードを最小化。
  - 永続化の保存対象を「割当＋個別設定」に絞り、タブ共通 UI 状態は UI-7 に従い対象外（CS-8）。保存タイミングはデバウンス即時保存（CS-9）、復元は通常 state 経路（CS-10）で、メイン出力側のハンドラ実装を 1 本化。
  - 不可用アバターは empty + 警告、他 Slot 継続（CS-11）。配信直前のアセット差し替え事故に対して、部分縮退で運用継続性を担保。

- **残留リスク（設計フェーズで継続検討）**:
  - R-1: トピック命名規約の具体値（`slot/{slotId}/assignment` 等の prefix / ID フォーマット）は設計フェーズで確定。変更時の互換性影響を見積もる。
  - R-2: 設定メタデータ API（CS-5）の具体スキーマ。RAC v0.1.0 の実 API を設計フェーズで棚卸し、UI 側で必要な項目（型・レンジ・既定値・表示名・単位等）が揃うかを確認する。不足があれば当面は最小共通項目セットで縮退。
  - R-3: 割当・設定操作のタイムアウト値（Requirement 4 第 8 項、Requirement 5 第 9 項）の具体値。D-8 の 5 秒を踏襲するか、本タブ固有で短縮するか。
  - R-4: 永続化ファイルのフォーマット（JSON / バイナリ / ScriptableObject）、配置（Application.persistentDataPath 配下の相対パス）、バージョニング方針。破損時のバックアップ命名規約も含む。
  - R-5: デバウンス時間（Requirement 8 第 3 項）の具体値。配信現場の操作頻度と I/O 負荷を見積もり、一般的な値（例: 500ms）を置く想定。
  - R-6: 操作中コントロールへの state 逆流時の競合解消（Requirement 5 第 7 項）。オペレーターが操作中の項目については逆流 state を一時抑止する等の UX 方針を確定。
  - R-7: 同一 Slot への割当操作の重複抑止（Requirement 4 第 5 項）は UI 側直列化か coalesce 前提かの設計判断。last-write-wins（OR-2）と整合する方針を選ぶ。
  - R-8: UI 上の「警告バッジ」「エラー状態」「接続断通知」の具体 UX（配色、アイコン、テキスト）は `ui-toolkit-shell` のスキン差し替え方針（UI-3）との整合性を維持しつつ設計フェーズで確定。
  - R-9: アバター候補一覧のサムネイル提供経路（Addressables 経由か別 state か、サムネイルのサイズ規約）は設計フェーズで確定。
  - R-10: RAC バージョンアップ時の追従方針。v0.1.0（Unity 6000.3.10f1+）を前提とするが、API 変更時の互換層の要否を設計フェーズで検討。

## Dig Summary（本 spec 固有の追加分）

- **本 spec 固有の追加決定**: CS-12（名前付きプリセット複数保持）、CS-13（ストアドサムネイルによるプレビュー）
- **継承**: core-ipc-foundation の D-1, D-3, D-4, D-5, D-9, D-10, D-11、output-renderer-shell の OR-1, OR-2、ui-toolkit-shell の UI-1〜UI-7
- **残留リスク**:
  - R-CS-12-1: プリセット切替中にメイン出力側への一括 state 送信が途中で失敗した場合の整合性（ロールバック / 部分適用のまま継続）は設計フェーズで確定
  - R-CS-12-2: プリセット数の上限 / 保存ファイル肥大化の扱い
  - R-CS-13-1: サムネイル未設定アバター用のデフォルト画像のデザインは利用者側 / パッケージ同梱のどちらで提供するかを設計フェーズで確定
